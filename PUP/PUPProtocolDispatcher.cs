using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using IFS.Transport;
using IFS.Logging;
using PcapDotNet.Base;

namespace IFS
{   
    /// <summary>
    /// Dispatches incoming PUPs to the right protocol handler; sends outgoing PUPs over the network.
    /// </summary>
    public class PUPProtocolDispatcher
    {
        /// <summary>
        /// Private Constructor for this class, enforcing Singleton usage.
        /// </summary>
        private PUPProtocolDispatcher()
        {
            _dispatchMap = new Dictionary<uint, PUPProtocolEntry>();           
        }

        /// <summary>
        /// Accessor for singleton instance of this class.
        /// </summary>
        public static PUPProtocolDispatcher Instance
        {
            get { return _instance; }
        }

        public void RegisterInterface(EthernetInterface i)
        {
            // TODO: support multiple interfaces (for gateway routing, for example.)
            // Also, this should not be ethernet-specific.
            _pupPacketInterface = new Ethernet(i);

            _pupPacketInterface.RegisterReceiveCallback(OnPupReceived);
        }

        /// <summary>
        /// Registers a new protocol with the dispatcher.
        /// </summary>
        /// <param name="reg"></param>
        /// <param name="impl"></param>
        public void RegisterProtocol(PUPProtocolEntry entry)
        {
            if (_dispatchMap.ContainsKey(entry.Socket))
            {
                throw new InvalidOperationException(
                    String.Format("Socket {0} has already been registered for protocol {1}", entry.Socket, _dispatchMap[entry.Socket].FriendlyName));
            }

            _dispatchMap[entry.Socket] = entry;
        }

        public void SendPup(PUP p)
        {
            _pupPacketInterface.Send(p);            
        }

        private void OnPupReceived(PUP pup)
        {
            //      
            // Forward PUP on to registered endpoints.
            //                                    
            if (_dispatchMap.ContainsKey(pup.DestinationPort.Socket))
            {
                PUPProtocolEntry entry = _dispatchMap[pup.DestinationPort.Socket];

                if (entry.ConnectionType == ConnectionType.Connectionless)
                {
                    Log.Write(LogLevel.HandledProtocol, String.Format("Dispatching PUP to {0} handler.", entry.FriendlyName));
                    // Connectionless; just pass the PUP directly to the protocol
                    entry.ProtocolImplementation.RecvData(pup);
                }
                else
                {
                    // RTP / BSP protocol.  Pass this to the BSP handler to set up a channel.
                    Log.Write(LogLevel.HandledProtocol, String.Format("Dispatching PUP to BSP protocol for {0}.", entry.FriendlyName));
                    //entry.ProtocolImplementation.RecvData(pup);

                    BSPManager.EstablishRendezvous(pup, (BSPProtocol)entry.ProtocolImplementation);
                }
            }
            else if (BSPManager.ChannelExistsForSocket(pup))
            {
                // An established BSP channel, send data to it.
                BSPManager.RecvData(pup);
            }
            else
            {
                // Not a protocol we handle; log it.
                Log.Write(LogLevel.UnhandledProtocol | LogLevel.DroppedPacket, String.Format("Unhandled PUP protocol, socket {0}, dropped packet.", pup.DestinationPort.Socket));
            }            
        }
      
        /// <summary>
        /// Our interface to a facility that can transmit/receive PUPs
        /// </summary>
        private IPupPacketInterface _pupPacketInterface;

        /// <summary>
        /// Map from socket to protocol implementation
        /// </summary>
        private Dictionary<UInt32, PUPProtocolEntry> _dispatchMap;

        private static PUPProtocolDispatcher _instance = new PUPProtocolDispatcher();
    }
}
