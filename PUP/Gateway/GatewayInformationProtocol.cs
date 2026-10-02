/*  
    This file is part of IFS.

    IFS is free software: you can redistribute it and/or modify
    it under the terms of the GNU Affero General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    IFS is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Affero General Public License for more details.

    You should have received a copy of the GNU Affero General Public License
    along with IFS.  If not, see <http://www.gnu.org/licenses/>.
*/

using IFS.Logging;
using System;
using System.Threading;

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
            _gatewayInfoThread = new Thread(GatewayInformationWorker);
            _gatewayInfoThread.Start();
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

                case PupType.GatewayInformationResponse:
                    // Currently a no-op.
                    Log.Write(LogComponent.MiscServices, String.Format("Gateway Information handler unimplemented."));
                    break;

                default:
                    Log.Write(LogComponent.MiscServices, String.Format("Unhandled Gateway protocol {0} ({1})", p.Type, (int)p.Type));
                    break;
            }
        }

        public override void Terminate()
        {
            if (_gatewayInfoThread.IsAlive)
            {
                _gatewayInfoThread.Abort();
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

            
            byte[] infoArray = GetGatewayInformationArray();

            PUPPort localPort = new PUPPort(DirectoryServices.Instance.LocalHostAddress, p.DestinationPort.Socket);

            // Response must contain our network number; this is used to tell clients what network they're on if they don't already know.
            PUPPort remotePort = new PUPPort(DirectoryServices.Instance.LocalNetwork, p.SourcePort.Host, p.SourcePort.Socket);

            PUP response = new PUP(PupType.GatewayInformationResponse, p.ID, remotePort, localPort, infoArray);

            Router.Instance.SendPup(response);
        }

        private static byte[] GetGatewayInformationArray()
        {
            //
            // We build the gateway information response from the RoutingTable that the Router maintains.
            // Since we do not at this time implement multi-hop routing, all networks known by the Router
            // are assumed to be directly connected and to have a hop-count of 0. 
            //
            byte[] knownNetworks = Router.Instance.RoutingTable.GetKnownNetworks();

            byte[] infoArray = new byte[knownNetworks.Length * 4];

            for (int i = 0; i < knownNetworks.Length; i++)
            {
                GatewayInformation info = new GatewayInformation();
                info.TargetNet = knownNetworks[i];
                info.GatewayNet = DirectoryServices.Instance.LocalNetwork;
                info.GatewayHost = DirectoryServices.Instance.LocalHost;
                info.HopCount = 0; // all networks are directly connected

                byte[] entry = Serializer.Serialize(info);

                entry.CopyTo(infoArray, i * 4);
            }

            return infoArray;
        }

        private void GatewayInformationWorker()
        {
            uint infoPupID = (uint)(new Random().Next());
            while (true)
            {
                //
                // From gatewayinformation.press:
                // "Each gateway host must also periodically broadcast Gateway Information Pups, as described above, 
                // on all directly-connected networks.  The frequency of this broadcast should be approximately one 
                // every 30 seconds, and immediately whenever the gateway’s own routing table changes (see below). 
                // These Pups should be sent from socket 2 to socket 2."
                //
                // At this time, we don't do anything with gateway information PUPs that we receive -- they could
                // at some point be used as originally intended, to dynamically update routing tables, but it would
                // require some serious security investments to make sure that the tables don't get poisoned.
                // However, even though we don't use them, some Alto software does.  For example, the PUP libraries
                // used by Mazewar expect to get periodic updates or eventually it will assume the route is no longer
                // viable and drop connections.
                //

                // Delay 30 seconds
                Thread.Sleep(30000);

                byte[] infoArray = GetGatewayInformationArray();

                // From us, on socket 2
                PUPPort localPort = new PUPPort(DirectoryServices.Instance.LocalHostAddress, 2);

                //
                // The set of known networks is by default the set of directly-connected networks.
                //
                byte[] knownNetworks = Router.Instance.RoutingTable.GetKnownNetworks();

                foreach (byte network in knownNetworks)
                {
                    // Send a broadcast to the specified network
                    PUPPort remotePort = new PUPPort(network, 0, 2);

                    PUP infoPup = new PUP(PupType.GatewayInformationResponse, infoPupID++, remotePort, localPort, infoArray);
                    Router.Instance.SendPup(infoPup);

                    Log.Write(LogComponent.MiscServices, "Gateway Information packet sent to network {0}", network);
                }
            }
        }

        private Thread _gatewayInfoThread;

    }
}
