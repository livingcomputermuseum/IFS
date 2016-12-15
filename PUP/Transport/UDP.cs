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
using System.Net;
using System.Net.Sockets;

using System.Threading;
using System.Net.NetworkInformation;
using IFS.Logging;
using System.IO;
using IFS.Gateway;

namespace IFS.Transport
{
    /// <summary>
    /// Implements the logic for encapsulating a 3mbit ethernet packet into/out of UDP datagrams.
    /// Sent packets are broadcast to the subnet. 
    /// 
    /// A brief diversion into the subject of broadcasts and the reason for using them.  (This applies
    /// to the Ethernet transport as well.)
    /// 
    /// Effectively, the IFS suite is implemented on top of a virtual 3 Megabit Ethernet network
    /// encapsulated over a modern network (UDP over IP, raw Ethernet frames, etc.).  Participants
    /// on this virtual network are virtual Altos (ContrAlto or others) and real Altos bridged via
    /// a 3M<->100M device.
    /// 
    /// Any of these virtual or real Altos can, at any time, be running in Promiscuous mode, can send
    /// arbitrary packets with any source or destination address in the header, or send broadcasts.  
    /// This makes address translation from the virtual (3M) side to the physical (UDP, 100M) side and 
    /// back again tricky.  It also makes it tricky to ensure an outgoing packet makes it to any and 
    /// all parties that may be interested (consider the Promiscuous Alto case.)
    /// 
    /// If each participant on the virtual network were to have a table mapping physical (UDP IP, 100M MAC) to
    /// virtual (3M MAC) addresses then broadcasts could be avoided, but it complicates the logic in all
    /// parties and requires each user to maintain this mapping table manually.
    /// 
    /// Resorting to using broadcasts at all times on the physical network removes these complications and
    /// makes it easy for end-users to deal with.
    /// The drawback is that broadcasts can reduce the efficiency of the network segment they're broadcast to.
    /// However, most Alto networks are extremely quiet (by today's standards) -- the maximum throughput
    /// of one Alto continuously transferring data to another is on the order of 20-30 kilobytes/sec.  
    /// (Most of the time, a given Alto will be completely silent.)
    /// On a modern 100M or 1G network, this is background noise and modern computers receiving these broadcasts
    /// will hardly notice.
    /// 
    /// Based on the above, and after a lot of experimentation, it was decided to err on the side of simplicity 
    /// and go with the broadcast implementation.
    /// 
    /// </summary>
    public class UDPEncapsulation : IPupPacketInterface, IRawPacketInterface
    {
        public UDPEncapsulation(NetworkInterface iface)
        {
            // Try to set up UDP client.
            try
            {
                _udpClient = new UdpClient(Configuration.UDPPort, AddressFamily.InterNetwork);                                                           
                _udpClient.Client.Blocking = true;                
                _udpClient.EnableBroadcast = true;
                _udpClient.MulticastLoopback = false;

                //
                // Grab the broadcast address for the interface so that we know what broadcast address to use
                // for our UDP datagrams.
                //                
                IPInterfaceProperties props = iface.GetIPProperties();                

                foreach (UnicastIPAddressInformation unicast in props.UnicastAddresses)
                {
                    // Find the first InterNetwork address for this interface and 
                    // go with it.
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        _thisIPAddress = unicast.Address;
                        _broadcastEndpoint = new IPEndPoint(GetBroadcastAddress(_thisIPAddress, unicast.IPv4Mask), Configuration.UDPPort);
                        break;
                    }
                }

                if (_broadcastEndpoint == null)
                {
                    throw new InvalidOperationException(String.Format("No IPV4 network information was found for interface '{0}'.", iface.Name));
                }

            }
            catch (Exception e)
            {
                Log.Write(LogType.Error, LogComponent.UDP,
                    "Error configuring UDP socket {0} for use with IFS on interface {1}.  Ensure that the selected network interface is valid, configured properly, and that nothing else is using this port.",
                    Configuration.UDPPort,
                    iface.Name);

                Log.Write(LogType.Error, LogComponent.UDP,
                    "Error was '{0}'.",
                    e.Message);

                _udpClient = null;
            }
        }

