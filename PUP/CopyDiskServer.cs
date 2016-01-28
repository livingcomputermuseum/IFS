using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace IFS
{
    public enum CopyDiskBlock
    {
        Version = 1,
        SendDiskParamsR = 2,
        HereAreDiskParams = 3,
        StoreDisk = 4,
        RetrieveDisk = 5,
        HereIsDiskPage = 6,
        EndOfTransfer = 7,
        SendErrors = 8,
        HereAreErrors = 9,
        No = 10,
        Yes = 11,
        Comment = 12,
        Login = 13,
        SendDiskParamsW = 14,
    }

    struct VersionBlock
    {
        public VersionBlock(ushort version, string herald)
        {
            Version = version;
            Herald = new BCPLString(herald);

            Length = (ushort)((6 + herald.Length + 2) / 2);     // +2 for length of BCPL string and to round up to next word length
            Command = (ushort)CopyDiskBlock.Version;
        }

        public ushort Length;
        public ushort Command;
        public ushort Version;
        public  BCPLString Herald;        
    }

    public class CopyDiskServer : BSPProtocol
    {
        /// <summary>
        /// Called by dispatcher to send incoming data destined for this protocol.
        /// </summary>
        /// <param name="p"></param>
        public override void RecvData(PUP p)
        {
            throw new NotImplementedException();
        }

        public override void InitializeServerForChannel(BSPChannel channel)
        {
            // spwan new worker thread with new BSP channel
            Thread newThread = new Thread(new ParameterizedThreadStart(CopyDiskServerThread));
            newThread.Start(channel);
        }

        private void CopyDiskServerThread(object obj)
        {
            BSPChannel channel = (BSPChannel)obj;

            while(true)
            {
                // Retrieve length of this block (in bytes):
                int length = channel.ReadUShort() * 2;

                // Sanity check that length is a reasonable value.                
                if (length > 2048)
                {
                    // TODO: shut down channel
                    throw new InvalidOperationException(String.Format("Insane block length ({0})", length));
                }

                // Retrieve type            
                CopyDiskBlock blockType = (CopyDiskBlock)channel.ReadUShort();

                // Read rest of block                
                byte[] data = new byte[length];

                channel.Read(ref data, data.Length - 4, 4);

                switch(blockType)
                {
                    case CopyDiskBlock.Version:
                        VersionBlock vbIn = (VersionBlock)Serializer.Deserialize(data, typeof(VersionBlock));                        

                        Console.WriteLine("Copydisk client is version {0}, '{1}'", vbIn.Version, vbIn.Herald.ToString());                        

                        // Send the response:
                        VersionBlock vbOut = new VersionBlock(vbIn.Version, "IFS CopyDisk of 26-Jan-2016");
                        channel.Send(Serializer.Serialize(vbOut));                        
                        break;

                    case CopyDiskBlock.Login:

                        break;

                    default:
                        Console.WriteLine("Unhandled CopyDisk block {0}", blockType);
                        break;
                }
            }
        }
    }
}
