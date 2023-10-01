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
using PcapDotNet.Base;
using PcapDotNet.Core;
using PcapDotNet.Core.Extensions;
using PcapDotNet.Packets;
using PcapDotNet.Packets.Ethernet;
using IFS.Logging;
using System.IO;
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
    public class Ethernet : IPacketInterface
    {
        public Ethernet(LivePacketDevice iface)
        {
            _interface = iface;
        }

        public void RegisterRouterCallback(ReceivedPacketCallback callback)
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
            byte[] encapsulatedFrame = PupPacketBuilder.BuildEncapsulatedEthernetFrameFromPup(p);
            SendFrame(encapsulatedFrame);
        }

        public void Send(byte[] data, byte source, byte destination, ushort frameType)
        {
            byte[] encapsulatedFrame = PupPacketBuilder.BuildEncapsulatedEthernetFrameFromRawData(data, source, destination, frameType);
            SendFrame(encapsulatedFrame);
        }

        public void Send(MemoryStream encapsulatedFrameStream)
        {
            SendFrame(encapsulatedFrameStream.ToArray());
        }

        private void SendFrame(byte[] encapsulatedFrame)
        {
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
            if ((int)p.Ethernet.EtherType == _3mbitFrameType)
            {
                Log.Write(LogType.Verbose, LogComponent.Ethernet, "3mbit pup received.");

                MemoryStream packetStream = p.Ethernet.Payload.ToMemoryStream();
                _routerCallback(packetStream, this);
            }
            else
            {
                // Not an encapsulated 3mbit frame, Discard the packet.  We will not log this, so as to keep noise down. 
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
        private ReceivedPacketCallback _routerCallback;

        // Constants

        // The type used for 3mbit frames encapsulated in 10mb frames
        private readonly int _3mbitFrameType = 0xbeef;     // easy to identify, ostensibly unused by anything of any import        

        // 10mbit broadcast address
        private UInt48 _10mbitBroadcast = (UInt48)0xffffffffffff;

    }
}
