using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace IFS
{
    public enum PupType
    {
        // Basic types
        EchoMe = 1,
        ImAnEcho = 2,
        ImABadEcho = 3,
        Error = 4,

        //  BSP/RFC types
        RFC = 8,
        Abort = 9,
        End = 10,
        EndReply = 11,
        Data = 16,
        AData = 17,
        Ack = 18,
        Mark = 19,
        Interrupt = 20,
        InterruptReply = 21,
        AMark = 22,

        // Misc. Services types
        StringTimeRequest = 128,
        StringTimeReply = 129,
        TenexTimeRequest = 130,
        TenexTimeReply = 131,
        AltoTimeRequestOld = 132,
        AltoTimeResponseOld = 133,
        AltoTimeRequest = 134,
        AltoTimeResponse = 135,

        // Network Lookup
        NameLookupRequest = 144,
        NameLookupResponse = 145,
        DirectoryLookupErrorReply = 146,
        AddressLookupRequest = 147,
        AddressLookupResponse = 148,
        
        // Alto Boot
        SendBootFileRequest = 164,
        BootDirectoryRequest = 165,
        BootDirectoryReply = 166,
    }

    public struct PUPPort
    {
        /// <summary>
        /// Builds a new port address given network, host and socket parameters
        /// </summary>
        /// <param name="network"></param>
        /// <param name="host"></param>
        /// <param name="socket"></param>
        public PUPPort(byte network, byte host, UInt32 socket)
        {
            Network = network;
            Host = host;
            Socket = socket;
        }

        /// <summary>
        /// Builds a new port address given a HostAddress and a socket.
        /// </summary>
        /// <param name="address"></param>
        /// <param name="socket"></param>
        public PUPPort(HostAddress address, UInt32 socket)
        {
            Network = address.Network;
            Host = address.Host;
            Socket = socket;
        }

        /// <summary>
        /// Builds a new port address from an array containing a raw port representaton
        /// </summary>
        /// <param name="rawData"></param>
        /// <param name="offset"></param>
        public PUPPort(byte[] rawData, int offset)
        {
            Network = rawData[offset];
            Host = rawData[offset + 1];
            Socket = Helpers.ReadUInt(rawData, 2);
        }

        /// <summary>
        /// Writes this address back out to a raw byte array at the specified offset
        /// </summary>
        /// <param name="rawData"></param>
        /// <param name="offset"></param>
        public void WriteToArray(ref byte[] rawData, int offset)
        {
            rawData[offset] = Network;
            rawData[offset + 1] = Host;
            Helpers.WriteUInt(ref rawData, Socket, offset + 2);
        }

        /// <summary>
        /// Same as above, but simply returns a new array instead of writing into an existing one.
        /// </summary>
        /// <returns></returns>
        public byte[] ToArray()
        {
            byte[] a = new byte[6];
            WriteToArray(ref a, 0);
            return a;
        }

        public byte Network;
        public byte Host;
        public UInt32 Socket;
    }

    public class PUP
    {
        /// <summary>
        /// Construct a new packet from the supplied data.
        /// </summary>
        /// <param name="rawPacket"></param>
        public PUP(PupType type, UInt32 id, PUPPort destination, PUPPort source, byte[] contents)
        {
            _rawData = null;

            // Ensure content length is <= 532 bytes.  (Technically larger PUPs are allowed,
            // but conventionally they are not used and I want to keep things safe.)
            if (contents.Length > MAX_PUP_SIZE)
            {
                throw new InvalidOperationException("PUP size must not exceed 532 bytes.");
            }

            TransportControl = 0;
            Type = type;
            ID = id;
            DestinationPort = destination;
            SourcePort = source;
            Contents = contents;
            Length = (ushort)(PUP_HEADER_SIZE + PUP_CHECKSUM_SIZE + contents.Length);

            // Stuff data into raw array
            _rawData = new byte[Length];
            Helpers.WriteUShort(ref _rawData, Length, 0);
            _rawData[2] = TransportControl;
            _rawData[3] = (byte)Type;
            Helpers.WriteUInt(ref _rawData, ID, 4);
            DestinationPort.WriteToArray(ref _rawData, 8);
            SourcePort.WriteToArray(ref _rawData, 14);
            Array.Copy(Contents, 0, _rawData, 20, Contents.Length);

            // Calculate the checksum and stow it
            Checksum = CalculateChecksum();
            Helpers.WriteUShort(ref _rawData, Checksum, Length - 2);
        }

        /// <summary>
        /// Same as above, but with no content (i.e. a zero-byte payload)
        /// </summary>
        /// <param name="type"></param>
        /// <param name="id"></param>
        /// <param name="destination"></param>
        /// <param name="source"></param>
        public PUP(PupType type, UInt32 id, PUPPort destination, PUPPort source) : 
            this(type, id, destination, source, new byte[0])
        {

        }

        /// <summary>
        /// Load in an existing packet from a stream
        /// </summary>
        /// <param name="stream"></param>
        public PUP(MemoryStream stream)
        {
            _rawData = new byte[stream.Length];
            stream.Read(_rawData, 0, (int)stream.Length);

            // Read fields in.  TODO: investigate more efficient ways to do this.
            Length = Helpers.ReadUShort(_rawData, 0);

            // Sanity check size:
            if (Length > stream.Length)
            {
                throw new InvalidOperationException("Length field in PUP is invalid.");
            }

            TransportControl = _rawData[2];
            Type = (PupType)_rawData[3];
            ID = Helpers.ReadUInt(_rawData, 4);
            DestinationPort = new PUPPort(_rawData, 8);
            SourcePort = new PUPPort(_rawData, 14);
            Array.Copy(_rawData, 20, Contents, 0, Length - PUP_HEADER_SIZE - PUP_CHECKSUM_SIZE);
            Checksum = Helpers.ReadUShort(_rawData, Length - 2);

            // Validate checksum
            ushort cChecksum = CalculateChecksum();

            if (cChecksum != Checksum)
            {
                throw new InvalidOperationException(String.Format("PUP checksum is invalid. ({0} vs {1}", Checksum, cChecksum));
            }

        }

        public byte[] RawData
        {
            get { return _rawData; }
        }

        private ushort CalculateChecksum()
        {

            int i;

            //
            // This code "borrowed" from the Stanford PUP code
            // and translated roughly to C#
            //
            Cksum cksm;

            cksm.lcksm = 0;
            cksm.scksm.ccksm = 0;       // to make the C# compiler happy since it knows not of unions
            cksm.scksm.cksm = 0;

            for (i = 0; i < _rawData.Length - PUP_CHECKSUM_SIZE; i += 2)
            {
                ushort word = Helpers.ReadUShort(_rawData, i);

                cksm.lcksm += word;
                cksm.scksm.cksm += cksm.scksm.ccksm;
                cksm.scksm.ccksm = 0;
                cksm.lcksm <<= 1;
                cksm.scksm.cksm += cksm.scksm.ccksm;
                cksm.scksm.ccksm = 0;
            }

            if (cksm.scksm.cksm == 0xffff)
            {
                cksm.scksm.cksm = 0;
            }

           return cksm.scksm.cksm;             
        }

        // Structs used by CalculateChecksum to simulate
        // a C union in C#
        [StructLayout(LayoutKind.Explicit)]
        struct Scksum
        {
            [FieldOffset(0)]
            public ushort ccksm;
            [FieldOffset(2)]
            public ushort cksm;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct Cksum
        {
            [FieldOffset(0)]
            public ulong lcksm;

            [FieldOffset(0)]
            public Scksum scksm;
        }

        public readonly ushort Length;
        public readonly byte TransportControl;
        public readonly PupType Type;
        public readonly UInt32 ID;
        public readonly PUPPort DestinationPort;
        public readonly PUPPort SourcePort;            
        public readonly byte[] Contents;
        public readonly ushort Checksum;        

        private byte[] _rawData;

        private const int MAX_PUP_SIZE = 532;
        private const int PUP_HEADER_SIZE = 20;
        private const int PUP_CHECKSUM_SIZE = 2;

        
    }

    public static class Helpers
    {
        public static ushort ReadUShort(byte[] data, int offset)
        {
            return (ushort)((data[offset] << 8) | data[offset + 1]);
        }

        public static UInt32 ReadUInt(byte[] data, int offset)
        {
            return (UInt32)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
        }

        public static void WriteUShort(ref byte[] data, ushort s, int offset)
        {
            data[offset] = (byte)(s >> 8);
            data[offset + 1] = (byte)s;
        }

        public static void WriteUInt(ref byte[] data, UInt32 s, int offset)
        {
            data[offset] = (byte)(s >> 24);
            data[offset + 1] = (byte)(s >> 16);
            data[offset + 2] = (byte)(s >> 8);
            data[offset + 3] = (byte)s;
        }        
    }
}
