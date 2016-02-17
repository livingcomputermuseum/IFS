using IFS.BSP;
using IFS.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.IO;

namespace IFS.EFTP
{
    /// <summary>
    /// EFTP: It's like a really limited version of BSP.
    /// This is not a standalone server like FTP but provides routines for sending / receiving data
    /// via EFTP, so that actual servers (Boot, Printing, etc.) can serve their clients.
    /// </summary>
    public static class EFTPManager
    {
        static EFTPManager()
        {
            //
            // Initialize the socket ID counter; we start with a
            // number beyond the range of well-defined sockets.
            // For each new EFTP channel that gets opened, we will
            // increment this counter to ensure that each channel gets
            // a unique ID.  (Well, until we wrap around...)
            //
            _nextSocketID = _startingSocketID;

            _activeChannels = new Dictionary<uint, EFTPChannel>();

        }
        public static void SendFile(PUPPort destination, Stream data)
        {
            UInt32 socketID = GetNextSocketID();
            EFTPChannel newChannel = new EFTPChannel(destination, socketID);
            _activeChannels.Add(socketID, newChannel);

            EFTPSend.StartSend(newChannel, data);
        }

        public static void RecvData(PUP p)
        {
            EFTPChannel channel = FindChannelForPup(p);

            if (channel == null)
            {
                Log.Write(LogType.Error, LogComponent.EFTP, "Received EFTP PUP on an unconnected socket, ignoring.");
                return;
            }

            switch (p.Type)
            {
              
                case PupType.EFTPData:                
                    {
                        channel.RecvData(p);
                    }
                    break;

                case PupType.EFTPAck:
                    {
                        channel.RecvAck(p);
                    }
                    break;

                case PupType.EFTPEnd:
                    {                        
                        channel.End(p);
                    }
                    break;
                
                case PupType.EFTPAbort:
                    {
                        string abortMessage = Helpers.ArrayToString(p.Contents);
                        Log.Write(LogType.Warning, LogComponent.RTP, String.Format("EFTP aborted, message: '{0}'", abortMessage));

                        DestroyChannel(channel);
                    }
                    break;

                default:
                    throw new NotImplementedException(String.Format("Unhandled EFTP PUP type {0}.", p.Type));

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
        public static void DestroyChannel(EFTPChannel channel)
        {
            channel.Destroy();

            _activeChannels.Remove(channel.ServerPort.Socket);
        }

        /// <summary>
        /// Finds the appropriate channel for the given PUP.
        /// </summary>
        /// <param name="p"></param>
        /// <returns></returns>
        private static EFTPChannel FindChannelForPup(PUP p)
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
        /// Generates a unique Socket ID.  We count downward from the max value
        /// (whereas BSP counts upwards) so as to avoid any duplicates.
        /// TODO: this could be done much more sanely via a centralized ID factory...
        /// </summary>
        /// <returns></returns>
        private static UInt32 GetNextSocketID()
        {
            UInt32 next = _nextSocketID;

            _nextSocketID--;

            //
            // Handle the wrap around case (which we're very unlikely to
            // ever hit, but why not do the right thing).
            // Start over at the initial ID.  This is very unlikely to
            // collide with any pending channels.
            //
            if (_nextSocketID < 0x1000)
            {
                _nextSocketID = _startingSocketID;
            }

            return next;
        }


        /// <summary>
        /// Map from socket address to BSP channel
        /// </summary>
        private static Dictionary<UInt32, EFTPChannel> _activeChannels;

        private static UInt32 _nextSocketID;
        private static readonly UInt32 _startingSocketID = UInt32.MaxValue;
    }

    
}
