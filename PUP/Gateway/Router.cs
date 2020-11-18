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

using IFS.Logging;
using IFS.Transport;
using PcapDotNet.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;

namespace IFS.Gateway
{
    public delegate void RoutePupCallback(PUP pup, bool route);

    /// <summary>
    /// Implements gateway services, routing PUPs intended for other networks to
    /// their proper destination.
    /// This is one layer above the physical transport layer (ethernet, udp) and
    /// is below the protocol layer.
    /// 
    /// Routed PUPs are transferred over UDP, with the intent to connect other IFS 
    /// gateway servers (and thus the networks they serve) to each other either 
    /// over the Internet or over a local network.
    /// Since we're sending these routed PUPs via TCP/IP, it makes little sense 
    /// to support multi-hop PUP routing and since I tend to err on the side of simplicity
    /// in this implementation that's what's been implemented here.  Each IFS network
    /// known to the Router is assumed to be directly connected.
    /// </summary>
    public class Router
    {
        private Router()
        {
            _localProtocolDispatcher = new PUPProtocolDispatcher();
            _routingTable = new RoutingTable();

            //
            // Look up our own network in the table and get our port.
            // If we don't have an entry in the table, disable routing.
            //
            RoutingTableEntry ourNetwork = _routingTable.GetAddressForNetworkNumber(DirectoryServices.Instance.LocalNetwork);

            _gatewayUdpClientLock = new ReaderWriterLockSlim();

            if (ourNetwork == null)
            {
                Log.Write(LogType.Warning,
                    LogComponent.Routing, 
                        "networks.txt does not contain a definition for our network ({0}).  Gateway routing disabled.",
                        DirectoryServices.Instance.LocalNetwork);

                _gatewayUdpClient = null;
            }
            else
            {
                _gatewayUdpPort = ourNetwork.Port;

                // Start the external network receiver.
                BeginExternalReceive();
            }           
        }

        public static Router Instance
        {
            get
            {
                return _router;
            }
        }

        public RoutingTable RoutingTable
        {
            get
            {
                return _routingTable;
            }
        }

        public void Shutdown()
        {
            _localProtocolDispatcher.Shutdown();
            _pupPacketInterface.Shutdown();

            if (_gatewayUdpClient != null)
            {
                _gatewayUdpClientLock.EnterWriteLock();
                _gatewayUdpClient.Close();
                _gatewayUdpClientLock.ExitWriteLock();
            }
        }

        public void RegisterRAWInterface(LivePacketDevice iface)
        {
            Ethernet enet = new Ethernet(iface);

            _pupPacketInterface = enet;
            _rawPacketInterface = enet;
            _pupPacketInterface.RegisterRouterCallback(RouteIncomingLocalPacket);
        }

        public void RegisterUDPInterface(NetworkInterface iface)
        {
            UDPEncapsulation udp = new UDPEncapsulation(iface);

            _pupPacketInterface = udp;
            _rawPacketInterface = udp;
            _pupPacketInterface.RegisterRouterCallback(RouteIncomingLocalPacket);
        }

        /// <summary>
        /// Sends a PUP out to the world; this may be routed to a different network.
        /// </summary>
        /// <param name="p"></param>
        public void SendPup(PUP p)
        {
            RouteOutgoingPacket(p);
        }

        /// <summary>
        /// Sends a raw packet out to the world.  This packet will not be routed.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="source"></param>
        /// <param name="destination"></param>
        /// <param name="frameType"></param>
        public void Send(byte[] data, byte source, byte destination, ushort frameType)
        {
            if (_rawPacketInterface != null)
            {
                _rawPacketInterface.Send(data, source, destination, frameType);
            }
        }

        /// <summary>
        /// Routes a PUP out to the world.
        /// </summary>
        /// <param name="p"></param>
        private void RouteOutgoingPacket(PUP p)
        {
            //
            // Check the destination network.  If it's 0 (meaning the sender doesn't know
            // what network it's on, or wants it to go out to whatever network it's currently
            // connected to) or it's destined for our network, we send it out directly through 
            // the local network interface.
            //
            if (p.DestinationPort.Network == 0 ||
                p.DestinationPort.Network == DirectoryServices.Instance.LocalNetwork)
            {
                _pupPacketInterface.Send(p);
            }
            else
            {
                //
                // Not for our network -- see if we know what network this is going to.
                //
                RoutePacketExternally(p);
            }
        }

