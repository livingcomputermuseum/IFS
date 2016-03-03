using IFS.BSP;
using IFS.EFTP;
using IFS.Logging;
using IFS.Transport;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using PcapDotNet.Base;
using System.Net.NetworkInformation;

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

        public void RegisterInterface(string description)
        {
            // TODO: support multiple interfaces (for gateway routing, for example.)
            // TODO: support configuration options for backend.
            //Ethernet enet = new Ethernet(i.Description);

            UDPEncapsulation udp = new UDPEncapsulation(description);
            _pupPacketInterface = udp as IPupPacketInterface;
            _rawPacketInterface = udp as IRawPacketInterface;            

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
            // drop every 10th packet for testing
            _packet++;

           // if ((_packet % 10) != 5)
            {
                _pupPacketInterface.Send(p);
            }
        
        }

        public void Send(byte[] data, byte source, byte destination, ushort frameType)
        {
            if (_rawPacketInterface != null)
            {
                _rawPacketInterface.Send(data, source, destination, frameType);
            }
        }        

        private void OnPupReceived(PUP pup)
        {
            //
            // Filter out packets not destined for us.
            // Even though we use pcap in non-promiscuous mode, if
            // something else has set the interface to promiscuous mode, that
            // setting may be overridden.
            //
            if (pup.DestinationPort.Host != 0 &&                                           // Not broadcast.
                pup.DestinationPort.Host != DirectoryServices.Instance.LocalHost)          // Not our address.
            {
                // Do nothing with this PUP.
                return;
            }

            //      
            // Forward PUP on to registered endpoints.
            //                                    
            if (_dispatchMap.ContainsKey(pup.DestinationPort.Socket))
            {
                PUPProtocolEntry entry = _dispatchMap[pup.DestinationPort.Socket];

                if (entry.ConnectionType == ConnectionType.Connectionless)
                {
                    Log.Write(LogType.Verbose, LogComponent.PUP, "Dispatching PUP (source {0}, dest {1}) to {2} handler.", pup.SourcePort, pup.DestinationPort, entry.FriendlyName);
                    // Connectionless; just pass the PUP directly to the protocol
                    entry.ProtocolImplementation.RecvData(pup);
                }
                else
                {
                    // RTP / BSP protocol.  Pass this to the BSP handler to set up a channel.
                    Log.Write(LogType.Verbose, LogComponent.PUP, "Dispatching PUP (source {0}, dest {1}) to BSP protocol for {0}.", pup.SourcePort, pup.DestinationPort, entry.FriendlyName);
                    //entry.ProtocolImplementation.RecvData(pup);

                    BSPManager.EstablishRendezvous(pup, (BSPProtocol)entry.ProtocolImplementation);
                }
            }
            else if (BSPManager.ChannelExistsForSocket(pup))
            {
                // An established BSP channel, send data to it.
                BSPManager.RecvData(pup);
            }
            else if (EFTPManager.ChannelExistsForSocket(pup))
            {
                EFTPManager.RecvData(pup);
            }
            else
            {
                // Not a protocol we handle; log it.
                Log.Write(LogType.Normal, LogComponent.PUP, "Unhandled PUP protocol, source socket {0}, destination socket {1}, type {2}, dropped packet.", pup.SourcePort.Socket, pup.DestinationPort.Socket, pup.Type);
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

        /// <summary>
        /// Map from socket to protocol implementation
        /// </summary>
        private Dictionary<UInt32, PUPProtocolEntry> _dispatchMap;

        private int _packet;

        private static PUPProtocolDispatcher _instance = new PUPProtocolDispatcher();
    }
}
