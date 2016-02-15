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
using System.IO;

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
    public class Ethernet : IPupPacketInterface, IRawPacketInterface
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
                // Build the outgoing data; this is:
                // 1st word: length of data following
                // 2nd word: 3mbit destination / source bytes
                // 3rd word: frame type (PUP)
                byte[] encapsulatedFrame = new byte[6 + p.RawData.Length];

                // 3mbit Packet length
                encapsulatedFrame[0] = (byte)((p.RawData.Length / 2 + 2) >> 8);
                encapsulatedFrame[1] = (byte)(p.RawData.Length / 2 + 2);

                // addressing
                encapsulatedFrame[2] = p.DestinationPort.Host;
                encapsulatedFrame[3] = p.SourcePort.Host;                                

                // frame type
                encapsulatedFrame[4] = (byte)(_pupFrameType >> 8);
                encapsulatedFrame[5] = (byte)_pupFrameType;

                // Actual data
                p.RawData.CopyTo(encapsulatedFrame, 6);

                // Byte swap
                encapsulatedFrame = ByteSwap(encapsulatedFrame);

                MacAddress destinationMac = _pupToEthernetMap[p.DestinationPort.Host];

                // Build the outgoing packet; place the source/dest addresses, type field and the PUP data.                
                EthernetLayer ethernetLayer = new EthernetLayer
                {
                    Source = _interface.GetMacAddress(),
                    Destination = destinationMac,
                    EtherType = (EthernetType)_3mbitFrameType,
                };                

                PayloadLayer payloadLayer = new PayloadLayer
                {
                    Data = new Datagram(encapsulatedFrame),
                };

                PacketBuilder builder = new PacketBuilder(ethernetLayer, payloadLayer);

                // Send it over the 'net!
                _communicator.SendPacket(builder.Build(DateTime.Now));
            }
            else
            {
                // Log error, this should not happen.
                Log.Write(LogType.Error, LogComponent.Ethernet, String.Format("PUP destination address {0} is unknown.", p.DestinationPort.Host));
            }
        }

        public void Send(byte[] data, byte source, byte destination, ushort frameType)
        {
            // Build the outgoing data; this is:
            // 1st word: length of data following
            // 2nd word: 3mbit destination / source bytes
            // 3rd word: frame type (PUP)
            byte[] encapsulatedFrame = new byte[6 + data.Length];

            // 3mbit Packet length
            encapsulatedFrame[0] = (byte)((data.Length / 2 + 2) >> 8);
            encapsulatedFrame[1] = (byte)(data.Length / 2 + 2);

            // addressing
            encapsulatedFrame[2] = destination;
            encapsulatedFrame[3] = source;

            // frame type
            encapsulatedFrame[4] = (byte)(frameType >> 8);
            encapsulatedFrame[5] = (byte)frameType;

            // Actual data
            data.CopyTo(encapsulatedFrame, 6);

            // Byte swap
            encapsulatedFrame = ByteSwap(encapsulatedFrame);

            MacAddress destinationMac;
            if (destination != 0xff)
            {
                if (_pupToEthernetMap.ContainsKey(destination))
                {
                    //
                    // Use the existing map.
                    //
                    destinationMac = _pupToEthernetMap[destination];
                }
                else
                {
                    //
                    // Nothing mapped for this PUP, do our best with it.
                    //
                    destinationMac = new MacAddress((UInt48)(_10mbitMACPrefix | destination));
                }
            }
            else
            {
                // 3mbit broadcast becomes 10mbit broadcast
                destinationMac = new MacAddress(_10mbitBroadcast);
            }

            // Build the outgoing packet; place the source/dest addresses, type field and the PUP data.                
            EthernetLayer ethernetLayer = new EthernetLayer
            {
                Source = _interface.GetMacAddress(),
                Destination = destinationMac,
                EtherType = (EthernetType)_3mbitFrameType,
            };

            PayloadLayer payloadLayer = new PayloadLayer
            {
                Data = new Datagram(encapsulatedFrame),
            };

            PacketBuilder builder = new PacketBuilder(ethernetLayer, payloadLayer);

            // Send it over the 'net!
            _communicator.SendPacket(builder.Build(DateTime.Now));
        }

        private void ReceiveCallback(Packet p)
        {
            //
            // Filter out encapsulated 3mbit frames and look for PUPs, forward them on.
            //
            if ((int)p.Ethernet.EtherType == _3mbitFrameType)
            {
                MemoryStream packetStream = ByteSwap(p.Ethernet.Payload.ToMemoryStream());

                // Read the length prefix (in words), convert to bytes.
                // Subtract off 2 words for the ethernet header
                int length = ((packetStream.ReadByte() << 8) | (packetStream.ReadByte())) * 2  - 4;

                // Read the address (1st word of 3mbit packet)
                byte destination = (byte)packetStream.ReadByte();
                byte source = (byte)packetStream.ReadByte();

                // Read the type and switch on it
                int etherType3mbit = ((packetStream.ReadByte() << 8) | (packetStream.ReadByte()));

                if (etherType3mbit == _pupFrameType)
                {
                    PUP pup = new PUP(packetStream, length);

                    //
                    // Check the network -- if this is not network zero (coming from a host that doesn't yet know what
                    // network it's on, or specifying the current network) or the network we're on, we will ignore it (for now).  Once we implement
                    // Gateway services we will handle these appropriately (at a higher, as-yet-unimplemented layer between this
                    // and the Dispatcher).
                    //
                    if (pup.DestinationPort.Network == 0 || pup.DestinationPort.Network == DirectoryServices.Instance.LocalHostAddress.Network)
                    {
                        UpdateMACTable(pup, p);
                        _callback(pup);
                    }
                    else
                    {
                        // Not for our network.
                        Log.Write(LogType.Verbose, LogComponent.Ethernet, "PUP is for network {0}, dropping.", pup.DestinationPort.Network);
                    }
                }
                else
                {
                    Log.Write(LogType.Warning, LogComponent.Ethernet, "3mbit packet is not a PUP, dropping");
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
            _communicator = _interface.Open(
                0xffff, 
                promiscuous ? PacketDeviceOpenAttributes.Promiscuous | PacketDeviceOpenAttributes.NoCaptureLocal: PacketDeviceOpenAttributes.NoCaptureLocal, 
                timeout);

            _communicator.SetKernelMinimumBytesToCopy(1);
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
                    Log.Write(LogType.Error, LogComponent.Ethernet,
                        "Duplicate host ID {0} for MAC {1} (currently mapped to MAC {2})",
                        p.SourcePort.Host,
                        e.Ethernet.Source,
                        _pupToEthernetMap[p.SourcePort.Host]);
                }
            }
            else
            {
                // Add a mapping in both directions
                _pupToEthernetMap.Add(p.SourcePort.Host, e.Ethernet.Source);
                _ethernetToPupMap.Add(e.Ethernet.Source, p.SourcePort.Host);
            }
        }

        private MemoryStream ByteSwap(MemoryStream input)
        {
            byte[] buffer = new byte[input.Length];

            input.Read(buffer, 0, buffer.Length);

            for(int i=0;i<buffer.Length;i+=2)
            {
                byte temp = buffer[i];
                buffer[i] = buffer[i + 1];
                buffer[i + 1] = temp;
            }

            input.Position = 0;            

            return new MemoryStream(buffer);
        }

        private byte[] ByteSwap(byte[] input)
        {                       
            for (int i = 0; i < input.Length; i += 2)
            {
                byte temp = input[i];
                input[i] = input[i + 1];
                input[i + 1] = temp;
            }

            return input;
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

        // The ethertype used in the encapsulated 3mbit frame
        private readonly ushort _pupFrameType = 512;

        // The type used for 3mbit frames encapsulated in 10mb frames
        private readonly int _3mbitFrameType = 0xbeef;     // easy to identify, ostensibly unused by anything of any import

        // 5 byte prefix for 3mbit->10mbit addresses when sending raw frames; this is the convention ContrAlto uses.
        // TODO: this should be configurable.
        private UInt48 _10mbitMACPrefix = 0x0000aa010200;  // 00-00-AA is the Xerox vendor code, used just to be cute.  

        // 10mbit broadcast address
        private UInt48 _10mbitBroadcast = (UInt48)0xffffffffffff;

    }
}
