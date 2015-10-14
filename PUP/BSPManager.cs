using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IFS
{


    public enum BSPState
    {
        Unconnected,
        Connected
    }

    public class BSPChannel
    {
        public BSPChannel(PUP rfcPup)
        {
            _inputLock = new ReaderWriterLockSlim();
            _outputLock = new ReaderWriterLockSlim();

            _inputWriteEvent = new AutoResetEvent(false);

            _inputQueue = new MemoryStream(65536);

            // TODO: init IDs, etc. based on RFC PUP
            _start_pos = _recv_pos = _send_pos = rfcPup.ID;

        }

        public PUPPort ClientPort
        {
            get { return _clientConnectionPort; }
        }

        public PUPPort ServerPort
        {
            get { return _serverConnectionPort; }
        }

        /// <summary>
        /// Reads data from the channel (i.e. from the client).  Will block if not all the requested data is available.
        /// </summary>
        /// <returns></returns>
        public int Read(ref byte[] data, int count)
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
                        data[i] = _inputQueue.Dequeue();
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

        /// <summary>
        /// Appends data into the input queue (called from BSPManager to place new PUP data into the BSP stream)
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
                // of where it goes in the queue.
                return;
            }
                  

            // Prepare to add data to the queue
            // Again, this is really inefficient
            _inputLock.EnterWriteLock();

            for (int i = 0; i < dataPUP.Contents.Length; i++)
            {
                _inputQueue.Enqueue(dataPUP.Contents[i]);
            }

            _recv_pos += (UInt32)dataPUP.Contents.Length;

            _inputLock.ExitWriteLock();

            _inputLock.ExitUpgradeableReadLock();

            _inputWriteEvent.Set();

            if ((PupType)dataPUP.Type == PupType.AData)
            {
                SendAck();
            }

        }

        /// <summary>
        /// Sends data to the channel (i.e. to the client).  Will block if an ACK is requested.
        /// </summary>
        /// <param name="data">The data to be sent</param>
        public void Send(byte[] data)
        {
            // Write data to the output stream

            // Await an ack for the PUP we just sent            
            _outputAckEvent.WaitOne(); // TODO: timeout and fail

        }

        public void Ack(PUP ackPUP)
        {
            // Update receiving end stats (max PUPs, etc.)
            // Ensure client's position matches ours

            // Let any waiting threads continue
            _outputAckEvent.Set();
        }

        public void Mark(byte type);
        public void Interrupt();

        public void Abort(int code, string message);
        public void Error(int code, string message);

        public void End();

        // TODO:
        // Events for:
        //   Abort, End, Mark, Interrupt (from client)
        //   Repositioning (due to lost packets) (perhaps not necessary)
        //     

        private void SendAck()
        {

        }


        private BSPState _state;
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

        }


        /// <summary>
        /// Called when BSP-based protocols receive data.
        /// </summary>
        /// <returns>
        /// null if no new channel is created due to the sent PUP (not an RFC)
        /// a new BSPChannel if one has been created based on the PUP (new RFC)
        /// </returns>
        /// <param name="p"></param>
        public static BSPChannel RecvData(PUP p)
        {
            PupType type = (PupType)p.Type;

            switch (type)
            {
                case PupType.RFC:
                    {
                        BSPChannel newChannel = new BSPChannel(p);
                        _activeChannels.Add(newChannel.ClientPort.Socket);

                        return newChannel;
                    }

                case PupType.Data:
                case PupType.AData:
                    {
                        BSPChannel channel = FindChannelForPup(p);

                        if (channel != null)
                        {                            
                            channel.WriteQueue(p);
                        }
                    }
                    break;

                case PupType.Ack:
                    BSPChannel channel = FindChannelForPup(p);

                    if (channel != null)
                    {                        
                        channel.Ack(p);
                    }
                    break;

                case PupType.End:
                    {
                        BSPChannel channel = FindChannelForPup(p);

                        if (channel != null)
                        {
                            channel.EndReply();
                        }
                    }
                    break;

                case PupType.Abort:
                    {
                        // TODO: tear down the channel
                    }
                    break;

                default:
                    throw new NotImplementedException(String.Format("Unhandled BSP PUP type {0}.", type));

            }

            return null;             
        }

        public static void DestroyChannel(BSPChannel channel)
        {
            
        }

        private static BSPChannel FindChannelForPup(PUP p)
        {
            return null;
        }

        private static Dictionary<UInt32, BSPChannel> _activeChannels;
    }
}