        /// <summary>
        /// Routes a locally received PUP to the proper destination host.
        /// </summary>
        /// <param name="pup"></param>
        public void RouteIncomingLocalPacket(PUP pup, bool route)
        {
            //
            // Check the network -- if it specifies network zero (coming from a host that doesn't yet know what
            // network it's on, or specifying the current network) or our network
            // we will pass it on to the protocol suite.
            //
            if (pup.DestinationPort.Network == 0 || pup.DestinationPort.Network == DirectoryServices.Instance.LocalHostAddress.Network)
            {
                _localProtocolDispatcher.ReceivePUP(pup);
            }
            else if (route)
            {
                //
                // Not for our network -- see if we know where to route it.
                //
                RoutePacketExternally(pup);
            }
            else
            {
                //
                // Not local, and we were asked not to route this PUP, so drop it on the floor.
                //
            }
        }

        private void RoutePacketExternally(PUP p)
        {
            RoutingTableEntry destinationNetworkEntry = _routingTable.GetAddressForNetworkNumber(p.DestinationPort.Network);

            if (destinationNetworkEntry != null)
            {
                //
                // Send this out through the external network interface.
                //
                if (_gatewayUdpClient != null)
                {
                    Log.Write(LogType.Verbose,
                           LogComponent.Routing,
                           "-> PUP routed to {0}:{1}, type {2} source {3} destination {4}.",
                           destinationNetworkEntry.HostAddress,
                           destinationNetworkEntry.Port,
                           p.Type,
                           p.SourcePort,
                           p.DestinationPort);


                    try
                    {
                        _gatewayUdpClientLock.EnterWriteLock();
                        _gatewayUdpClient.Send(p.RawData, p.RawData.Length, destinationNetworkEntry.HostAddress, destinationNetworkEntry.Port);
                    }
                    catch (Exception e)
                    {
                        Log.Write(LogType.Error,
                            LogComponent.Routing,
                            "Gateway UDP client send failed, error {0}.  Continuing.",
                            e.Message);
                    }
                    finally
                    {
                        _gatewayUdpClientLock.ExitWriteLock();
                    }
                }
            }
            else
            {
                //
                // We don't know where to send this, drop it instead.
                //
                Log.Write(
                    LogType.Verbose,
                    LogComponent.Routing,
                    "Outgoing PUP is for unknown network {0}, dropping.", p.DestinationPort.Network);
            }
        }        

        private void RouteIncomingExternalPacket(PUP p)
        {
            //
            // Ensure that this is for our network; otherwise this has been misrouted.
            // (Since we don't do multi-hop routing, any packet coming in through a gateway
            // interface must be destined for us.)
            //
            if (p.DestinationPort.Network == DirectoryServices.Instance.LocalNetwork)
            {
                //
                // And if it's intended for us (the IFS server) let our services have a crack at it, too.
                //
                if (p.DestinationPort.Host == DirectoryServices.Instance.LocalHostAddress.Host ||       // us specifically
                    p.DestinationPort.Host == 0)                                                        // broadcast
                {
                    _localProtocolDispatcher.ReceivePUP(p);
                }

                //
                // Send it out on the local network for anyone to see if it's not for us, or if it's a broadcast.
                //
                if (p.DestinationPort.Host != DirectoryServices.Instance.LocalHostAddress.Host ||       // not us
                    p.DestinationPort.Host == 0)                                                        // broadcast
                {
                    _pupPacketInterface.Send(p);
                }
            }
            else
            {
                //
                // This was misrouted.  Log it and drop.
                //
                Log.Write(LogType.Error,
                    LogComponent.Routing,
                    "PUP was misrouted; intended for network {0}, host {1}",
                    p.DestinationPort.Network,
                    p.DestinationPort.Host);
            }
        }

        private void BeginExternalReceive()
        {
            CreateGatewayReceiver();

            // Kick off receive thread.   
            _gatewayReceiveThread = new Thread(GatewayReceiveThread);
            _gatewayReceiveThread.Start();
        }

        private void CreateGatewayReceiver()
        {
            _gatewayUdpClientLock.EnterWriteLock();

            if (_gatewayUdpClient != null)
            {
                _gatewayUdpClient.Close();
            }

            _gatewayUdpClient = new UdpClient(_gatewayUdpPort, AddressFamily.InterNetwork);
            _gatewayUdpClient.Client.Blocking = true;
            
            _gatewayUdpClientLock.ExitWriteLock();
        }

