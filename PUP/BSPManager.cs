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

    public struct BSPAck
    {
        public ushort MaxBytes;
        public ushort MaxPups;
        public ushort BytesSent;
    }

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
            _outputQueue = new Queue<byte>(65536);

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
            if (OnDestroy != null)
            {
                OnDestroy();
            }
        }

        public void End(PUP p)
        {
            PUP endReplyPup = new PUP(PupType.EndReply, p.ID, _clientConnectionPort, _serverConnectionPort, new byte[0]);
            PUPProtocolDispatcher.Instance.SendPup(endReplyPup);

            // "The receiver of the End PUP responds by returning an EndReply Pup with matching ID and then 
            //  _dallying_ up to some reasonably long timeout interval (say, 10 seconds) in order to respond to
            // a retransmitted End Pup should its initial EndReply be lost.  If and when the dallying end of  the
            // stream connection receives its EndReply, it may immediately self destruct."
            // TODO: actually make this happen...

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
                    if (!_inputWriteEvent.WaitOne(BSPReadTimeoutPeriod))
                    {
                        Log.Write(LogType.Error, LogComponent.BSP, "Timed out waiting for data on read, aborting connection.");
                        // We timed out waiting for data, abort the connection.
                        SendAbort("Timeout on read.");
                        BSPManager.DestroyChannel(this);
                    }
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
        public void RecvWriteQueue(PUP dataPUP)
        {
            // If we are over our high watermark, we will drop the data (and not send an ACK even if requested).
            // Clients should be honoring the limits we set in the RFC packets.
            _inputLock.EnterUpgradeableReadLock();

            /*
            if (_inputQueue.Count + dataPUP.Contents.Length > MaxBytes)
            {
                Log.Write(LogLevel.Error, "Queue larger than {0} bytes, dropping.");
                _inputLock.ExitUpgradeableReadLock();
                return;                
            } */
            
            // Sanity check on expected position from sender vs. received data on our end.
            // If they don't match then we've lost a packet somewhere.
            if (dataPUP.ID != _recv_pos)
            {
                // Current behavior is to simply drop all incoming PUPs (and not ACK them) until they are re-sent to us
                // (in which case the above sanity check will pass).  According to spec, AData requests that are not ACKed
                // must eventually be resent.  This is far simpler than accepting out-of-order data and keeping track
                // of where it goes in the queue, though less efficient.
                _inputLock.ExitUpgradeableReadLock();
                Log.Write(LogType.Error, LogComponent.BSP, "Lost Packet, client ID does not match our receive position ({0} != {1})", dataPUP.ID, _recv_pos);
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
        /// Sends data, with immediate flush.
        /// </summary>
        /// <param name="data"></param>
        public void Send(byte[] data)
        {
            Send(data, true /* flush */);
        }

        /// <summary>
        /// Sends data to the channel (i.e. to the client).  Will block (waiting for an ACK) if an ACK is requested.
        /// </summary>
        /// <param name="data">The data to be sent</param>
        /// <param name="flush">Whether to flush data out immediately or to wait for enough for a full PUP first.</param>
        public void Send(byte[] data, bool flush)
        {
            // Add output data to output queue.
            // Again, this is really inefficient
            for (int i = 0; i < data.Length; i++)
            {
                _outputQueue.Enqueue(data[i]);               
            }

            if (flush || _outputQueue.Count >= PUP.MAX_PUP_SIZE)
            {                
                // Send data until all is used (for a flush) or until we have less than a full PUP (non-flush).
                while (_outputQueue.Count >= (flush ? 1 : PUP.MAX_PUP_SIZE))
                {
                    byte[] chunk = new byte[Math.Min(PUP.MAX_PUP_SIZE, _outputQueue.Count)];

                    // Ugh.
                    for (int i = 0; i < chunk.Length; i++)
                    {
                        chunk[i] = _outputQueue.Dequeue();
                    }

                    // Send the data, retrying as necessary.
                    int retry;
                    for (retry = 0; retry < BSPRetryCount; retry++)
                    {
                        PUP dataPup = new PUP(PupType.AData, _send_pos, _clientConnectionPort, _serverConnectionPort, chunk);
                        PUPProtocolDispatcher.Instance.SendPup(dataPup);

                        _send_pos += (uint)chunk.Length;

                        // Await an ack for the PUP we just sent.  If we timeout, we will retry.
                        //     
                        if (_outputAckEvent.WaitOne(BSPAckTimeoutPeriod))
                        {
                            break;
                        }

                        Log.Write(LogType.Warning, LogComponent.BSP, "ACK not received for sent data, retrying.");
                    }

                    if (retry >= BSPRetryCount)
                    {
                        Log.Write(LogType.Error, LogComponent.BSP, "ACK not received after retries, aborting connection.");
                        SendAbort("ACK not received for sent data.");
                        BSPManager.DestroyChannel(this);
                    }

                }
            }           
        }

        public void SendAbort(string message)
        {
            PUP abortPup = new PUP(PupType.Abort, _start_pos, _clientConnectionPort, _serverConnectionPort, Helpers.StringToArray(message));
            PUPProtocolDispatcher.Instance.SendPup(abortPup);
        }

        /// <summary>
        /// Invoked when the client sends an ACK
        /// </summary>
        /// <param name="ackPUP"></param>
        public void RecvAck(PUP ackPUP)
        {
            // Update receiving end stats (max PUPs, etc.)
            // Ensure client's position matches ours
            if (ackPUP.ID != _send_pos)
            {
                Log.Write(LogType.Warning, LogComponent.BSP,
                    "Client position != server position for BSP {0} ({1} != {2})",
                    _serverConnectionPort.Socket,
                    ackPUP.ID,
                    _send_pos);
            }            

            BSPAck ack = (BSPAck)Serializer.Deserialize(ackPUP.Contents, typeof(BSPAck));            


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

        public delegate void DestroyDelegate();

        public DestroyDelegate OnDestroy;

        private void SendAck()
        {
            _inputLock.EnterReadLock();
            BSPAck ack = new BSPAck();
            ack.MaxBytes = MaxBytes;  //(ushort)(MaxBytes - _inputQueue.Count);
            ack.MaxPups = MaxPups;
            ack.BytesSent = MaxBytes;  //(ushort)(MaxBytes - _inputQueue.Count);
            _inputLock.ExitReadLock();

            PUP ackPup = new PUP(PupType.Ack, _recv_pos, _clientConnectionPort, _serverConnectionPort, Serializer.Serialize(ack));

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
        private Queue<byte> _outputQueue;

        // Constants

        // For now, we work on one PUP at a time.
        private const int MaxPups = 1;
        private const int MaxPupSize = 532;
        private const int MaxBytes = 1 * 532;


        private const int BSPRetryCount = 5;
        private const int BSPAckTimeoutPeriod = 1000;       // 1 second
        private const int BSPReadTimeoutPeriod = 60000;     // 1 minute
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
                Log.Write(LogType.Error, LogComponent.RTP, "Expected RFC pup, got {0}", p.Type);
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

            Log.Write(LogComponent.RTP, 
                "Establishing Rendezvous, ID {0}, Server port {1}, Client port {2}.", 
                p.ID, newChannel.ServerPort, newChannel.ClientPort);

            PUPProtocolDispatcher.Instance.SendPup(rfcResponse);            
        }

        /// <summary>
        /// Called when BSP-based protocols receive data.
        /// </summary>
        /// <param name="p"></param>
        public static void RecvData(PUP p)
        {            
            BSPChannel channel = FindChannelForPup(p);

            if (channel == null)
            {
                Log.Write(LogType.Error, LogComponent.BSP, "Received BSP PUP on an unconnected socket, ignoring.");
                return;
            }

            switch (p.Type)
            {
                case PupType.RFC:
                    Log.Write(LogType.Error, LogComponent.BSP, "Received RFC on established channel, ignoring.");
                    break;

                case PupType.Data:
                case PupType.AData:
                    {                        
                        channel.RecvWriteQueue(p);                                             
                    }
                    break;

                case PupType.Ack:
                    {                        
                        channel.RecvAck(p);                        
                    }
                    break;
                    
                case PupType.End:
                    {
                        // Second step of tearing down a connection, the End from the client, to which we will
                        // send an EndReply, expecting a second EndReply.
                        channel.End(p);                        
                    }
                    break;

                case PupType.EndReply:
                    {
                        // Last step of tearing down a connection, the EndReply from the client.
                        DestroyChannel(channel);
                    }
                    break;

                case PupType.Abort:
                    {                                                
                        string abortMessage = Helpers.ArrayToString(p.Contents);
                        Log.Write(LogType.Warning, LogComponent.RTP, String.Format("BSP aborted, message: '{0}'", abortMessage));

                        DestroyChannel(channel);
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
