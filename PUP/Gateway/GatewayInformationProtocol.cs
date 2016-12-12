using IFS.Gateway;
using IFS.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IFS.Gateway
{
    public struct GatewayInformation
    {
        public byte TargetNet;
        public byte GatewayNet;
        public byte GatewayHost;
        public byte HopCount;
    }

    /// <summary>
    /// Gateway Information Protocol (see http://xeroxalto.computerhistory.org/_cd8_/pup/.gatewayinformation.press!1.pdf)
    /// </summary>
    public class GatewayInformationProtocol : PUPProtocolBase
    {
        public GatewayInformationProtocol()
        {
            // TODO:
            // load host tables, etc.
            // spin up thread that spits out a GatewayInformation PUP periodically.
        }

        /// <summary>
        /// Called by dispatcher to send incoming data destined for this protocol
        /// </summary>
        /// <param name="p"></param>
        public override void RecvData(PUP p)
        {
            switch (p.Type)
            {
                case PupType.GatewayInformationRequest:
                    SendGatewayInformationResponse(p);
                    break;

                default:
                    Log.Write(LogComponent.MiscServices, String.Format("Unhandled Gateway protocol {0}", p.Type));
                    break;
            }
        }

        private void SendGatewayInformationResponse(PUP p)
        {
            //
            // Pup Type: 201 (octal)
            // Pup ID: same as in Request Pup
            // Pup Contents: one or more groups of four bytes, each providing routing information for
            // one network, as follows:
            //
            //    <target-net> <gateway-net> <gateway-host> <hop-count>
            //
            // In each group, the first byte specifies the target network number. If the gateway host is
            // directly connected to that network, then the <hop-count> is zero and the <gateway-net> and
            // <gateway-host> describe the gateway’s connection to the network.
            // If the gateway host is not directly connected to the target network, then the second and
            // third bytes give the network and host numbers of another gateway through which the
            // responding gateway routes Pups to that network, and the fourth byte gives the hop count,
            // i.e., the number of additional gateways (not including itself) through which the responding
            // gateway believes a Pup must pass to reach the specified network. A hop count greater than
            // the constant maxHops (presently 15) signifies that the target network is believed to be
            // inaccessible.
            //

            // Right now, we know of only one network (our own) and we are directly connected to it.
            //
            GatewayInformation info = new GatewayInformation();
            info.TargetNet = DirectoryServices.Instance.LocalNetwork;
            info.GatewayNet = DirectoryServices.Instance.LocalNetwork;
            info.GatewayHost = DirectoryServices.Instance.LocalNetwork;
            info.HopCount = 0;

            PUPPort localPort = new PUPPort(DirectoryServices.Instance.LocalHostAddress, p.DestinationPort.Socket);

            // Response must contain our network number; this is used to tell clients what network they're on if they don't already know.
            PUPPort remotePort = new PUPPort(DirectoryServices.Instance.LocalNetwork, p.SourcePort.Host, p.SourcePort.Socket);

            PUP response = new PUP(PupType.GatewayInformationResponse, p.ID, remotePort, localPort, Serializer.Serialize(info));

            Router.Instance.SendPup(response);
        }

    }
}