        /// <summary>
        /// Worker thread for UDP packet receipt.
        /// </summary>
        private void GatewayReceiveThread()
        {
            // Just call Receive forever, that's it.  This will never return.
            // (probably need to make this more elegant so we can tear down the thread
            // properly.)
            Log.Write(LogComponent.Routing, "Gateway UDP Receiver thread started for port {0}.", _gatewayUdpPort);

            IPEndPoint groupEndPoint = new IPEndPoint(IPAddress.Any, _gatewayUdpPort);

            while (true)
            {
                byte[] data = null;
                try
                {
                    data = _gatewayUdpClient.Receive(ref groupEndPoint);
                }
                catch(Exception e)
                {
                    //
                    // This can happen on occasion for reasons I don't quite understand.
                    // We will log the failure and attempt to continue.
                    //
                    Log.Write(LogType.Error, 
                        LogComponent.Routing, 
                        "Gateway UDP client receive failed, error {0}.  Continuing.",
                        e.Message);

                    continue;
                }
                
                // 1) validate packet
                // 2) get a PUP out of it
                // 3) send to RouteIncomingPacket.
                // 4) do it again.
                if (data.Length < PUP.PUP_HEADER_SIZE + PUP.PUP_CHECKSUM_SIZE ||
                    data.Length > PUP.MAX_PUP_SIZE + PUP.PUP_HEADER_SIZE + PUP.PUP_CHECKSUM_SIZE)
                {
                    Log.Write(LogType.Error, LogComponent.Routing, "External PUP has an invalid size ({0}).  Dropping.", data.Length);
                    continue;
                }

                try
                {
                    //
                    // See if we can get a PUP out of this.
                    //
                    PUP externalPUP = new PUP(new MemoryStream(data), data.Length);

                    //
                    // TODO: should technically bump the PUP's TransportControl field up;
                    // really need to rewrite the PUP class to make this possible without
                    // building up an entirely new PUP.
                    //
                    RouteIncomingExternalPacket(externalPUP);

                    Log.Write(LogType.Verbose,
                        LogComponent.Routing,
                        "<- External PUP received from {0}:{1}, type {2} source {3} destination {4}.  Routing to local network.",
                        groupEndPoint.Address,
                        groupEndPoint.Port,
                        externalPUP.Type,
                        externalPUP.SourcePort,
                        externalPUP.DestinationPort);
                }
                catch (Exception e)
                {
                    Log.Write(LogType.Error, LogComponent.Routing, "Error handling external PUP: {0}", e.Message);
                }
                
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
        /// Our UdpClient for sending PUPs to external networks.
        /// </summary>
        private UdpClient _gatewayUdpClient;

        /// <summary>
        /// Used to ensure thread-safety for the UDP client (which is not thread-safe)
        /// </summary>
        private ReaderWriterLockSlim _gatewayUdpClientLock;

        /// <summary>
        /// Thread to watch for incoming external PUPs
        /// </summary>
        private Thread _gatewayReceiveThread;

        /// <summary>
        /// The UDP port we use for our gateway, as specified in networks.txt
        /// </summary>
        private int _gatewayUdpPort;

        private RoutingTable _routingTable;

        private static Router _router = new Router();

        private PUPProtocolDispatcher _localProtocolDispatcher;
    }

    public class RoutingTableEntry
    {
        public RoutingTableEntry(string hostAddress, int port)
        {
            HostAddress = hostAddress;
            Port = port;
        }

        public string HostAddress;
        public int Port;
    }

    public class RoutingTable
    {
        public RoutingTable()
        {
            LoadRoutingTables();
        }

        /// <summary>
        /// Returns the host address of the gateway server for the specified inter-
        /// network number.  Returns null if no address is defined.
        /// </summary>
        /// <param name="networkNumber"></param>
        /// <returns></returns>
        public RoutingTableEntry GetAddressForNetworkNumber(byte networkNumber)
        {
            if (_addressTable.ContainsKey(networkNumber))
            {
                return _addressTable[networkNumber];
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Returns the inter-network number for the network served by the given
        /// host address.  Returns 0 (undefined network) if no number is defined
        /// for the given address.
        /// </summary>
        /// <param name="address"></param>
        /// <returns></returns>
        public byte GetNetworkNumberForAddress(RoutingTableEntry address)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Returns an array containing the numbers of networks that have been defined.
        /// This is used by Gateway Services to return routing information.
        /// </summary>
        /// <returns></returns>
        public byte[] GetKnownNetworks()
        {
            byte[] networks = new byte[_addressTable.Keys.Count];
            _addressTable.Keys.CopyTo(networks, 0);

            return networks;
        }

        private void LoadRoutingTables()
        {
            _addressTable = new Dictionary<byte, RoutingTableEntry>();
            //
            // Read in the routing tables from Conf\networks.txt.
            //
            using (StreamReader sr = new StreamReader(Path.Combine("Conf", "networks.txt")))
            {
                int lineNumber = 0;
                while (!sr.EndOfStream)
                {
                    lineNumber++;

                    //
                    // A line is either:
                    //  '#' followed by comment to EOL
                    // <inter-network name> <hostname>
                    // Any whitespace is ignored
                    // 
                    // Format for Inter-Network name expressions for a network is:
                    //  network#  (to specify hosts on another network)
                    //
                    string line = sr.ReadLine().Trim().ToLowerInvariant();

                    if (line.StartsWith("#") || String.IsNullOrWhiteSpace(line))
                    {
                        // Comment or empty, just ignore
                        continue;
                    }

                    // Tokenize on whitespace
                    string[] tokens = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                    // We need exactly two tokens (inter-network name and one hostname)
                    if (tokens.Length != 2)
                    {
                        // Log warning and continue.
                        Log.Write(LogType.Warning, LogComponent.Routing,
                            "networks.txt line {0}: Invalid syntax.", lineNumber);

                        continue;
                    }

                    // First token should be an inter-network name, which should end with '#'.
                    if (!tokens[0].EndsWith("#"))
                    {
                        // Log warning and continue.
                        Log.Write(LogType.Warning, LogComponent.Routing,
                            "networks.txt line {0}: Improperly formed inter-network name '{1}'.", lineNumber, tokens[0]);

                        continue;
                    }

                    // tokenize on '#'
                    string[] networkTokens = tokens[0].Split(new char[] { '#' }, StringSplitOptions.RemoveEmptyEntries);

                    byte networkNumber = 0;
                    // 1 token means a network name, anything else is invalid here
                    if (networkTokens.Length == 1)
                    {
                        try
                        {
                            networkNumber = Convert.ToByte(networkTokens[0], 8);
                        }
                        catch
                        {
                            // Log warning and continue.
                            Log.Write(LogType.Warning, LogComponent.Routing,
                                "hosts.txt line {0}: Invalid network number in inter-network address '{1}'.", lineNumber, tokens[0]);

                            continue;
                        }
                    }
                    else
                    {
                        // Log warning and continue.
                        Log.Write(LogType.Warning, LogComponent.Routing,
                            "networks.txt line {0}: Invalid network number in inter-network address '{1}'.", lineNumber, tokens[0]);
                        continue;
                    }

                    if (_addressTable.ContainsKey(networkNumber))
                    {
                        // Log warning and continue.
                        Log.Write(LogType.Warning, LogComponent.Routing,
                            "networks.txt line {0}: Duplicate network entry '{1}'.", lineNumber, networkNumber);
                        continue;
                    }

                    //
                    // The 2nd token contains the hostname for the network gateway.
                    // This could be a domain name or an IP address (V4 only for the moment)
                    // with or without port.
                    //
                    string hostName = tokens[1];

                    string[] hostNameParts = hostName.Split(':');

                    string address = String.Empty;
                    int port = 0;

                    if (hostNameParts.Length == 2)
                    {
                        try
                        {
                            // Hostname + port
                            address = hostNameParts[0];
                            port = int.Parse(hostNameParts[1]);

                            if (port <= 0 || port > 65535)
                            {
                                throw new InvalidOperationException("Port number is out of range.");
                            }
                        }
                        catch (Exception)
                        {
                            Log.Write(LogType.Warning, LogComponent.Routing,
                                "networks.txt line {0}: Invalid hostname specification '{1}'.", lineNumber, hostName);
                            continue;
                        }
                    }
                    else if (hostNameParts.Length == 1)
                    {
                        address = hostNameParts[0];
                        port = DefaultUDPPort;
                    }
                    else
                    {
                        Log.Write(LogType.Warning, LogComponent.Routing,
                                "networks.txt line {0}: Invalid hostname specification '{1}'.", lineNumber, hostName);
                        continue;
                    }

                    // Add entry to the table.
                    _addressTable.Add(networkNumber, new RoutingTableEntry(address, port));
                }

            }
        }

        /// <summary>
        /// The default UDP port for external networks.
        /// </summary>
        private static readonly int DefaultUDPPort = 42425;

        private Dictionary<byte, RoutingTableEntry> _addressTable;
    }

}
