using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using PcapDotNet.Base;
using PcapDotNet.Core;
using PcapDotNet.Core.Extensions;
using PcapDotNet.Packets;
using PcapDotNet.Packets.Ethernet;
using IFS.Logging;

namespace IFS.Transport
{
    public struct EthernetInterface
    {
        public EthernetInterface(string name, string description, MacAddress macAddress)
        {
            Name = name;
            Description = description;
            MacAddress = macAddress;
        }

        public static List<EthernetInterface> EnumerateDevices()
        {
            List<EthernetInterface> interfaces = new List<EthernetInterface>();

            foreach (LivePacketDevice device in LivePacketDevice.AllLocalMachine)
            {
                interfaces.Add(new EthernetInterface(device.Name, device.Description, device.GetMacAddress()));
            }

            return interfaces;
        }

        public string       Name;
        public string       Description;
        public MacAddress   MacAddress;
    }

    /// <summary>
    /// Defines interface "to the metal" (raw ethernet frames) which may wrap the underlying transport (for example, winpcap)
    /// </summary>
    public class Ethernet : IPupPacketInterface
    {
        public Ethernet(EthernetInterface iface)
        {
            AttachInterface(iface);

            // Set up maps
            _pupToEthernetMap = new Dictionary<byte, MacAddress>(256);
            _ethernetToPupMap = new Dictionary<MacAddress, byte>(256);
        }

        public void RegisterReceiveCallback(HandlePup callback)
        {
            _callback = callback;

            // Now that we have a callback we can start receiving stuff.
            Open(false /* not promiscuous */, int.MaxValue);
            BeginReceive();
        }
        

        public void Send(PUP p)
        {
            //
            // Write PUP to ethernet:
            // Get destination network & host address from PUP and route to correct ethernet address.
            // For now, no actual routing (Gateway not implemented yet), everything is on the same 'net.
            // Just look up host address and find the MAC of the host to send it to.            
            //           
            if (_pupToEthernetMap.ContainsKey(p.DestinationPort.Host))
            {
                MacAddress destinationMac = _pupToEthernetMap[p.SourcePort.Host];

                // Build the outgoing packet; place the source/dest addresses, type field and the PUP data.                
                EthernetLayer ethernetLayer = new EthernetLayer
                {
                    Source = _interface.GetMacAddress(),
                    Destination = destinationMac,
                    EtherType = (EthernetType)_pupFrameType,
                };

                PayloadLayer payloadLayer = new PayloadLayer
                {
                    Data = new Datagram(p.RawData),
                };

                PacketBuilder builder = new PacketBuilder(ethernetLayer, payloadLayer);

                // Send it over the 'net!
                _communicator.SendPacket(builder.Build(DateTime.Now));
            }
            else
            {
                // Log error, this should not happen.
                Log.Write(LogLevel.Error, String.Format("PUP destination address {0} is unknown.", p.DestinationPort.Host));
            }
        }

        private void ReceiveCallback(Packet p)
        {
            //
            // Filter out PUPs, forward them on.
            //
            if ((int)p.Ethernet.EtherType == _pupFrameType)
            {
                PUP pup = new PUP(p.Ethernet.Payload.ToMemoryStream());

                //
                // Check the network -- if this is not network zero (coming from a host that doesn't yet know what
                // network it's on) or the network we're on, we will ignore it (for now).  Once we implement
                // Gateway services we will handle these appropriately (at a higher, as-yet-unimplemented level between this
                // and the Dispatcher).
                //
                if (pup.SourcePort.Network == 0 || pup.SourcePort.Network == DirectoryServices.Instance.LocalHostAddress.Network)
                {
                    UpdateMACTable(pup, p);
                    _callback(pup);
                }
                else
                {
                    // Not for our network.
                    Log.Write(LogLevel.DroppedPacket, String.Format("PUP is for network {0}, dropping.", pup.SourcePort.Network));
                }
            }
            else
            {
                // Not a PUP, Discard the packet.  We will not log this, so as to keep noise down. 
                //Log.Write(LogLevel.DroppedPacket, "Not a PUP.  Dropping.");
            }
        }

        private void AttachInterface(EthernetInterface iface)
        {
            _interface = null;

            // Find the specified device by name
            foreach (LivePacketDevice device in LivePacketDevice.AllLocalMachine)
            {
                if (device.Name == iface.Name && device.GetMacAddress() == iface.MacAddress)
                {
                    _interface = device;
                    break;
                }
            }

            if (_interface == null)
            {
                throw new InvalidOperationException("Requested interface not found.");
            }
        }

        private void Open(bool promiscuous, int timeout)
        {
            _communicator = _interface.Open(0xffff, promiscuous ? PacketDeviceOpenAttributes.Promiscuous : PacketDeviceOpenAttributes.None, timeout);
        }

        /// <summary>
        /// Begin receiving packets, forever.
        /// </summary>
        private void BeginReceive()
        {
            _communicator.ReceivePackets(-1, ReceiveCallback);
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
                // PUP host id on the network.
                //
                if (_pupToEthernetMap[p.SourcePort.Host] != e.Ethernet.Source)
                {
                    Log.Write(LogLevel.DuplicateHostNumber,
                        String.Format("Duplicate host ID {0} for MAC {1} (currently mapped to MAC {2})",
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
        /// PUP<->Ethernet address map
        /// </summary>
        private Dictionary<byte, MacAddress> _pupToEthernetMap;
        private Dictionary<MacAddress, byte> _ethernetToPupMap;

        private LivePacketDevice _interface;
        private PacketCommunicator _communicator;
        private HandlePup _callback;

        // Constants
        private const ushort _pupFrameType = 512;

    }
}
