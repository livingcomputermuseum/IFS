using IFS.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IFS.BSP
{

    public struct BSPAck
    {
        public ushort MaxBytes;
        public ushort MaxPups;
        public ushort BytesSent;
    }    

    public delegate void WorkerExitDelegate(BSPWorkerBase destroyed);

    public abstract class BSPWorkerBase
    {
        public BSPWorkerBase(BSPChannel channel)
        {
            _channel = channel;
        }

        public abstract void Terminate();
                    
        public WorkerExitDelegate OnExit;

        protected BSPChannel _channel;
    }

    /// <summary>
    /// Manages active BSP channels and creates new ones as necessary, invoking the associated
    /// protocol handlers.
    /// Dispatches PUPs to the appropriate BSP channel.
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

            _workers = new List<BSPWorkerBase>(Configuration.MaxWorkers);
        }

        /// <summary>
        /// Called when a PUP comes in on a known socket.  Establishes a new BSP channel.
        /// A worker of the appropriate type is woken up to service the channel.
        /// </summary>
        /// <param name="p"></param>
        public static void EstablishRendezvous(PUP p, Type workerType)
        {
            if (p.Type != PupType.RFC)
            {
                Log.Write(LogType.Error, LogComponent.RTP, "Expected RFC pup, got {0}", p.Type);
                return;
            }
            
            UInt32 socketID = GetNextSocketID();
            BSPChannel newChannel = new BSPChannel(p, socketID);
            newChannel.OnDestroy += OnChannelDestroyed;
            _activeChannels.Add(socketID, newChannel); 

            //
            // Initialize the worker for this channel.
            InitializeWorkerForChannel(newChannel, workerType);

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

            Log.Write(LogType.Verbose, LogComponent.BSP, "BSP pup is {0}", p.Type);

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
                
                case PupType.Mark:
                case PupType.AMark:
                    {
                        channel.RecvWriteQueue(p);
                    }
                    break;

                case PupType.Abort:
                    {                                                
                        string abortMessage = Helpers.ArrayToString(p.Contents);
                        Log.Write(LogType.Warning, LogComponent.RTP, String.Format("BSP aborted, message: '{0}'", abortMessage));

                        DestroyChannel(channel);
                    }
                    break;

                case PupType.Error:
                    {
                        channel.RecvError(p);
                    }
                    break;

                case PupType.Interrupt:
                    {
                        channel.RecvInterrupt(p);
                    }
                    break;

                default:
                    throw new NotImplementedException(String.Format("Unhandled BSP PUP type {0}.", p.Type));

            }                    
        }

        /// <summary>
        /// Indicates whether a channel exists for the socket specified by the given PUP.
        /// </summary>
        /// <param name="p"></param>
        /// <returns></returns>
        public static bool ChannelExistsForSocket(PUP p)
        {
            return FindChannelForPup(p) != null;
        }

        /// <summary>
        /// Destroys and unregisters the specified channel.
        /// </summary>
        /// <param name="channel"></param>
        public static void DestroyChannel(BSPChannel channel)
        {
            // Tell the channel to shut down.  It will in turn
            // notify us when that is complete and we will remove
            // our references to it.  (See OnChannelDestroyed.)
            channel.Destroy();
        }

        public static void OnChannelDestroyed(BSPChannel channel)
        {
            _activeChannels.Remove(channel.ServerPort.Socket);
        }

        /// <summary>
        /// Finds the appropriate channel for the given PUP.
        /// </summary>
        /// <param name="p"></param>
        /// <returns></returns>
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

        /// <summary>
        /// Generates a unique Socket ID.
        /// </summary>
        /// <returns></returns>
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

        private static void InitializeWorkerForChannel(BSPChannel channel, Type workerType)
        {
            if (_workers.Count < Configuration.MaxWorkers)
            {
                // Spawn new worker, which starts it running.
                // It must be a subclass of BSPWorkerBase or this will throw.
                BSPWorkerBase worker = (BSPWorkerBase)Activator.CreateInstance(workerType, new object[] { channel });
                
                worker.OnExit += OnWorkerExit;
                _workers.Add(worker);
            }
            else
            {
                // TODO: send back "server full" repsonse of some sort.
            }
        }        

        public static int WorkerCount
        {
            get
            {
                return _workers.Count();
            }
        }

        private static void OnWorkerExit(BSPWorkerBase destroyed)
        {            
            if (_workers.Contains(destroyed))
            {
                _workers.Remove(destroyed);
            }
        }

        private static List<BSPWorkerBase> _workers;

        /// <summary>
        /// Map from socket address to BSP channel
        /// </summary>
        private static Dictionary<UInt32, BSPChannel> _activeChannels;

        private static UInt32 _nextSocketID;
        private static readonly UInt32 _startingSocketID = 0x1000;
    }
}
