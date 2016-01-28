using IFS.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IFS
{

    public abstract class BSPProtocol : PUPProtocolBase
    {
        public abstract void InitializeServerForChannel(BSPChannel channel);
    }

    public enum BSPState
    {
        Unconnected,
        Connected
    }

    public class BSPChannel
    {
        public BSPChannel(PUP rfcPup, UInt32 socketID, BSPProtocol protocolHandler)
        {
            _inputLock = new ReaderWriterLockSlim();
            _outputLock = new ReaderWriterLockSlim();

            _inputWriteEvent = new AutoResetEvent(false);

            _inputQueue = new Queue<byte>(65536);

            _outputAckEvent = new AutoResetEvent(false);

            _protocolHandler = protocolHandler;

            // TODO: init IDs, etc. based on RFC PUP
            _start_pos = _recv_pos = _send_pos = rfcPup.ID;

            // Set up socket addresses.
            // The client sends the connection port it prefers to use
            // in the RFC pup.
            _clientConnectionPort = new PUPPort(rfcPup.Contents, 0);

            // 
            if (_clientConnectionPort.Network == 0)
            {
                _clientConnectionPort.Network = DirectoryServices.Instance.LocalNetwork;
            }

            // We create our connection port using a unique socket address.
            _serverConnectionPort = new PUPPort(DirectoryServices.Instance.LocalHostAddress, socketID);            
        }
       
        public PUPPort ClientPort
        {
            get { return _clientConnectionPort; }
        }

        public PUPPort ServerPort
        {
            get { return _serverConnectionPort; }
        }

        public void Destroy()
        {
            
        }

        /// <summary>
        /// Reads data from the channel (i.e. from the client).  Will block if not all the requested data is available.
        /// </summary>
        /// <returns></returns>
        public int Read(ref byte[] data, int count)
        {
            return Read(ref data, count, 0);
        }

        /// <summary>
        /// Reads data from the channel (i.e. from the client).  Will block if not all the requested data is available.
        /// </summary>
        /// <returns></returns>
        public int Read(ref byte[] data, int count, int offset)
        {            
            // sanity check
            if (count > data.Length)
            {
                throw new InvalidOperationException("count must be less than or equal to the length of the buffer being read into.");
            }

            int read = 0;

            // Loop until the data we asked for arrives or until we time out waiting.
            // TODO: handle partial transfers due to aborted BSPs.
            while (true)
            {
                _inputLock.EnterUpgradeableReadLock();
                if (_inputQueue.Count >= count)
                {
                    _inputLock.EnterWriteLock();
                    // We have the data right now, read it and return.
                    // TODO: this is a really inefficient thing.
                    for (int i = 0; i < count; i++)
                    {
                        data[i + offset] = _inputQueue.Dequeue();
                    }
                         
                    _inputLock.ExitWriteLock();
                    _inputLock.ExitUpgradeableReadLock();

                    break;
                }
                else
                {
                    _inputLock.ExitUpgradeableReadLock();

                    // Not enough data in the queue.
                    // Wait until we have received more data, then try again.
                    _inputWriteEvent.WaitOne(); // TODO: timeout and fail
                }
            }

            return read;
        }

        public byte ReadByte()
        {
            // TODO: optimize this
            byte[] data = new byte[1];

            Read(ref data, 1);

            return data[0];
        }

        public ushort ReadUShort()
        {
            // TODO: optimize this
            byte[] data = new byte[2];

            Read(ref data, 2);

            return Helpers.ReadUShort(data, 0);
        }

        public BCPLString ReadBCPLString()
        {
            return new BCPLString(this);
        }

        /// <summary>
        /// Appends incoming client data into the input queue (called from BSPManager to place new PUP data into the BSP stream)
        /// </summary>        
        public void WriteQueue(PUP dataPUP)
        {
            // If we are over our high watermark, we will drop the data (and not send an ACK even if requested).
            // Clients should be honoring the limits we set in the RFC packets.
            _inputLock.EnterUpgradeableReadLock();
            if (_inputQueue.Count + dataPUP.Contents.Length > MaxBytes)
            {
                _inputLock.ExitUpgradeableReadLock();
                return;                
            }      
            
            // Sanity check on expected position from sender vs. received data on our end.
            // If they don't match then we've lost a packet somewhere.
            if (dataPUP.ID != _recv_pos)
            {
                // Current behavior is to simply drop all incoming PUPs (and not ACK them) until they are re-sent to us
                // (in which case the above sanity check will pass).  According to spec, AData requests that are not ACKed
                // must eventually be resent.  This is far simpler than accepting out-of-order data and keeping track
                // of where it goes in the queue, though less efficient.
                _inputLock.ExitUpgradeableReadLock();
                return;
            }
                  

            // Prepare to add data to the queue
            // Again, this is really inefficient
            _inputLock.EnterWriteLock();

            for (int i = 0; i < dataPUP.Contents.Length; i++)
            {
                _inputQueue.Enqueue(dataPUP.Contents[i]);

                //Console.Write("{0:x} ({1}), ", dataPUP.Contents[i], (char)dataPUP.Contents[i]);
            }

            _recv_pos += (UInt32)dataPUP.Contents.Length;

            _inputLock.ExitWriteLock();

            _inputLock.ExitUpgradeableReadLock();

            _inputWriteEvent.Set();

            // If the client wants an ACK, send it now.
            if ((PupType)dataPUP.Type == PupType.AData)
            {
                SendAck();
            }

        }

        /// <summary>
        /// Sends data to the channel (i.e. to the client).  Will block (waiting for an ACK) if an ACK is requested.
        /// </summary>
        /// <param name="data">The data to be sent</param>
        public void Send(byte[] data)
        {
            // Write data to the output stream.
            // For now, we request ACKs for every pup sent.
            // TODO: should buffer data until an entire PUP's worth is ready
            // (and split data that's too large into multiple PUPs.)
            PUP dataPup = new PUP(PupType.AData, _send_pos, _clientConnectionPort, _serverConnectionPort, data);

            PUPProtocolDispatcher.Instance.SendPup(dataPup);

            _send_pos += (uint)data.Length;            

            // Await an ack for the PUP we just sent            
            _outputAckEvent.WaitOne(); // TODO: timeout and fail
        }

        /// <summary>
        /// Invoked when the client sends an ACK
        /// </summary>
        /// <param name="ackPUP"></param>
        public void Ack(PUP ackPUP)
        {
            // Update receiving end stats (max PUPs, etc.)
            // Ensure client's position matches ours
            if (ackPUP.ID != _send_pos)
            {
                Log.Write(LogLevel.BSPLostPacket,
                    String.Format("Client position != server position for BSP {0} ({1} != {2})",
                        _serverConnectionPort.Socket,
                        ackPUP.ID,
                        _send_pos));
            }


            // Let any waiting threads continue
            _outputAckEvent.Set();
        }

        /*
        public void Mark(byte type);
        public void Interrupt();

        public void Abort(int code, string message);
        public void Error(int code, string message);

        public void End(); 
        */

        // TODO:
        // Events for:
        //   Abort, End, Mark, Interrupt (from client)
        //   Repositioning (due to lost packets) (perhaps not necessary)
        // to allow protocols consuming BSP streams to be alerted when things happen.
        //     

        private void SendAck()
        {
            PUP ackPup = new PUP(PupType.Ack, _recv_pos, _clientConnectionPort, _serverConnectionPort);

            PUPProtocolDispatcher.Instance.SendPup(ackPup);
        }

        private BSPProtocol _protocolHandler;

        private UInt32   _recv_pos;
        private UInt32   _send_pos;
        private UInt32   _start_pos;

        private PUPPort  _clientConnectionPort;      // the client port
        private PUPPort  _serverConnectionPort;      // the server port we (the server) have established for communication

        private ReaderWriterLockSlim                _inputLock;
        private System.Threading.AutoResetEvent     _inputWriteEvent;

        private ReaderWriterLockSlim _outputLock;

        private System.Threading.AutoResetEvent     _outputAckEvent;

        // TODO: replace this with a more efficient structure for buffering data
        private Queue<byte> _inputQueue;

        // Constants

        // For now, we work on one PUP at a time.
        private const int MaxPups = 1;
        private const int MaxPupSize = 532;
        private const int MaxBytes = 1 * 532;
    }

    /// <summary>
    /// 
    /// </summary>
    public static class BSPManager
    {
        static BSPManager()
        {
            //
            // Initialize the socket ID counter; we start with a
            // number beyond the range of well-defined sockets.
            // For each new BSP channel that gets opened, we will
            // increment this counter to ensure that each channel gets
            // a unique ID.  (Well, until we wrap around...)
            //
            _nextSocketID = _startingSocketID;

            _activeChannels = new Dictionary<uint, BSPChannel>();
        }

        /// <summary>
        /// Called when a PUP comes in on a known BSP socket
        /// </summary>
        /// <param name="p"></param>
        public static void EstablishRendezvous(PUP p, BSPProtocol protocolHandler)
        {
            if (p.Type != PupType.RFC)
            {
                Log.Write(LogLevel.Error, String.Format("Expected RFC pup, got {0}", p.Type));
                return;
            }
            
            UInt32 socketID = GetNextSocketID();
            BSPChannel newChannel = new BSPChannel(p, socketID, protocolHandler);
            _activeChannels.Add(socketID, newChannel);          

            //
            // Initialize the server for this protocol.
            protocolHandler.InitializeServerForChannel(newChannel);

            // Send RFC response to complete the rendezvous.

            // Modify the destination port to specify our network
            PUPPort sourcePort = p.DestinationPort;
            sourcePort.Network = DirectoryServices.Instance.LocalNetwork;
            PUP rfcResponse = new PUP(PupType.RFC, p.ID, newChannel.ClientPort, sourcePort, newChannel.ServerPort.ToArray());

            PUPProtocolDispatcher.Instance.SendPup(rfcResponse);
        }

        /// <summary>
        /// Called when BSP-based protocols receive data.
        /// </summary>
        /// <returns>
        /// null if no new channel is created due to the sent PUP (not an RFC)
        /// a new BSPChannel if one has been created based on the PUP (new RFC)
        /// </returns>
        /// <param name="p"></param>
        public static void RecvData(PUP p)
        {            
            BSPChannel channel = FindChannelForPup(p);

            if (channel == null)
            {
                Log.Write(LogLevel.Error, "Received BSP PUP on an unconnected socket, ignoring.");
                return;
            }

            switch (p.Type)
            {
                case PupType.RFC:
                    Log.Write(LogLevel.Error, "Received RFC on established channel, ignoring.");
                    break;

                case PupType.Data:
                case PupType.AData:
                    {           
                        channel.WriteQueue(p);                                             
                    }
                    break;

                case PupType.Ack:
                    {                        
                        channel.Ack(p);                        
                    }
                    break;

                case PupType.End:
                    {                        
                        //channel.EndReply();
                    }
                    break;

                case PupType.Abort:
                    {
                        // TODO: tear down the channel
                        DestroyChannel(channel);

                        string abortMessage = Helpers.ArrayToString(p.Contents);

                        Log.Write(LogLevel.Warning, String.Format("BSP aborted, message: '{0}'", abortMessage));
                    }
                    break;

                default:
                    throw new NotImplementedException(String.Format("Unhandled BSP PUP type {0}.", p.Type));

            }                    
        }

        public static bool ChannelExistsForSocket(PUP p)
        {
            return FindChannelForPup(p) != null;
        }

        public static void DestroyChannel(BSPChannel channel)
        {
            channel.Destroy();

            _activeChannels.Remove(channel.ServerPort.Socket);
        }

        private static BSPChannel FindChannelForPup(PUP p)
        {
            if (_activeChannels.ContainsKey(p.DestinationPort.Socket))
            {
                return _activeChannels[p.DestinationPort.Socket];
            }
            else
            {
                return null;
            }
        }

        private static UInt32 GetNextSocketID()
        {
            UInt32 next = _nextSocketID;

            _nextSocketID++;

            //
            // Handle the wrap around case (which we're very unlikely to
            // ever hit, but why not do the right thing).
            // Start over at the initial ID.  This is very unlikely to
            // collide with any pending channels.
            //
            if(_nextSocketID < _startingSocketID)
            {
                _nextSocketID = _startingSocketID;
            }

            return next;
        }


        /// <summary>
        /// Map from socket address to BSP channel
        /// </summary>
        private static Dictionary<UInt32, BSPChannel> _activeChannels;

        private static UInt32 _nextSocketID;
        private static readonly UInt32 _startingSocketID = 0x1000;
    }
}
