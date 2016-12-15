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

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading;

namespace IFS.EFTP
{
    public class EFTPSend
    {

        public static void StartSend(EFTPChannel channel, Stream data)
        {
            EFTPSend newSender = new EFTPSend(channel, data);

        }

        private EFTPSend(EFTPChannel channel, Stream data)
        {
            _data = data;
            _channel = channel;
            _sendDone = false;

            _workerThread = new Thread(SendWorker);
            _workerThread.Start();
            
            channel.OnDestroy += OnChannelDestroyed;
        }

        private void SendWorker()
        {
            byte[] block = new byte[512];
            while(true)
            {
                //
                // Send the file 512 bytes at a time to the channel, and then finish.
                //
                int read = _data.Read(block, 0, block.Length);
                _channel.Send(block, read, true);

                Log.Write(LogType.Verbose, LogComponent.EFTP, "Sent data, position is now {0}", _data.Position);

                if (read != block.Length)
                {
                    _channel.SendEnd();
                    break;
                }                
            }
            _data.Close();
            _sendDone = true;

            EFTPManager.DestroyChannel(_channel);
        }

        private void OnChannelDestroyed()
        {
            if (!_sendDone)
            {
                _workerThread.Abort();
                _data.Close();
            }            
        }

        private Thread _workerThread;

        private EFTPChannel _channel;
        private Stream _data;

        private bool _sendDone;
    }
}
