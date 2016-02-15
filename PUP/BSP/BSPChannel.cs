using IFS.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IFS.BSP
{
    /// <summary>
    /// Provides functionality for maintaining/terminating BSP connections, and the transfer of data
    /// across said connection.
    /// 
    /// Implementation currenty provides (apparently) proper "windows" for sending data to client;
    /// only one PUP at a time is accepted for input at the moment.  This should likely be corrected,
    /// but is not likely to improve performance altogether that much.
    /// </summary>
    public class BSPChannel
    {
        public BSPChannel(PUP rfcPup, UInt32 socketID, BSPProtocol protocolHandler)
        {
            _inputLock = new ReaderWriterLockSlim();
            _outputLock = new ReaderWriterLockSlim();

            _inputWriteEvent = new AutoResetEvent(false);
            _inputQueue = new Queue<ushort>(65536);

            _outputAckEvent = new AutoResetEvent(false);
            _outputReadyEvent = new AutoResetEvent(false);
            _dataReadyEvent = new AutoResetEvent(false);
            _outputQueue = new Queue<byte>(65536);

            _outputWindow = new List<PUP>(16);
            _outputWindowLock = new ReaderWriterLockSlim();

            _protocolHandler = protocolHandler;

            // Init IDs, etc. based on RFC PUP
            _lastClientRecvPos = _startPos = _recvPos = _sendPos = rfcPup.ID;

            // Set up socket addresses.
            // The client sends the connection port it prefers to use
            // in the RFC pup.
            _clientConnectionPort = new PUPPort(rfcPup.Contents, 0);

            // If the client doesn't know what network it's on, it's now on ours.
            if (_clientConnectionPort.Network == 0)
            {
                _clientConnectionPort.Network = DirectoryServices.Instance.LocalNetwork;
            }

            // We create our connection port using a unique socket address.
            _serverConnectionPort = new PUPPort(DirectoryServices.Instance.LocalHostAddress, socketID);

            //
            // Init MaxPups to indicate that we need to find out what the client actually supports when we first
            // start sending data.
            //
            _clientLimits.MaxPups = 0xffff;

            // Create our consumer thread for output and kick it off.
            _consumerThread = new Thread(OutputConsumerThread);
            _consumerThread.Start();
        }

        public delegate void DestroyDelegate();

        public DestroyDelegate OnDestroy;

        /// <summary>
        /// The port we use to talk to the client.
        /// </summary>
        public PUPPort ClientPort
        {
            get { return _clientConnectionPort; }
        }

        /// <summary>
        /// The port the client uses to talk to us.
        /// </summary>
        public PUPPort ServerPort
        {
            get { return _serverConnectionPort; }
        }

        /// <summary>
        /// Returns the last Mark byte received, if any.
        /// </summary>
        public byte LastMark
        {
            get { return _lastMark; }
        }        

        /// <summary>
        /// Performs cleanup on this channel and notifies anyone who's interested that
        /// the channel has been destroyed.
        /// </summary>
        public void Destroy()
        {         
            _consumerThread.Abort();

            if (OnDestroy != null)
            {
                OnDestroy();
            }
        }

        /// <summary>
        /// Handles an End request from the client.
        /// </summary>
        /// <param name="p"></param>
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
        /// If a Mark byte is encountered, will return a short read.
        /// </summary>
        /// <returns></returns>
        public int Read(ref byte[] data, int count, int offset)
        {
            // sanity check
            if (count + offset > data.Length)
            {
                throw new InvalidOperationException("count + offset must be less than or equal to the length of the buffer being read into.");
            }

            int read = 0;

            //
            // Loop until either:
            // - all the data we asked for arrives 
            // - we get a Mark byte
            // - we time out waiting for data
            //
            bool done = false;
            while (!done)
            {
                _inputLock.EnterUpgradeableReadLock();
                if (_inputQueue.Count > 0)
                {
                    _inputLock.EnterWriteLock();

                    // We have some data right now, read it in.
                    // TODO: this code is ugly and it wants to die.
                    while (_inputQueue.Count > 0 && read < count)
                    {
                        ushort word = _inputQueue.Dequeue();

                        // Is this a mark or a data byte?
                        if (word < 0x100)
                        {
                            // Data, place in data stream
                            data[read + offset] = (byte)word;
                            read++;
                        }
                        else
                        {
                            // Mark.  Set last mark and exit.
                            _lastMark = (byte)(word >> 8);
                            done = true;
                            break;
                        }
                    }

                    if (read >= count)
                    {
                        done = true;
                    }

                    _inputLock.ExitWriteLock();
                    _inputLock.ExitUpgradeableReadLock();
                }
                else
                {
                    _inputLock.ExitUpgradeableReadLock();

                    // No data in the queue.
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

        /// <summary>
        /// Reads a single byte from the channel.  Will block if no data is available.
        /// </summary>
        /// <returns></returns>
        public byte ReadByte()
        {
            // TODO: optimize this
            byte[] data = new byte[1];

            Read(ref data, 1);

            return data[0];
        }

        /// <summary>
        /// Reads a single 16-bit word from the channel.  Will block if no data is available.
        /// </summary>
        /// <returns></returns>
        public ushort ReadUShort()
        {
            // TODO: optimize this
            byte[] data = new byte[2];

            Read(ref data, 2);

            return Helpers.ReadUShort(data, 0);
        }

        /// <summary>
        /// Reads a single BCPL string from the channel.  Will block as necessary.
        /// </summary>
        /// <returns></returns>
        public BCPLString ReadBCPLString()
        {
            return new BCPLString(this);
        }

        /// <summary>
        /// Reads data from the queue until a Mark byte is received.
        /// The mark byte is returned (LastMark is also set.)  Any data
        /// between the current position and the Mark read is discarded.
        /// 
        /// This will block until the next Mark is found.
        /// </summary>
        /// <returns></returns>
        public byte WaitForMark()
        {
            byte mark = 0;

            // This data is discarded.  The length is arbitrary.
            byte[] dummyData = new byte[512];

            while (true)
            {
                int read = Read(ref dummyData, dummyData.Length);

                // Short read, indicating a Mark.
                if (read < dummyData.Length)
                {
                    mark = _lastMark;
                    break;
                }
            }

            return mark;
        }

        /// <summary>
        /// Appends incoming client data or Marks into the input queue (called from BSPManager to place new PUP data into the BSP stream.)
        /// </summary>        
        public void RecvWriteQueue(PUP dataPUP)
        {
            //
            // Sanity check:  If this is a Mark PUP, the contents must only be one byte in length.
            //
            bool markPup = dataPUP.Type == PupType.AMark || dataPUP.Type == PupType.Mark;
            if (markPup)
            {
                if (dataPUP.Contents.Length != 1)
                {
                    Log.Write(LogType.Error, LogComponent.BSP, "Mark PUP must be 1 byte in length.");

                    SendAbort("Mark PUP must be 1 byte in length.");
                    BSPManager.DestroyChannel(this);
                    return;
                }
            }

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
            if (dataPUP.ID != _recvPos)
            {
                // Current behavior is to simply drop all incoming PUPs (and not ACK them) until they are re-sent to us
                // (in which case the above sanity check will pass).  According to spec, AData requests that are not ACKed
                // must eventually be resent.  This is far simpler than accepting out-of-order data and keeping track
                // of where it goes in the queue, though less efficient.
                _inputLock.ExitUpgradeableReadLock();
                Log.Write(LogType.Error, LogComponent.BSP, "Lost Packet, client ID does not match our receive position ({0} != {1})", dataPUP.ID, _recvPos);
                return;
            }


            // Prepare to add data to the queue
            _inputLock.EnterWriteLock();

            if (markPup)
            {
                //
                // For mark pups, the data goes in the high byte of the word
                // so that it can be identified as a mark when it's read back.
                _inputQueue.Enqueue((ushort)(dataPUP.Contents[0] << 8));
            }
            else
            {
                // Again, this is really inefficient
                for (int i = 0; i < dataPUP.Contents.Length; i++)
                {
                    _inputQueue.Enqueue(dataPUP.Contents[i]);
                }
            }

            _recvPos += (UInt32)dataPUP.Contents.Length;

            _inputLock.ExitWriteLock();

            _inputLock.ExitUpgradeableReadLock();

            _inputWriteEvent.Set();

            // If the client wants an ACK, send it now.
            if (dataPUP.Type == PupType.AData || dataPUP.Type == PupType.AMark)
            {
                SendAck();
            }
        }

        /// <summary>
        /// Sends data, with immediate flush to the network.
        /// </summary>
        /// <param name="data"></param>
        public void Send(byte[] data)
        {
            Send(data, data.Length, true /* flush */);
        }

        /// <summary>
        /// Sends data, optionally flushing.
        /// </summary>
        /// <param name="data"></param>
        public void Send(byte[] data, bool flush)
        {
            Send(data, data.Length, flush);
        }

        /// <summary>
        /// Sends data to the channel (i.e. to the client).  Will block (waiting for an ACK) if an ACK is requested.
        /// </summary>
        /// <param name="data">The data to be sent</param>
        /// <param name="flush">Whether to flush data out immediately or to wait for enough for a full PUP first.</param>
        public void Send(byte[] data, int length, bool flush)
        {
            if (length > data.Length)
            {
                throw new InvalidOperationException("Length must be less than or equal to the size of data.");
            }

            // Add output data to output queue.
            // Again, this is really inefficient
            for (int i = 0; i < length; i++)
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

                    // Send the data.
                    PUP dataPup = new PUP(PupType.Data, _sendPos, _clientConnectionPort, _serverConnectionPort, chunk);
                    SendDataPup(dataPup);
                }
            }
        }

        /// <summary>
        /// Sends an Abort PUP to the client, (generally indicating a catastrophic failure of some sort.)
        /// </summary>
        /// <param name="message"></param>
        public void SendAbort(string message)
        {
            PUP abortPup = new PUP(PupType.Abort, _startPos, _clientConnectionPort, _serverConnectionPort, Helpers.StringToArray(message));

            //
            // Send this directly, do not wait for the client to be ready (since it may be wedged, and we don't expect anyone to actually notice
            // this anyway).
            //
            PUPProtocolDispatcher.Instance.SendPup(abortPup);
        }

        /// <summary>
        /// Sends a Mark (or AMark) to the client.
        /// </summary>
        /// <param name="markType"></param>
        /// <param name="ack"></param>
        public void SendMark(byte markType, bool ack)
        {
            PUP markPup = new PUP(ack ? PupType.AMark : PupType.Mark, _sendPos, _clientConnectionPort, _serverConnectionPort, new byte[] { markType });

            // Send it.         
            SendDataPup(markPup);
        }

        /// <summary>
        /// Invoked when the client sends an ACK.
        /// Update our record of the client's PUP buffers.
        /// </summary>
        /// <param name="ackPUP"></param>
        public void RecvAck(PUP ackPUP)
        {
            //_outputWindowLock.EnterWriteLock();
            _clientLimits = (BSPAck)Serializer.Deserialize(ackPUP.Contents, typeof(BSPAck));

            Log.Write(LogType.Verbose, LogComponent.BSP,
                "ACK from client: bytes sent {0}, max bytes {1}, max pups {2}",
                _clientLimits.BytesSent,
                _clientLimits.MaxBytes,
                _clientLimits.MaxPups);
            
            _lastClientRecvPos = ackPUP.ID;            

            //
            // Unblock those waiting for an ACK.
            //
            _outputAckEvent.Set();            
        }              

        /// <summary>
        /// Sends an ACK to the client.
        /// </summary>
        private void SendAck()
        {
            _inputLock.EnterReadLock();
            BSPAck ack = new BSPAck();
            ack.MaxBytes = MaxBytes;
            ack.MaxPups = MaxPups;
            ack.BytesSent = MaxBytes;
            _inputLock.ExitReadLock();

            PUP ackPup = new PUP(PupType.Ack, _recvPos, _clientConnectionPort, _serverConnectionPort, Serializer.Serialize(ack));

            PUPProtocolDispatcher.Instance.SendPup(ackPup);
        }

        /// <summary>
        /// Sends a PUP.  Will block if client is unable to receive data.  If timeouts expire, channel will be shut down.
        /// </summary>
        /// <param name="p"></param>
        private void SendDataPup(PUP p)
        {
            //
            // Sanity check:  This should only be called for Data or Mark pups.
            //
            if (p.Type != PupType.AData && p.Type != PupType.Data && p.Type != PupType.Mark && p.Type != PupType.AMark)
            {
                throw new InvalidOperationException("Invalid PUP type for SendDataPup.");
            }

            //
            // Add the pup to the output window.  This may block if the window is full.
            //
            AddPupToOutputWindow(p);                          
        }

        /// <summary>
        /// Pings the client with an empty AData PUP, which will cause it to respond with an ACK containing client BSP information.
        /// </summary>        
        private void RequestClientStats()
        {
            //
            // Send an empty AData PUP to keep the connection alive and to update the client data stats.
            //
            PUP aData = new PUP(PupType.AData, _sendPos, _clientConnectionPort, _serverConnectionPort, new byte[0]);
            PUPProtocolDispatcher.Instance.SendPup(aData);
        }

        /// <summary>
        /// Adds the specified data/mark PUP to the moving output window, these PUPs will be picked up by the Output Thread
        /// and sent when the client is ready for them.
        /// </summary>
        /// <param name="p"></param>
        private void AddPupToOutputWindow(PUP p)
        {
            // Ensure things are set up
            EstablishWindow();            

            _outputWindowLock.EnterUpgradeableReadLock();

            if (_outputWindow.Count < _clientLimits.MaxPups)
            {
                //
                // There's space in the window, so go for it.
                //
                _outputWindowLock.EnterWriteLock();
                _outputWindow.Add(p);
                _outputWindowLock.ExitWriteLock();                
            }
            else
            {
                //
                // No space right now -- wait until the consumer has made some space.
                //                
                // Leave the lock so the consumer is unblocked
                _outputWindowLock.ExitUpgradeableReadLock();

                _outputReadyEvent.WaitOne();

                // Re-enter.
                _outputWindowLock.EnterUpgradeableReadLock();

                _outputWindowLock.EnterWriteLock();
                _outputWindow.Add(p);
                _outputWindowLock.ExitWriteLock();                
            }
                                               
            //
            // Tell the Consumer thread we've added a new PUP to be consumed.
            //
            _dataReadyEvent.Set();

            _outputWindowLock.ExitUpgradeableReadLock();            
        }        

        /// <summary>
        /// OutputConsumerThread consumes data placed into the output window (by sending it to the PUP dispatcher).        
        /// 
        /// It is responsible for getting positive handoff (via ACKS) from the client and resending PUPs the client
        /// missed.  While the output window is full, writers to the channel will be blocked.
        /// </summary>
        private void OutputConsumerThread()
        {
            while (true)
            {
                //
                // Wait for data.
                //
                _dataReadyEvent.WaitOne();

                _outputWindowLock.EnterUpgradeableReadLock();

                // Keep consuming until we've caught up with production
                while (_outputWindowIndex < _outputWindow.Count)
                {
                    //
                    // Pull the next PUP off the output queue and send it.
                    //
                    PUP nextPup = _outputWindow[_outputWindowIndex++];                    

                    //
                    // If we've sent as many PUPs to the client as it says it can take,
                    // we need to change the PUP to an AData PUP so we can acknowledge
                    // acceptance of the entire window we've sent.
                    //
                    bool bAck = false;
                    if (_outputWindowIndex >= _clientLimits.MaxPups)
                    {
                        Log.Write(LogType.Verbose, LogComponent.BSP, "Window full (size {0}), requiring ACK of data.", _clientLimits.MaxPups);
                        bAck = true;                        
                    }

                    //
                    // We need to build a PUP with the proper ID based on the current send position.
                    // TODO: rewrite the underlying PUP code so we don't have to completely recreate the PUPs like this, it makes me hurt.
                    //
                    if (nextPup.Type == PupType.Data || nextPup.Type == PupType.AData)
                    {                        
                        nextPup = new PUP(bAck ? PupType.AData : PupType.Data, _sendPos, nextPup.DestinationPort, nextPup.SourcePort, nextPup.Contents);
                    }
                    else if (nextPup.Type == PupType.Mark || nextPup.Type == PupType.AMark)
                    {
                        nextPup = new PUP(bAck ? PupType.AMark : PupType.Mark, _sendPos, nextPup.DestinationPort, nextPup.SourcePort, nextPup.Contents);
                    }                    

                    //
                    // Send it!
                    //
                    _sendPos += (uint)nextPup.Contents.Length;
                    PUPProtocolDispatcher.Instance.SendPup(nextPup);                    

                    //
                    // If we required an ACK, wait for it to arrive so we can confirm client reception of data.
                    //
                    if (nextPup.Type == PupType.AData || nextPup.Type == PupType.AMark)
                    {
                        // Wait for the client to be able to receive at least one PUP.
                        while (true)
                        {
                            WaitForAck();

                            if (_clientLimits.MaxPups > 0)
                            {
                                break;
                            }
                            
                            // Nope.  Request another ACK.
                        }
                        
                        //
                        // Check that the ACK's position matches ours, if it does not this indicates that the client lost at least one PUP,
                        // so we will need to resend it.
                        if (_lastClientRecvPos != _sendPos)
                        {
                            Log.Write(LogType.Warning, LogComponent.BSP,
                                "Client position != server position for BSP {0} ({1} != {2})",
                                _serverConnectionPort.Socket,
                                _lastClientRecvPos,
                                _sendPos);

                            //
                            // Move our window index back to the first PUP we missed and start resending from that position.
                            //
                            _outputWindowIndex = 0;
                            while(_outputWindowIndex < _outputWindow.Count)
                            {                                
                                if (_outputWindow[_outputWindowIndex].ID == _lastClientRecvPos)
                                {
                                    _sendPos = _outputWindow[_outputWindowIndex].ID;
                                    break;
                                }
                                _outputWindowIndex++;
                            }

                            if (_outputWindowIndex == _outputWindow.Count)
                            {
                                // Something bad has happened and we don't have that PUP anymore...
                                Log.Write(LogType.Error, LogComponent.BSP, "Client lost more than a window of data, BSP connection is broken.  Aborting.");
                                SendAbort("Fatal BSP synchronization error.");
                                BSPManager.DestroyChannel(this);
                                _outputWindowLock.ExitUpgradeableReadLock();
                                return;
                            }

                        }
                        else
                        {
                            //
                            // Everything was received OK by the client, remove the PUPs we sent from the output window and let writers continue.
                            //                            
                            _outputWindowLock.EnterWriteLock();
                            _outputWindow.RemoveRange(0, _outputWindowIndex);
                            _outputWindowIndex = 0;                            
                            _outputWindowLock.ExitWriteLock();
                            _outputReadyEvent.Set();          
                            
                            // Note: we don't break from the loop here; there may still be PUPs left in _outputWindow that need to be sent.                  
                        }
                    }
                }

                _outputWindowLock.ExitUpgradeableReadLock();
            }

        }


        /// <summary>
        /// Used when first actual data is sent over BSP, establishes initial parameters.
        /// </summary>
        private void EstablishWindow()
        {            
            _outputWindowLock.EnterReadLock();
            int maxPups = _clientLimits.MaxPups;
            _outputWindowLock.ExitReadLock();

            if (maxPups == 0xffff)
            {                
                //                
                // Wait for the client to be ready and tell us how many PUPs it can handle to start with.
                //
                RequestClientStats();
                WaitForAck();

                if (_clientLimits.MaxPups == 0)
                {
                    throw new InvalidOperationException("Client reports MaxPups of 0, this is invalid at start of BSP.");
                }   
            }            
        }

        /// <summary>
        /// Waits for an ACK from the client, "pinging" the client periodically.  Will retry a number of times, if no
        /// ACK is received the channel is shut down.
        /// </summary>
        private void WaitForAck()
        {
            //
            // Wait for the client to ACK.
            //
            int retry = 0;                  
            for (retry = 0; retry < BSPRetryCount; retry++)
            {
                if (_outputAckEvent.WaitOne(BSPAckTimeoutPeriod))
                {
                    // Done.                  
                    break;
                }
                else
                {
                    // No response within timeout, ask for an update.
                    RequestClientStats();
                }
            }

            if (retry >= BSPRetryCount)
            {
                Log.Write(LogType.Error, LogComponent.BSP, "Timeout waiting for ACK, aborting connection.");
                SendAbort("Client unresponsive.");
                BSPManager.DestroyChannel(this);
            }
        }    

        private BSPProtocol _protocolHandler;

        // The byte positions for the input and output streams
        private UInt32 _recvPos;
        private UInt32 _sendPos;
        private UInt32 _startPos;

        private PUPPort _clientConnectionPort;      // the client port
        private PUPPort _serverConnectionPort;      // the server port we (the server) have established for communication

        private BSPAck _clientLimits;               // The stats from the last ACK we got from the client.        
        private uint _lastClientRecvPos;            // The client's receive position, as indicated by the last ACK pup received.

        private ReaderWriterLockSlim _inputLock;
        private AutoResetEvent _inputWriteEvent;

        private ReaderWriterLockSlim _outputLock;
        
        private AutoResetEvent _outputAckEvent;              

        // NOTE: The input queue consists of ushorts so that
        // we can encapsulate Mark bytes without using a separate data structure.
        private Queue<ushort> _inputQueue;
        private Queue<byte> _outputQueue;

        // The output window (one entry per PUP the client says it's able to handle).
        private List<PUP> _outputWindow;
        private int _outputWindowIndex;
        private ReaderWriterLockSlim _outputWindowLock;
        private AutoResetEvent _outputReadyEvent;
        private AutoResetEvent _dataReadyEvent;

        private Thread _consumerThread;

        private byte _lastMark;

        // Constants

        // For now, we work on one PUP at a time.
        private const int MaxPups = 1;
        private const int MaxPupSize = 532;
        private const int MaxBytes = 1 * 532;

        // Timeouts and retries
        private const int BSPRetryCount = 5;
        private const int BSPAckTimeoutPeriod = 1000;       // 1 second
        private const int BSPReadTimeoutPeriod = 60000;     // 1 minute
    }    
}
