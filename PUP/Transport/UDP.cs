using System;
using System.Net;
using System.Net.Sockets;

using System.Threading;
using System.Net.NetworkInformation;
using IFS.Logging;
using System.IO;

namespace IFS.Transport
{
    /// <summary>
    /// Implements the logic for encapsulating a 3mbit ethernet packet into/out of UDP datagrams.
    /// Sent packets are broadcast to the subnet.        
    /// </summary>
    public class UDPEncapsulation : IPupPacketInterface, IRawPacketInterface
    {
        public UDPEncapsulation(NetworkInterface iface)
        {
            // Try to set up UDP client.
            try
            {
                _udpClient = new UdpClient(_udpPort, AddressFamily.InterNetwork);                                                           
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
                        _broadcastEndpoint = new IPEndPoint(GetBroadcastAddress(_thisIPAddress, unicast.IPv4Mask), _udpPort);
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
                    _udpPort,
                    iface.Name);

                Log.Write(LogType.Error, LogComponent.UDP,
                    "Error was '{0}'.",
                    e.Message);

                _udpClient = null;
            }
        }

        public void RegisterReceiveCallback(HandlePup callback)
        {
            _callback = callback;

            // Now that we have a callback we can start receiving stuff.            
            BeginReceive();
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
            // TODO: this could be done without broadcasts if we kept a table mapping IPs to 3mbit MACs.
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
            // TODO: this could be done without broadcasts if we kept a table mapping IPs to 3mbit MACs.
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
                PUP pup = new PUP(packetStream, length);

                //
                // Check the network -- if this is not network zero (coming from a host that doesn't yet know what
                // network it's on, or specifying the current network) or the network we're on, we will ignore it (for now).  Once we implement
                // Gateway services we will handle these appropriately (at a higher, as-yet-unimplemented layer between this
                // and the Dispatcher).
                //
                if (pup.DestinationPort.Network == 0 || pup.DestinationPort.Network == DirectoryServices.Instance.LocalHostAddress.Network)
                {                    
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

        private void ReceiveThread()
        {
            // Just call ReceivePackets, that's it.  This will never return.
            // (probably need to make this more elegant so we can tear down the thread
            // properly.)
            Log.Write(LogComponent.UDP, "UDP Receiver thread started.");

            IPEndPoint groupEndPoint = new IPEndPoint(IPAddress.Any, _udpPort);            

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

        private HandlePup _callback;

        // Thread used for receive
        private Thread _receiveThread;

        // UDP port (TODO: make configurable?)
        private const int _udpPort = 42424;
        private UdpClient _udpClient;
        private IPEndPoint _broadcastEndpoint;

        // The IP address (unicast address) of the interface we're using to send UDP datagrams.
        private IPAddress _thisIPAddress;

    }
}
