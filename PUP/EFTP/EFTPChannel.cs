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

using IFS.Gateway;
using IFS.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace IFS.EFTP
{
    /// <summary>
    /// Represents an open EFTP channel and provides methods for sending data to it.
    /// (Receiving is not yet implemented, the current implementation exists only to support Boot File requests.)
    /// </summary>
    public class EFTPChannel
    {
        public EFTPChannel(PUPPort destination, UInt32 socketID)
        {
            _clientConnectionPort = destination;

            _outputAckEvent = new AutoResetEvent(false);

            // We create our connection port using a unique socket address.
            _serverConnectionPort = new PUPPort(DirectoryServices.Instance.LocalHostAddress, socketID);

            _outputQueue = new Queue<byte>(65536);
            _sendPos = 0;
        }

        public PUPPort ServerPort
        {
            get { return _serverConnectionPort; }
        }

        public PUPPort ClientPort
        {
            get { return _clientConnectionPort; }
        }

        public delegate void DestroyDelegate();

        public DestroyDelegate OnDestroy;

        public void Destroy()
        {
            if (OnDestroy != null)
            {
                OnDestroy();
            }
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
            // Again, this is really inefficient.
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

                    while (true)
                    {
                        // Send the data.                        
                        PUP dataPup = new PUP(PupType.EFTPData, _sendPos, _clientConnectionPort, _serverConnectionPort, chunk);
                        
                        Router.Instance.SendPup(dataPup);

                        // Await an ACK.  We will retry several times and resend as necessary.
                        int retry = 0;
                        for (retry = 0; retry < EFTPRetryCount; retry++)
                        {
                            if (_outputAckEvent.WaitOne(EFTPAckTimeoutPeriod))
                            {
                                // done, we got our ACK.
                                break;
                            }
                            else
                            {
                                // timeout: resend the PUP and wait for an ACK again.
                                Router.Instance.SendPup(dataPup);
                            }
                        }

                        if (retry >= EFTPRetryCount)
                        {
                            Log.Write(LogType.Error, LogComponent.EFTP, "Timeout waiting for ACK, aborting connection.");
                            SendAbort("Client unresponsive.");
                            EFTPManager.DestroyChannel(this);
                        }

                        if (_lastRecvPos == _sendPos)
                        {
                            // The client is in sync with us, we are done with this packet.                                                        
                            break;
                        }
                        else if (_sendPos - _lastRecvPos > 1)
                        {
                            // We lost more than one packet, something is very broken.
                            Log.Write(LogType.Error, LogComponent.EFTP, "Client lost more than one packet, connection is broken.  Aborting.");
                            SendAbort("Client lost too much data.");
                            EFTPManager.DestroyChannel(this);
                        }
                        else
                        {
                            // We lost one packet, move back and send it again.     
                            Log.Write(LogType.Warning, LogComponent.EFTP, "Client lost a packet, resending.");                            
                        }

                    }

                    // Move to next packet.
                    _sendPos++;
                }
            }
        }

        public void SendEnd()
        {
            PUP endPup = new PUP(PupType.EFTPEnd, _sendPos, _clientConnectionPort, _serverConnectionPort, new byte[0]);
            Router.Instance.SendPup(endPup);            

            // Await an ack
            _outputAckEvent.WaitOne(EFTPAckTimeoutPeriod);

            _sendPos++;

            // Send another end to close things off.
            endPup = new PUP(PupType.EFTPEnd, _sendPos, _clientConnectionPort, _serverConnectionPort, new byte[0]);
            Router.Instance.SendPup(endPup);
        }

        public void RecvData(PUP p)
        {
            // For now, receive is not implemented.
            throw new NotImplementedException();
        }

        public void RecvAck(PUP p)
        {
            //
            // Sanity check that the client's position matches ours.
            //
            _lastRecvPos = p.ID;
            if (_lastRecvPos != _sendPos)
            {
                Log.Write(LogType.Error, LogComponent.EFTP, "Client position does not match server ({0} != {1}",
                    _lastRecvPos, _sendPos);
            }

            //
            // Unblock those waiting for an ACK.
            //
            _outputAckEvent.Set();
        }
            

        public void End(PUP p)
        {

        }

        private void SendAbort(string message)
        {
            /*
            PUP abortPup = new PUP(PupType.EFTPAbort, _sendPos, _clientConnectionPort, _serverConnectionPort, Helpers.StringToArray(message));

            //
            // Send this directly, do not wait for the client to be ready (since it may be wedged, and we don't expect anyone to actually notice
            // this anyway).
            //
            Router.Instance.SendPup(abortPup);
            */
        }

        private PUPPort _clientConnectionPort;
        private PUPPort _serverConnectionPort;

        private uint _sendPos;
        private uint _lastRecvPos;
        private Queue<byte> _outputQueue;
        private AutoResetEvent _outputAckEvent;
        

        // Timeouts and retries
        private const int EFTPRetryCount = 5;
        private const int EFTPAckTimeoutPeriod = 1000;       // 1 second        
    }
}
