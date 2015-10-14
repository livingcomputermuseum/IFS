using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using IFS.Transport;
using PcapDotNet.Packets;

namespace IFS
{

   

    /// <summary>
    /// Dispatches incoming PUPs to the right protocol handler; sends outgoing PUPs over the network.
    /// </summary>
    public class Dispatcher
    {
        /// <summary>
        /// Private Constructor for this class, enforcing Singleton usage.
        /// </summary>
        private Dispatcher()
        {
            _dispatchMap = new Dictionary<uint, PUPProtocolEntry>();

            _packetInterface = new Ethernet(iface, OnPacketReceived);
        }

        /// <summary>
        /// Accessor for singleton instance of this class.
        /// </summary>
        public static Dispatcher Instance
        {
            get { return _instance; }
        }

        /// <summary>
        /// Registers a new protocol with the dispatcher
        /// </summary>
        /// <param name="reg"></param>
        /// <param name="impl"></param>
        public void RegisterProtocol(PUPProtocolEntry entry)
        {
            if (_dispatchMap.ContainsKey(entry.Socket))
            {
                throw new InvalidOperationException(
                    String.Format("Socket {0} has already been registered for protocol {1}", impl.Socket, _dispatchMap[impl.Socket].FriendlyName));
            }

            _dispatchMap[entry.Socket] = entry;
        }

        public void SendPup(PUP p)
        {
            // TODO: Write PUP to ethernet
            
        }

        private void OnPacketReceived(Packet p)
        {            
            // filter out PUPs, discard all other packets.  Forward PUP on to registered endpoints.
            //
            if ((int)p.Ethernet.EtherType == 512)    // 512 = pup type
            {
                PUP pup = new PUP(p.Ethernet.Payload.ToMemoryStream());

                if (_dispatchMap.ContainsKey(pup.DestinationPort.Socket))
                {
                    PUPProtocolEntry entry = _dispatchMap[pup.DestinationPort.Socket];
                    entry.ProtocolImplementation.RecvData(pup);
                }
                else
                {
                    // Not a protocol we handle; TODO: log it.
                }
            }
            else
            {
                // Not a PUP, Discard the packet.
            }
        }

        /// <summary>
        /// Our interface to some kind of network
        /// </summary>
        private IPacketInterface _packetInterface;

        /// <summary>
        /// Map from socket to protocol implementation
        /// </summary>
        private Dictionary<UInt32, PUPProtocolEntry> _dispatchMap;

        private static Dispatcher _instance = new Dispatcher();
    }
}