        /// <summary>
        /// Registers a gateway to handle incoming PUPs.
        /// </summary>
        /// <param name="callback"></param>
        public void RegisterRouterCallback(RoutePupCallback callback)
        {
            _routerCallback = callback;

            // Now that we have a callback we can start receiving stuff.            
            BeginReceive();
        }

        public void Shutdown()
        {            
            _receiveThread.Abort();
            _routerCallback = null;
        }

        public void Send(PUP p)
        {
            //
            // Write PUP to UDP:
            //
            // For now, no actual routing (Gateway not implemented yet), everything is on the same 'net.
            // Just send a broadcast UDP with the encapsulated frame inside of it.
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

            // Send as UDP broadcast.            
            _udpClient.Send(encapsulatedFrame, encapsulatedFrame.Length, _broadcastEndpoint);               
        }

        /// <summary>
        /// Sends an array of bytes over the ethernet as a 3mbit packet encapsulated in a 10mbit packet.
        /// </summary>
        /// <param name="packet"></param>
        /// <param name="hostId"></param>
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

            // Send as UDP broadcast.            
            _udpClient.Send(encapsulatedFrame, encapsulatedFrame.Length, _broadcastEndpoint);                        
        }

        private void Receive(MemoryStream packetStream)
        {
            //
            // Look for PUPs, forward them on.
            //
                           
            // Read the length prefix (in words), convert to bytes.
            // Subtract off 2 words for the ethernet header
            int length = ((packetStream.ReadByte() << 8) | (packetStream.ReadByte())) * 2 - 4;

            // Read the address (1st word of 3mbit packet)
            byte destination = (byte)packetStream.ReadByte();
            byte source = (byte)packetStream.ReadByte();

            // Read the type and switch on it
            int etherType3mbit = ((packetStream.ReadByte() << 8) | (packetStream.ReadByte()));

            if (etherType3mbit == _pupFrameType)
            {
                try
                {
                    PUP pup = new PUP(packetStream, length);
                    _routerCallback(pup);
                }
                catch(Exception e)
                {
                    // An error occurred, log it.
                    Log.Write(LogType.Error, LogComponent.PUP, "Error handling PUP: {0}", e.Message);
                }
            }
            else
            {
                Log.Write(LogType.Warning, LogComponent.Ethernet, "UDP packet is not a PUP, dropping");
            }        
        }

        /// <summary>
        /// Begin receiving packets, forever.
        /// </summary>
        private void BeginReceive()
        {
            // Kick off receive thread.   
            _receiveThread = new Thread(ReceiveThread);
            _receiveThread.Start();
        }

        /// <summary>
        /// Worker thread for UDP packet receipt.
        /// </summary>
        private void ReceiveThread()
        {
            // Just call ReceivePackets, that's it.  This will never return.
            // (probably need to make this more elegant so we can tear down the thread
            // properly.)
            Log.Write(LogComponent.UDP, "UDP Receiver thread started.");

            IPEndPoint groupEndPoint = new IPEndPoint(IPAddress.Any, Configuration.UDPPort);            

            while (true)
            {
                byte[] data = _udpClient.Receive(ref groupEndPoint);

                // Drop our own UDP packets.
                if (!groupEndPoint.Address.Equals(_thisIPAddress))
                {
                    Receive(new System.IO.MemoryStream(data));
                }
            }
        }


        private IPAddress GetBroadcastAddress(IPAddress address, IPAddress subnetMask)
        {
            byte[] ipAdressBytes = address.GetAddressBytes();
            byte[] subnetMaskBytes = subnetMask.GetAddressBytes();

            byte[] broadcastAddress = new byte[ipAdressBytes.Length];
            for (int i = 0; i < broadcastAddress.Length; i++)
            {
                broadcastAddress[i] = (byte)(ipAdressBytes[i] | (subnetMaskBytes[i] ^ 255));
            }

            return new IPAddress(broadcastAddress);
        }               
       
        // The ethertype used in the encapsulated 3mbit frame
        private readonly ushort _pupFrameType = 512;

        private RoutePupCallback _routerCallback;

        // Thread used for receive
        private Thread _receiveThread;
        
        private UdpClient _udpClient;
        private IPEndPoint _broadcastEndpoint;

        // The IP address (unicast address) of the interface we're using to send UDP datagrams.
        private IPAddress _thisIPAddress;

    }
}
