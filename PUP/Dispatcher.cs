using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using IFS.Transport;
using PcapDotNet.Packets;
using PcapDotNet.Packets.Ethernet;
using IFS.Logging;
using PcapDotNet.Base;

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
        }

        /// <summary>
        /// Accessor for singleton instance of this class.
        /// </summary>
        public static Dispatcher Instance
        {
            get { return _instance; }
        }

        public void RegisterInterface(EthernetInterface i)
        {
            // TODO: support multiple interfaces (for gateway routing, for example.)
            // Also, this should not be ethernet-specific.
            _packetInterface = new Ethernet(i, OnPacketReceived);
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
                    String.Format("Socket {0} has already been registered for protocol {1}", entry.Socket, _dispatchMap[entry.Socket].FriendlyName));
            }

            _dispatchMap[entry.Socket] = entry;
        }

        public void SendPup(PUP p)
        {
            //
            // Write PUP to ethernet:
            // Get destination network & host address from PUP and route to correct ethernet address.
            // For now, no actual routing (Gateway not implemented yet), everthing is on the same 'net.
            // Just look up host address and find the MAC of the host to send it to.
            // TODO: this translation should be at the Interface level, not here (we shouldn't be doing
            // ethernet-specific things here -- at the moment it doesn't matter since we're only speaking
            // ethernet, but this should be fixed.)
            //           
            if (_pupToEthernetMap.ContainsKey(p.DestinationPort.Host))
            {
                MacAddress destinationMac = _pupToEthernetMap[p.SourcePort.Host];

                // Build the outgoing packet; place the source/dest addresses, type field and the PUP data.                
                EthernetLayer ethernetLayer = new EthernetLayer
                {
                    Source = (MacAddress)_packetInterface.GetDeviceAddress(),
                    Destination = destinationMac,
                    EtherType = (EthernetType)512,  // PUP type (TODO: move to constant)
                };

                PayloadLayer payloadLayer = new PayloadLayer
                {
                    Data = new Datagram(p.RawData),
                };

                PacketBuilder builder = new PacketBuilder(ethernetLayer, payloadLayer);
                
                _packetInterface.SendPacket(builder.Build(DateTime.Now));
            }
            else
            {
                // Log error, this should not happen.
                Log.Write(LogLevel.Error, String.Format("PUP destination address {0} is unknown.", p.DestinationPort.Host));
            }
            
        }

        private void OnPacketReceived(Packet p)
        {            
            // filter out PUPs, discard all other packets.  Forward PUP on to registered endpoints.
            //
            if ((int)p.Ethernet.EtherType == 512)    // 512 = pup type
            {
                PUP pup = new PUP(p.Ethernet.Payload.ToMemoryStream());

                //
                // Check the network -- if this is not network zero (coming from a host that doesn't yet know what
                // network it's on) or the network we're on, we will ignore it (for now...).  Once we implement
                // Gateway services we will handle these appropriately.
                //
                if (pup.SourcePort.Network == 0 || pup.SourcePort.Network == DirectoryServices.Instance.LocalHostAddress.Network)
                {
                    UpdateMACTable(pup, p);

                    if (_dispatchMap.ContainsKey(pup.DestinationPort.Socket))
                    {
                        PUPProtocolEntry entry = _dispatchMap[pup.DestinationPort.Socket];
                        entry.ProtocolImplementation.RecvData(pup);
                    }
                    else
                    {
                        // Not a protocol we handle; TODO: log it.
                        Log.Write(LogLevel.UnhandledProtocol | LogLevel.DroppedPacket, String.Format("Unhandled PUP protocol, socket {0}, dropped packet.", pup.DestinationPort.Socket));
                    }
                }
                else
                {
                    // Not for our network; eventually we will look into routing...
                    Log.Write(LogLevel.DroppedPacket, String.Format("PUP is for network {0}, dropping.", pup.SourcePort.Network));
                }
            }
            else
            {
                // Not a PUP, Discard the packet.  We will not log this to keep noise down. 
                //Log.Write(LogLevel.DroppedPacket, "Not a PUP.  Dropping.");
            }
        }

        private void UpdateMACTable(PUP p, Packet e)
        {
            //
            // See if we already have this entry.
            //
            if (_pupToEthernetMap.ContainsKey(p.SourcePort.Host))
            {
                // 
                // We do; ensure that the mac addresses match -- if not we have a duplicate
                // PUP id on the network.
                //
                if (_pupToEthernetMap[p.SourcePort.Host] != e.Ethernet.Source)
                {
                    Log.Write(LogLevel.DuplicateHostNumber,
                        String.Format("Duplicate host number {0} for MAC {1} (currently mapped to MAC {2})",
                            p.SourcePort.Host,
                            e.Ethernet.Source,
                            _pupToEthernetMap[p.SourcePort.Host]));
                }
            }
            else
            {
                // Add a mapping in both directions
                _pupToEthernetMap.Add(p.SourcePort.Host, e.Ethernet.Source);
                _ethernetToPupMap.Add(e.Ethernet.Source, p.SourcePort.Host);
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

        /// <summary>
        /// PUP<->Ethernet address map
        /// </summary>
        private Dictionary<byte, MacAddress> _pupToEthernetMap;
        private Dictionary<MacAddress, byte> _ethernetToPupMap;

        private static Dispatcher _instance = new Dispatcher();
    }
}
