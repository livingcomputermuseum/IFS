using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

using IFS.Logging;

namespace IFS.FTP
{
    public enum FTPCommand
    {
        Invalid = 0,
        Retrieve = 1,
        Store = 2,
        NewStore = 9,
        Yes = 3,
        No = 4,
        HereIsPropertyList = 11,
        HereIsFile = 5,
        Version = 8,
        Comment = 7,
        EndOfCommand = 6,
        Enumerate = 10,
        NewEnumerate = 12,
        Delete = 14,
        Rename = 15,
    }

    struct FTPVersion
    {
        public FTPVersion(byte version, string herald)
        {
            Version = version;
            Herald = herald;
        }

        public byte Version;
        public string Herald;
    }

    public class FTPServer : BSPProtocol
    {
        /// <summary>
        /// Called by dispatcher to send incoming data destined for this protocol.
        /// </summary>
        /// <param name="p"></param>
        public override void RecvData(PUP p)
        {
            throw new NotImplementedException();
        }

        public override void InitializeServerForChannel(BSPChannel channel)
        {
            // Spawn new worker
            FTPWorker ftpWorker = new FTPWorker(channel);
        }
    }

    public class FTPWorker
    {
        public FTPWorker(BSPChannel channel)
        {
            // Register for channel events
            channel.OnDestroy += OnChannelDestroyed;

            _running = true;

            _workerThread = new Thread(new ParameterizedThreadStart(FTPWorkerThreadInit));
            _workerThread.Start(channel);
        }

        private void OnChannelDestroyed()
        {
            // Tell the thread to exit and give it a short period to do so...
            _running = false;

            Log.Write(LogType.Verbose, LogComponent.FTP, "Asking FTP worker thread to exit...");
            _workerThread.Join(1000);

            if (_workerThread.IsAlive)
            {
                Logging.Log.Write(LogType.Verbose, LogComponent.FTP, "FTP worker thread did not exit, terminating.");
                _workerThread.Abort();
            }
        }

        private void FTPWorkerThreadInit(object obj)
        {
            _channel = (BSPChannel)obj;
            //
            // Run the worker thread.
            // If anything goes wrong, log the exception and tear down the BSP connection.
            //
            try
            {
                FTPWorkerThread();
            }
            catch (Exception e)
            {
                if (!(e is ThreadAbortException))
                {
                    Log.Write(LogType.Error, LogComponent.FTP, "FTP worker thread terminated with exception '{0}'.", e.Message);
                    _channel.SendAbort("Server encountered an error.");
                }
            }
        }

        private void FTPWorkerThread()
        {
            // Buffer used to receive command data.
            byte[] buffer = new byte[1024];

            while (_running)
            {
                // Discard input until we get a Mark.  We should (in general) get a
                // command, followed by EndOfCommand.  
                FTPCommand command = (FTPCommand)_channel.WaitForMark();

                // Read data until the next Mark, which should be "EndOfCommand"
                int length = _channel.Read(ref buffer, buffer.Length);

                //
                // Sanity check:  FTP spec doesn't specify max length of a command so the current
                // length is merely a guess.  If we actually filled the buffer then we should note it
                // so this can be corrected.
                //
                if (length == buffer.Length)
                {
                    throw new InvalidOperationException("Expected short read for FTP command.");
                }

                //
                // Ensure that next Mark is "EndOfCommand"
                //
                if (_channel.LastMark != (byte)FTPCommand.EndOfCommand)
                {
                    throw new InvalidOperationException(String.Format("Expected EndOfCommand, got {0}", (FTPCommand)_channel.LastMark));
                }

                //
                // TODO: this is ugly, figure out a clean way to do this.  We need to deal with only the
                // actual data retrieved.  Due to the clumsy way we read it in we need to copy it here.
                //
                byte[] data = new byte[length];
                Array.Copy(buffer, data, length);

                //
                // At this point we should have the entire command, execute it.
                //
                switch(command)
                {
                    case FTPCommand.Version:
                        {
                            FTPVersion version = (FTPVersion)Serializer.Deserialize(data, typeof(FTPVersion));
                            Log.Write(LogType.Normal, LogComponent.FTP, "Client FTP version is {0}, herald is '{1}.", version.Version, version.Herald);

                            //
                            // Return our Version.
                            FTPVersion serverVersion = new FTPVersion(1, "LCM IFS FTP of 4 Feb 2016.");
                            SendFTPResponse(FTPCommand.Version, serverVersion);                            
                        }
                        break;

                    case FTPCommand.Enumerate:
                        {
                            // Argument to Enumerate is a property list (string).
                            //
                            string fileSpec = Helpers.ArrayToString(data);
                            Log.Write(LogType.Verbose, LogComponent.FTP, "File spec for enumeration is '{0}.", fileSpec);
                        }
                        break;

                    default:
                        Log.Write(LogType.Warning, LogComponent.FTP, "Unhandled FTP command {0}.", command);
                        break;
                }
            }                 
        }

        private void SendFTPResponse(FTPCommand responseCommand, object data)
        {
            _channel.SendMark((byte)FTPCommand.Version, false);
            _channel.Send(Serializer.Serialize(data));
            _channel.SendMark((byte)FTPCommand.EndOfCommand, true);
        }

        private BSPChannel _channel;

        private Thread _workerThread;
        private bool _running;
    }
}
