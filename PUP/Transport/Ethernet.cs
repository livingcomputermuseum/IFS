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
using System.Net.NetworkInformation;
using System.Threading;
using IFS.Gateway;

namespace IFS.Transport
{
    /// <summary>
    /// Defines interface "to the metal" (raw ethernet frames) using WinPCAP to send and receive Ethernet
    /// frames.
    /// 
    /// Ethernet packets are broadcast.  See comments in UDP.cs for the reasoning behind this.
    /// 
    /// </summary>
    public class Ethernet : IPupPacketInterface, IRawPacketInterface
    {
        public Ethernet(LivePacketDevice iface)
        {
            _interface = iface;            
        }

        public void RegisterRouterCallback(RoutePupCallback callback)
        {
            _routerCallback = callback;

            // Now that we have a callback we can start receiving stuff.
            Open(false /* not promiscuous */, 0);

            // Kick off the receiver thread, this will never return or exit.
            Thread receiveThread = new Thread(new ThreadStart(BeginReceive));
            receiveThread.Start();
        }

        public void Shutdown()
        {
            _routerCallback = null;
            _communicator.Break();                        
        }
        
        public void Send(PUP p)
        {
            //
            // Write PUP to ethernet:
            //
            
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

            MacAddress destinationMac = new MacAddress(_10mbitBroadcast);

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
            
            MacAddress destinationMac = new MacAddress(_10mbitBroadcast);            

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
                Log.Write(LogType.Verbose, LogComponent.Ethernet, "3mbit pup received.");

                MemoryStream packetStream = p.Ethernet.Payload.ToMemoryStream();

                // Read the length prefix (in words), convert to bytes.
                // Subtract off 2 words for the ethernet header
                int length = ((packetStream.ReadByte() << 8) | (packetStream.ReadByte())) * 2  - 4;

                // Read the address (1st word of 3mbit packet)
                byte destination = (byte)packetStream.ReadByte();
                byte source = (byte)packetStream.ReadByte();

                // Read the type and switch on it
                int etherType3mbit = ((packetStream.ReadByte() << 8) | (packetStream.ReadByte()));

                //
                // Ensure this is a packet we're interested in.
                //
                if (etherType3mbit == _pupFrameType &&                          // it's a PUP
                    (destination == DirectoryServices.Instance.LocalHost ||     // for us, or...
                     destination == 0))                                         // broadcast
                {
                    try
                    {
                        PUP pup = new PUP(packetStream, length);
                        _routerCallback(pup, destination != 0);
                    }
                    catch(Exception e)
                    {
                        // An error occurred, log it.
                        Log.Write(LogType.Error, LogComponent.PUP, "Error handling PUP: {0}", e.Message);
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
                // Log.Write(LogType.Verbose, LogComponent.Ethernet, "Not a PUP (type 0x{0:x}.  Dropping.", p.Ethernet.EtherType);
            }
        }       

        private void Open(bool promiscuous, int timeout)
        {
            _communicator = _interface.Open(
                0xffff, 
                promiscuous ? PacketDeviceOpenAttributes.Promiscuous : PacketDeviceOpenAttributes.None, 
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

        private LivePacketDevice _interface;
        private PacketCommunicator _communicator;
        private RoutePupCallback _routerCallback;

        // Constants

        // The ethertype used in the encapsulated 3mbit frame
        private readonly ushort _pupFrameType = 512;

        // The type used for 3mbit frames encapsulated in 10mb frames
        private readonly int _3mbitFrameType = 0xbeef;     // easy to identify, ostensibly unused by anything of any import        

        // 10mbit broadcast address
        private UInt48 _10mbitBroadcast = (UInt48)0xffffffffffff;

    }
}
