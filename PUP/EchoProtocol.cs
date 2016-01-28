using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IFS
{
    /// <summary>
    /// Implements the PUP Echo Protocol.
    /// </summary>
    public class EchoProtocol : PUPProtocolBase
    {
        public EchoProtocol()
        {

        }

        /// <summary>
        /// Called by dispatcher to send incoming data destined for this protocol
        /// </summary>
        /// <param name="p"></param>
        public override void RecvData(PUP p)
        {
            // If this is an EchoMe packet, we will send back an "ImAnEcho" packet.
            if (p.Type == PupType.EchoMe)
            {
                // Just send it back with the source/destination swapped.
                PUPPort localPort = new PUPPort(DirectoryServices.Instance.LocalHostAddress, p.SourcePort.Socket);

                //
                // An annoyance:  The Alto "puptest" diagnostic actually expects us to echo *everything* back including
                // the garbage byte on odd-length PUPs.  (Even though the garbage byte is meant to be ignored.)
                // So in these cases we need to do extra work and copy in the garbage byte.  Grr.
                //
                byte[] contents;
                bool garbageByte = (p.Contents.Length % 2) != 0;

                if (!garbageByte)
                {
                    // Even, no work needed
                    contents = p.Contents;
                }
                else
                {
                    // No such luck, copy in the extra garbage byte to make diagnostics happy.
                    contents = new byte[p.Contents.Length + 1];
                    p.Contents.CopyTo(contents, 0);
                    contents[contents.Length - 1] = p.RawData[p.RawData.Length - 3];
                }

                PUP echoPup = new PUP(PupType.ImAnEcho, p.ID, p.SourcePort, localPort, contents, garbageByte);
                PUPProtocolDispatcher.Instance.SendPup(echoPup);
            }
        }

    }
}
