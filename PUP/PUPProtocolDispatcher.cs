using IFS.BSP;
using IFS.EFTP;
using IFS.Logging;

using System;
using System.Collections.Generic;
using IFS.CopyDisk;
using IFS.FTP;
using IFS.Gateway;

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
        public PUPProtocolDispatcher()
        {
            _dispatchMap = new Dictionary<uint, PUPProtocolEntry>();

            RegisterProtocols();
        }   
        
        public void Shutdown()
        {
            _breathOfLifeServer.Shutdown();
            BSPManager.Shutdown();
            //EFTPManager.Shutdown();
        }                    

        public void ReceivePUP(PUP pup)
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
                Log.Write(LogType.Verbose, LogComponent.PUP, "PUP is neither broadcast nor for us.  Discarding.");
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
                    BSPManager.EstablishRendezvous(pup, entry.WorkerType);
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
        /// Registers a new protocol with the dispatcher.
        /// </summary>
        /// <param name="reg"></param>
        /// <param name="impl"></param>
        private void RegisterProtocol(PUPProtocolEntry entry)
        {
            if (_dispatchMap.ContainsKey(entry.Socket))
            {
                throw new InvalidOperationException(
                    String.Format("Socket {0} has already been registered for protocol {1}", entry.Socket, _dispatchMap[entry.Socket].FriendlyName));
            }

            _dispatchMap[entry.Socket] = entry;
        }

        private void RegisterProtocols()
        {
            // Set up protocols:

            // Connectionless
            RegisterProtocol(new PUPProtocolEntry("Gateway Information", 2, ConnectionType.Connectionless, new GatewayInformationProtocol()));
            RegisterProtocol(new PUPProtocolEntry("Misc Services", 0x4, ConnectionType.Connectionless, new MiscServicesProtocol()));
            RegisterProtocol(new PUPProtocolEntry("Echo", 0x5, ConnectionType.Connectionless, new EchoProtocol()));

            // RTP/BSP based:            
            RegisterProtocol(new PUPProtocolEntry("CopyDisk", 0x15  /* 25B */, ConnectionType.BSP, typeof(CopyDiskWorker)));
            RegisterProtocol(new PUPProtocolEntry("FTP", 0x3, ConnectionType.BSP, typeof(FTPWorker)));
            RegisterProtocol(new PUPProtocolEntry("Mail", 0x7, ConnectionType.BSP, typeof(FTPWorker)));

            // Breath Of Life
            _breathOfLifeServer = new BreathOfLife();
        }

        /// <summary>
        /// Map from socket to protocol implementation
        /// </summary>
        private Dictionary<UInt32, PUPProtocolEntry> _dispatchMap;

        //
        // Breath of Life server, which is its own thing.
        private BreathOfLife _breathOfLifeServer;
    }
}
