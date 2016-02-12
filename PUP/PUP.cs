using IFS.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace IFS
{
    /// <summary>
    /// All of the different types of PUPs.
    /// </summary>
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

        // Where is User
        WhereIsUserRequest = 152,
        WhereIsUserResponse = 153,
        WhereIsUserErrorReply = 154,
        
        // Alto Boot
        SendBootFileRequest = 164,
        BootDirectoryRequest = 165,
        BootDirectoryReply = 166,

        // User authentication
        AuthenticateRequest = 168,
        AuthenticatePositiveResponse = 169,
        AuthenticateNegativeResponse = 170,

        // Gateway Information Protocol
        GatewayInformationRequest = 128,
        GatewayInformationResponse = 129
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
            Socket = Helpers.ReadUInt(rawData, offset + 2);
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

        public override string ToString()
        {
            return String.Format("Net {0} Host {1} Socket {2}", Network, Host, Socket);
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
        /// <param name="contentsContainsGarbageByte">
        /// True if the "contents" array contains a garbage (padding) byte that should NOT
        /// be factored into the Length field of the PUP.  This is necessary so we can properly
        /// support the Echo protocol; since some PUP tests that check validation require that the
        /// PUP be echoed in its entirety (including the supposedly-ignorable "garbage" byte on
        /// odd-length PUPs) we need to be able to craft a PUP with one extra byte of content that's
        /// otherwise ignored...
        /// 
        /// TODO: Update to use Serialization code rather than packing bytes by hand.
        /// </param>
        /// 
        public PUP(PupType type, UInt32 id, PUPPort destination, PUPPort source, byte[] contents, bool contentsContainsGarbageByte)
        {
            _rawData = null;

            // Ensure content length is <= 532 bytes.  (Technically larger PUPs are allowed,
            // but conventionally they are not used and I want to keep things safe.)
            if (contents.Length > MAX_PUP_SIZE)
            {
                throw new InvalidOperationException("PUP size must not exceed 532 bytes.");
            }

            //
            // Sanity check:
            // "contentsContainGarbageByte" can ONLY be true if "contents" is of even length
            //
            if (contentsContainsGarbageByte && (contents.Length % 2) != 0)
            {
                throw new InvalidOperationException("Odd content length with garbage byte specified.");
            }

            TransportControl = 0;
            Type = type;
            ID = id;
            DestinationPort = destination;
            SourcePort = source;

            // Ensure contents are an even number of bytes.
            int contentLength = (contents.Length % 2) == 0 ? contents.Length : contents.Length + 1;
            Contents = new byte[contents.Length];
            contents.CopyTo(Contents, 0);

            // Length is always the real length of the data (not padded to an even number)
            Length = (ushort)(PUP_HEADER_SIZE + PUP_CHECKSUM_SIZE + contents.Length);            

            // Stuff data into raw array
            _rawData = new byte[PUP_HEADER_SIZE + PUP_CHECKSUM_SIZE + contentLength];

            //
            // Subtract off one byte from the Length value if the contents contain a garbage byte.
            // (See header comments for function)
            if (contentsContainsGarbageByte)
            {
                Length--;
            }

            Helpers.WriteUShort(ref _rawData, Length, 0);
            _rawData[2] = TransportControl;
            _rawData[3] = (byte)Type;            
            Helpers.WriteUInt(ref _rawData, ID, 4);
            DestinationPort.WriteToArray(ref _rawData, 8);
            SourcePort.WriteToArray(ref _rawData, 14);
            Array.Copy(Contents, 0, _rawData, 20, Contents.Length);

            // Calculate the checksum and stow it
            Checksum = CalculateChecksum();
            Helpers.WriteUShort(ref _rawData, Checksum, _rawData.Length - 2);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="type"></param>
        /// <param name="id"></param>
        /// <param name="destination"></param>
        /// <param name="source"></param>
        /// <param name="contents"></param>
        public PUP(PupType type, UInt32 id, PUPPort destination, PUPPort source, byte[] contents) :
            this(type, id, destination, source, contents, false)
        {
            
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
        public PUP(MemoryStream stream, int length)
        {
            _rawData = new byte[length];
            stream.Read(_rawData, 0, length);

            // Read fields in.  TODO: investigate more efficient ways to do this.
            Length = Helpers.ReadUShort(_rawData, 0);

            // Sanity check size:
            if (Length > length)
            {
                throw new InvalidOperationException("Length field in PUP is invalid.");
            }            

            TransportControl = _rawData[2];
            Type = (PupType)_rawData[3];            
            ID = Helpers.ReadUInt(_rawData, 4);
            DestinationPort = new PUPPort(_rawData, 8);
            SourcePort = new PUPPort(_rawData, 14);

            int contentLength = Length - PUP_HEADER_SIZE - PUP_CHECKSUM_SIZE;
            Contents = new byte[contentLength];
            Array.Copy(_rawData, 20, Contents, 0, contentLength);

            // Length is the number of valid bytes in the PUP, which may be an odd number.
            // There are always an even number of bytes in the PUP, and the checksum
            // therefore begins on an even byte boundary.  Calculate the checksum offset
            // appropriately.  (Empirically, we could also just use the last word of the packet,
            // as the Alto PUP implementation never appears to pad any extra data after the PUP, but
            // this doesn't appear to be a requirement and I don't want to rely on it.)
            int checksumOffset = (Length % 2) == 0 ? Length - PUP_CHECKSUM_SIZE : Length - PUP_CHECKSUM_SIZE + 1;

            Checksum = Helpers.ReadUShort(_rawData, checksumOffset);

            // Validate checksum
            ushort cChecksum = CalculateChecksum();

            if (Checksum != 0xffff && cChecksum != Checksum)
            {
                // TODO: determine what to do with packets that are corrupted.
                Log.Write(LogType.Warning, LogComponent.PUP, "PUP checksum is invalid. (got {0:x}, expected {1:x})", Checksum, cChecksum);
            }

        }

        public byte[] RawData
        {
            get { return _rawData; }
        }

        private ushort CalculateChecksum()
        {
            uint sum = 0;

            // Sum over everything except the checksum word
            for (int i=0; i< _rawData.Length - PUP_CHECKSUM_SIZE; i+=2)
            {
                ushort nextWord = Helpers.ReadUShort(_rawData, i);
                //ushort nextWord = (ushort)((_rawData[i + 1] << 8) | _rawData[i]);

                // 2's complement add with "end-around" carry results in
                // 1's complement add
                sum += nextWord;

                uint carry = (sum & 0x10000) >> 16;
                sum = (ushort)(sum + carry);

                // Rotate left
                sum = sum << 1;
                carry = (sum & 0x10000) >> 16;
                sum = (ushort)(sum + carry);                
            }

            // Negative 0? convert to positive 0.
            if (sum == 0xffff)
            {
                sum = 0;
            }


            return (ushort)sum;
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

        public readonly static int MAX_PUP_SIZE = 532;
        public readonly static int PUP_HEADER_SIZE = 20;
        public readonly static int PUP_CHECKSUM_SIZE = 2;       
    }

    public static class Helpers
    {
        public static ushort ReadUShort(byte[] data, int offset)
        {
            return (ushort)((data[offset] << 8) | data[offset + 1]);
        }

        public static ushort ReadUShort(Stream s)
        {
            return (ushort)((s.ReadByte() << 8) | s.ReadByte());
        }

        public static UInt32 ReadUInt(byte[] data, int offset)
        {
            return (UInt32)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
        }

        public static UInt32 ReadUInt(Stream s)
        {
            return (UInt32)((s.ReadByte() << 24) | (s.ReadByte() << 16) | (s.ReadByte() << 8) | s.ReadByte());
        }

        public static void WriteUShort(ref byte[] data, ushort s, int offset)
        {
            data[offset] = (byte)(s >> 8);
            data[offset + 1] = (byte)s;
        }

        public static void WriteUShort(Stream st, ushort s)
        {
            st.WriteByte((byte)(s >> 8));
            st.WriteByte((byte)s);            
        }

        public static void WriteUInt(ref byte[] data, UInt32 s, int offset)
        {
            data[offset] = (byte)(s >> 24);
            data[offset + 1] = (byte)(s >> 16);
            data[offset + 2] = (byte)(s >> 8);
            data[offset + 3] = (byte)s;
        }

        public static void WriteUInt(Stream st, UInt32 s)
        {
            st.WriteByte((byte)(s >> 24));
            st.WriteByte((byte)(s >> 16));
            st.WriteByte((byte)(s >> 8));
            st.WriteByte((byte)s);
        }

        public static byte[] StringToArray(string s)
        {
            byte[] stringArray = new byte[s.Length];

            // We simply take the low 8-bits of each Unicode character and stuff it into the
            // byte array.  This works fine for the ASCII subset of Unicode but obviously
            // is bad for everything else.  This is unlikely to be an issue given the lack of
            // any real internationalization support on the IFS end of things, but might be
            // something to look at.
            for (int i = 0; i < stringArray.Length; i++)
            {
                stringArray[i] = (byte)s[i];
            }

            return stringArray;
        }

        public static string ArrayToString(byte[] a)
        {
            StringBuilder sb = new StringBuilder(a.Length);

            for (int i = 0; i < a.Length; i++)
            {
                sb.Append((char)(a[i]));
            }

            return sb.ToString();
        }
    }
}
