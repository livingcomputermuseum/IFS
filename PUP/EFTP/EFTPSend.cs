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
        }

        private void OnChannelDestroyed()
        {
            _workerThread.Abort();
            _data.Close();
        }

        private Thread _workerThread;

        private EFTPChannel _channel;
        private Stream _data;
    }
}
