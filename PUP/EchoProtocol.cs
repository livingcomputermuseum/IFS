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

                PUP echoPup = new PUP(PupType.ImAnEcho, p.ID, p.SourcePort, localPort);
                Dispatcher.Instance.SendPup(p);
            }
        }

    }
}
