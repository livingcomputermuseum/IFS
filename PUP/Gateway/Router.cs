using IFS.Logging;
using IFS.Transport;
using PcapDotNet.Core;
using System.Net.NetworkInformation;

namespace IFS.Gateway
{
    public delegate void RoutePupCallback(PUP pup);

    /// <summary>
    /// Implements gateway services, routing PUPs intended for other networks to
    /// their proper destination.
    /// This is one layer above the physical transport layer (ethernet, udp) and
    /// is below the protocol layer.
    /// 
    /// The routing is currently a stub implmentation, and only handles PUPs destined for our own network.
    /// </summary>
    public class Router
    {
        private Router()
        {
            _localProtocolDispatcher = new PUPProtocolDispatcher();
        }

        public static Router Instance
        {
            get
            {
                return _router;
            }
        }

        public void Shutdown()
        {
            _localProtocolDispatcher.Shutdown();
            _pupPacketInterface.Shutdown();
        }

        public void RegisterRAWInterface(LivePacketDevice iface)
        {
            Ethernet enet = new Ethernet(iface);

            _pupPacketInterface = enet;
            _rawPacketInterface = enet;
            _pupPacketInterface.RegisterRouterCallback(RouteIncomingPacket);
        }

        public void RegisterUDPInterface(NetworkInterface iface)
        {
            UDPEncapsulation udp = new UDPEncapsulation(iface);

            _pupPacketInterface = udp;
            _rawPacketInterface = udp;
            _pupPacketInterface.RegisterRouterCallback(RouteIncomingPacket);
        }

        /// <summary>
        /// Sends a PUP out to the world; this may be routed to a different network.
        /// </summary>
        /// <param name="p"></param>
        public void SendPup(PUP p)
        {
            RouteOutgoingPacket(p);                   
        }

        /// <summary>
        /// Sends a raw packet out to the world.  This packet will not be routed.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="source"></param>
        /// <param name="destination"></param>
        /// <param name="frameType"></param>
        public void Send(byte[] data, byte source, byte destination, ushort frameType)
        {
            if (_rawPacketInterface != null)
            {
                _rawPacketInterface.Send(data, source, destination, frameType);
            }
        }

        /// <summary>
        /// Routes a PUP out to the world.
        /// </summary>
        /// <param name="p"></param>
        private void RouteOutgoingPacket(PUP p)
        {
            // For now, we send the packet out without performing any routing.
            _pupPacketInterface.Send(p);
        }

        /// <summary>
        /// Routes a newly received packet to the proper destination host.
        /// </summary>
        /// <param name="pup"></param>
        public void RouteIncomingPacket(PUP pup)
        {
            //
            // Check the network -- if this is network zero (coming from a host that doesn't yet know what
            // network it's on, or specifying the current network) or our network, we will pass it on to the protocol suite.
            //
            if (pup.DestinationPort.Network == 0 || pup.DestinationPort.Network == DirectoryServices.Instance.LocalHostAddress.Network)
            {
                _localProtocolDispatcher.ReceivePUP(pup);
            }
            else
            {
                // Not for our network.
                // For now, we will drop the packet.  Once we implement
                // Gateway services we will handle these appropriately.)
                Log.Write(LogType.Verbose, LogComponent.Ethernet, "PUP is for network {0}, dropping.", pup.DestinationPort.Network);
            }
        }

        /// <summary>
        /// Our interface to a facility that can transmit/receive PUPs
        /// </summary>
        private IPupPacketInterface _pupPacketInterface;

        /// <summary>
        /// Our interface to a facility that can transmit raw Ethernet frames
        /// </summary>
        private IRawPacketInterface _rawPacketInterface;

        private static Router _router = new Router();

        private PUPProtocolDispatcher _localProtocolDispatcher;
    }
}
