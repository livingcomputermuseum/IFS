using IFS.BSP;
using IFS.Logging;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace IFS.CopyDisk
{
    public enum CopyDiskBlock
    {
        Version = 1,
        SendDiskParamsR = 2,
        HereAreDiskParams = 3,
        StoreDisk = 4,
        RetrieveDisk = 5,
        HereIsDiskPage = 6,
        EndOfTransfer = 7,
        SendErrors = 8,
        HereAreErrors = 9,
        No = 10,
        Yes = 11,
        Comment = 12,
        Login = 13,
        SendDiskParamsW = 14,
    }

    public enum NoCode
    {
        UnitNotReady = 1,
        UnitWriteProtected = 2,
        OverwriteNotAllowed = 3,
        UnknownCommand = 4,
        IllegalUserName = 16,
        IllegalOrIncorrectPassword = 17,
        IllegalConnectName = 18,
        IllegalConnectPassword = 19,
    }

    struct VersionYesNoBlock
    {
        public VersionYesNoBlock(CopyDiskBlock command, ushort code, string herald)
        {
            Code = code;
            Herald = new BCPLString(herald);

            Length = (ushort)((6 + herald.Length + 2) / 2);     // +2 for length of BCPL string and to round up to next word length
            Command = (ushort)command;
        }

        public ushort Length;
        public ushort Command;
        public ushort Code;
        public BCPLString Herald;        
    }

    struct LoginBlock
    {
        public ushort Length;
        public ushort Command;        
        public BCPLString UserName;

        [WordAligned]
        public BCPLString UserPassword;

        [WordAligned]
        public BCPLString ConnName;

        [WordAligned]
        public BCPLString ConnPassword;
    }

    struct SendDiskParamsBlock
    {
        public ushort Length;
        public ushort Command;        
        public BCPLString UnitName;        
    }    

    struct HereAreDiskParamsBFSBlock
    {
        public HereAreDiskParamsBFSBlock(DiskGeometry geometry)
        {
            Length = 6;
            Command = (ushort)CopyDiskBlock.HereAreDiskParams;

            DiskType = 10;      // 12(octal) - BFS disk types
            Cylinders = (ushort)geometry.Cylinders;
            Heads = (ushort)geometry.Tracks;
            Sectors = (ushort)geometry.Sectors;
        }

        public ushort Length;
        public ushort Command;
        public ushort DiskType;
        public ushort Cylinders;
        public ushort Heads;
        public ushort Sectors;
    }

    struct HereAreErrorsBFSBlock
    {
        public HereAreErrorsBFSBlock(ushort hardErrors, ushort softErrors)
        {
            Length = 4;
            Command = (ushort)CopyDiskBlock.HereAreErrors;
            HardErrorCount = hardErrors;
            SoftErrorCount = softErrors;
        }

        public ushort Length;
        public ushort Command;
        public ushort HardErrorCount;
        public ushort SoftErrorCount;
    }


    struct TransferParametersBlock
    {
        public ushort Length;
        public ushort Command;
        public ushort StartAddress;
        public ushort EndAddress;
    }

    struct HereIsDiskPageBlock
    {
        public HereIsDiskPageBlock(byte[] header, byte[] label, byte[] data)
        {
            if (header.Length != 4 ||
                label.Length != 16 ||
                data.Length != 512)
            {
                throw new InvalidOperationException("Page data is incorrectly sized.");
            }

            Length = (512 + 16 + 4 + 4) / 2;
            Command = (ushort)CopyDiskBlock.HereIsDiskPage;

            Header = header;
            Label = label;
            Data = data;
        }

        public ushort Length;
        public ushort Command;

        [ArrayLength(4)]
        public byte[] Header;

        [ArrayLength(16)]
        public byte[] Label;

        [ArrayLength(512)]
        public byte[] Data;
    }

    struct HereIsDiskPageIncorrigableBlock
    {
        public HereIsDiskPageIncorrigableBlock(byte[] header, byte[] label)
        {
            if (header.Length != 4 ||
                label.Length != 16)
            {
                throw new InvalidOperationException("Page data is incorrectly sized.");
            }

            Length = (16 + 4 + 4) / 2;
            Command = (ushort)CopyDiskBlock.HereIsDiskPage;

            Header = header;
            Label = label;            
        }

        public ushort Length;
        public ushort Command;

        [ArrayLength(4)]
        public byte[] Header;

        [ArrayLength(16)]
        public byte[] Label;        
    }

    struct EndOfTransferBlock
    {
        public EndOfTransferBlock(int dummy)        /* can't have parameterless constructor for struct */
        {
            Length = 2;
            Command = (ushort)CopyDiskBlock.EndOfTransfer;
        }

        public ushort Length;
        public ushort Command;
    }   

    public class CopyDiskWorker : BSPWorkerBase
    {
        public CopyDiskWorker(BSPChannel channel) : base(channel)
        {
            // Register for channel events
            channel.OnDestroy += OnChannelDestroyed;

            _running = true;

            _workerThread = new Thread(new ThreadStart(CopyDiskWorkerThreadInit));
            _workerThread.Start();            
        }

        public override void Terminate()
        {
            ShutdownWorker();
        }

        private void OnChannelDestroyed(BSPChannel channel)
        {
            ShutdownWorker();
        }

        private void CopyDiskWorkerThreadInit()
        {            
            //
            // Run the worker thread.
            // If anything goes wrong, log the exception and tear down the BSP connection.
            //
            try
            {
                CopyDiskWorkerThread();
            }
            catch(Exception e)
            {
                if (!(e is ThreadAbortException))
                {
                    Logging.Log.Write(LogType.Error, LogComponent.CopyDisk, "CopyDisk worker thread terminated with exception '{0}'.", e.Message);
                    _channel.SendAbort("Server encountered an error.");

                    OnExit(this);
                }
            }
        }

        private void CopyDiskWorkerThread()
        {            
            // TODO: enforce state (i.e. reject out-of-order block types.)
            while (_running)
            {
                // Retrieve length of this block (in bytes):
                int length = _channel.ReadUShort() * 2;

                // Sanity check that length is a reasonable value.                
                if (length > 2048)
                {
                    // TODO: shut down channel
                    throw new InvalidOperationException(String.Format("Insane block length ({0})", length));
                }

                // Retrieve type            
                CopyDiskBlock blockType = (CopyDiskBlock)_channel.ReadUShort();

                // Read rest of block starting at offset 4 (so deserialization works)              
                byte[] data = new byte[length];
                _channel.Read(ref data, data.Length - 4, 4);

                Log.Write(LogType.Verbose, LogComponent.CopyDisk, "Copydisk block type is {0}", blockType);

                switch(blockType)
                {
                    case CopyDiskBlock.Version:
                        {
                            VersionYesNoBlock vbIn = (VersionYesNoBlock)Serializer.Deserialize(data, typeof(VersionYesNoBlock));

                            Log.Write(LogType.Verbose, LogComponent.CopyDisk, "Copydisk client is version {0}, '{1}'", vbIn.Code, vbIn.Herald.ToString());

                            // Send the response:
                            VersionYesNoBlock vbOut = new VersionYesNoBlock(CopyDiskBlock.Version, vbIn.Code, "LCM IFS CopyDisk of 26-Jan-2016");
                            _channel.Send(Serializer.Serialize(vbOut));
                        }
                        break;

                    case CopyDiskBlock.Login:
                        {
                            LoginBlock login = (LoginBlock)Serializer.Deserialize(data, typeof(LoginBlock));

                            Log.Write(LogType.Verbose, LogComponent.CopyDisk, "Login is for user '{0}', password '{1}', connection '{2}', connection password '{3}'.",
                                login.UserName,
                                login.UserPassword,
                                login.ConnName,
                                login.ConnPassword);

                            _userToken = AuthenticateUser(login.UserName.ToString(), login.UserPassword.ToString());

                            if (_userToken != null)
                            {
                                //
                                // Send a "Yes" response back.
                                //
                                VersionYesNoBlock yes = new VersionYesNoBlock(CopyDiskBlock.Yes, 0, "Come on in, the water's fine.");
                                _channel.Send(Serializer.Serialize(yes));
                            }
                            else
                            {
                                //
                                // Send a "No" response back indicating the login failure.
                                //
                                VersionYesNoBlock no = new VersionYesNoBlock(CopyDiskBlock.No, (ushort)NoCode.IllegalOrIncorrectPassword, "Invalid username or password.");
                                _channel.Send(Serializer.Serialize(no), true);                                
                            }
                        }
                        break;

                    case CopyDiskBlock.SendDiskParamsR:
                        {
                            SendDiskParamsBlock p = (SendDiskParamsBlock)Serializer.Deserialize(data, typeof(SendDiskParamsBlock));

                            Log.Write(LogType.Verbose, LogComponent.CopyDisk, "Requested unit for reading is '{0}'", p.UnitName);

                            //
                            // See if the pack image exists, return HereAreDiskParams if so, or No if not.
                            // If the image exists, save the path for future use.
                            //
                            // Some sanity (and security) checks:
                            // Name must be a filename only, no paths of any kind allowed.
                            // Oh, and the file must exist in the directory holding disk packs.
                            //
                            string diskPath = GetPathForDiskImage(p.UnitName.ToString());
                            if (!String.IsNullOrEmpty(Path.GetDirectoryName(p.UnitName.ToString())) ||
                                !File.Exists(diskPath))
                            {
                                // Invalid name, return No reponse.
                                VersionYesNoBlock no = new VersionYesNoBlock(CopyDiskBlock.No, (ushort)NoCode.UnitNotReady, "Invalid unit name.");
                                _channel.Send(Serializer.Serialize(no));
                            }
                            else
                            {
                                //
                                // Attempt to open the image file and read it into memory.
                                //
                                try
                                {                                    
                                    using (FileStream packStream = new FileStream(diskPath, FileMode.Open, FileAccess.Read))
                                    {
                                        // TODO: determine pack type rather than assuming Diablo 31
                                        _pack = new DiabloPack(DiabloDiskType.Diablo31);
                                        _pack.Load(packStream, diskPath, true /* reverse byte order */);
                                    }

                                    // Send a "HereAreDiskParams" response indicating success.
                                    //
                                    HereAreDiskParamsBFSBlock diskParams = new HereAreDiskParamsBFSBlock(_pack.Geometry);
                                    _channel.Send(Serializer.Serialize(diskParams));
                                }
                                catch
                                {
                                    // If we fail for any reason, return a "No" response.
                                    // TODO: can we be more helpful here?
                                    VersionYesNoBlock no = new VersionYesNoBlock(CopyDiskBlock.No, (ushort)NoCode.UnitNotReady, "Image could not be opened.");
                                    _channel.Send(Serializer.Serialize(no));
                                }
                            }
                        }
                        break;

                    case CopyDiskBlock.SendDiskParamsW:
                        {
                            SendDiskParamsBlock p = (SendDiskParamsBlock)Serializer.Deserialize(data, typeof(SendDiskParamsBlock));

                            Log.Write(LogType.Verbose, LogComponent.CopyDisk, "Requested unit for writing is '{0}'", p.UnitName);
                           
                            //
                            // Some sanity (and security) checks:
                            // Name must be a filename only, no paths of any kind allowed.
                            // Oh, and the file must not exist in the directory holding disk packs.
                            //
                            string diskPath = GetPathForDiskImage(p.UnitName.ToString());
                            if (!String.IsNullOrEmpty(Path.GetDirectoryName(p.UnitName.ToString())) ||
                                File.Exists(diskPath))
                            {
                                // Invalid name, return No reponse.
                                VersionYesNoBlock no = new VersionYesNoBlock(CopyDiskBlock.No, (ushort)NoCode.UnitNotReady, "Invalid unit name or image already exists.");
                                _channel.Send(Serializer.Serialize(no));
                            }
                            else
                            {
                                //
                                // Create a new in-memory disk image.  We will write it out to disk when the transfer is completed.
                                //                                                                   
                                // TODO: determine pack type based on disk params rather than assuming Diablo 31
                                _pack = new DiabloPack(DiabloDiskType.Diablo31);
                                _pack.PackName = diskPath;
                                                           

                                // Send a "HereAreDiskParams" response indicating success.
                                //
                                HereAreDiskParamsBFSBlock diskParams = new HereAreDiskParamsBFSBlock(_pack.Geometry);
                                _channel.Send(Serializer.Serialize(diskParams));
                            }
                        }
                        break;

                    case CopyDiskBlock.HereAreDiskParams:
                        {
                            HereAreDiskParamsBFSBlock diskParams = (HereAreDiskParamsBFSBlock)Serializer.Deserialize(data, typeof(HereAreDiskParamsBFSBlock));

                            Log.Write(LogType.Verbose, LogComponent.CopyDisk, "Disk params are: Type {0}, C/H/S {1}/{2}/{3}",
                                diskParams.DiskType,
                                diskParams.Cylinders,
                                diskParams.Heads,
                                diskParams.Sectors);

                        }
                        break;

                    case CopyDiskBlock.RetrieveDisk:
                    case CopyDiskBlock.StoreDisk:
                        {
                            TransferParametersBlock transferParameters = (TransferParametersBlock)Serializer.Deserialize(data, typeof(TransferParametersBlock));

                            _startAddress = _pack.DiskAddressToVirtualAddress(transferParameters.StartAddress);
                            _endAddress = _pack.DiskAddressToVirtualAddress(transferParameters.EndAddress);

                            // Validate that the user is allowed to store.
                            if (blockType == CopyDiskBlock.StoreDisk)
                            {
                                if (_userToken.Privileges != IFSPrivileges.ReadWrite)
                                {
                                    VersionYesNoBlock no = new VersionYesNoBlock(CopyDiskBlock.No, (ushort)NoCode.UnitWriteProtected, "You do not have permission to store disk images.");
                                    _channel.Send(Serializer.Serialize(no));
                                    break;
                                }
                            }

                            Log.Write(LogType.Verbose, LogComponent.CopyDisk, "Transfer is from block {0} to block {1}", transferParameters.StartAddress, transferParameters.EndAddress);

                            // Validate start/end parameters
                            if (_endAddress <= _startAddress ||
                                _startAddress > _pack.MaxAddress ||
                                _endAddress > _pack.MaxAddress)
                            {
                                VersionYesNoBlock no = new VersionYesNoBlock(CopyDiskBlock.No, (ushort)NoCode.UnknownCommand, "Transfer parameters are invalid.");
                                _channel.Send(Serializer.Serialize(no));
                            }
                            else
                            {                                
                                // We're OK.  Save the parameters and send a Yes response.
                                VersionYesNoBlock yes = new VersionYesNoBlock(CopyDiskBlock.Yes, 0, "You are cleared for launch.");
                                _channel.Send(Serializer.Serialize(yes));

                                //
                                // And send the requested range of pages if this is a Retrieve operation
                                // (otherwise wait for a HereIsDiskPage block from the client.)
                                //
                                if (blockType == CopyDiskBlock.RetrieveDisk)
                                {
                                    for (int i = _startAddress; i < _endAddress + 1; i++)
                                    {
                                        DiabloDiskSector sector = _pack.GetSector(i);
                                        HereIsDiskPageBlock block = new HereIsDiskPageBlock(sector.Header, sector.Label, sector.Data);
                                        _channel.Send(Serializer.Serialize(block), false /* do not flush */);

                                        if ((i % 100) == 0)
                                        {
                                            Log.Write(LogType.Verbose, LogComponent.CopyDisk, "Sent page {0}", i);
                                        }
                                    }

                                    // Send "EndOfTransfer" block to finish the transfer.
                                    EndOfTransferBlock endTransfer = new EndOfTransferBlock(0);
                                    _channel.Send(Serializer.Serialize(endTransfer));

                                    Log.Write(LogType.Verbose, LogComponent.CopyDisk, "Send done.");
                                }
                                else
                                {
                                    _currentAddress = _startAddress;
                                }
                            }
                        }
                        break;

                    case CopyDiskBlock.HereIsDiskPage:
                        {
                            if (_currentAddress > _endAddress)
                            {
                                _channel.SendAbort("Invalid address for page.");
                                _running = false;
                                break;
                            }

                            if ((_currentAddress % 100) == 0)
                            {
                                Log.Write(LogType.Verbose, LogComponent.CopyDisk, "Received page {0}", _currentAddress);
                            }
                                                        
                            if (data.Length < 512 + 16 + 4 + 4)
                            {
                                // Incomplete ("incorrigable") page, indicating an unreadable or empty sector, just copy
                                // the header/label data in and leave an empty data page.
                                HereIsDiskPageIncorrigableBlock diskPage = (HereIsDiskPageIncorrigableBlock)Serializer.Deserialize(data, typeof(HereIsDiskPageIncorrigableBlock));
                                DiabloDiskSector sector = new DiabloDiskSector(diskPage.Header, diskPage.Label, new byte[512]);
                                _pack.SetSector(_currentAddress, sector);

                                _currentAddress++;

                                Log.Write(LogType.Verbose, LogComponent.CopyDisk, "Page is empty / incorrigable.");
                            }
                            else
                            {
                                HereIsDiskPageBlock diskPage = (HereIsDiskPageBlock)Serializer.Deserialize(data, typeof(HereIsDiskPageBlock));
                                DiabloDiskSector sector = new DiabloDiskSector(diskPage.Header, diskPage.Label, diskPage.Data);
                                _pack.SetSector(_currentAddress, sector);
                                
                                _currentAddress++;
                            }                            
                        }
                        break;

                    case CopyDiskBlock.EndOfTransfer:
                        {
                            // No data in block.  If we aren't currently at the end of the transfer, the transfer has been aborted.
                            // Do nothing right now.
                            if (_currentAddress < _endAddress)
                            {
                                Log.Write(LogType.Verbose, LogComponent.CopyDisk, "Transfer was aborted.");
                                _running = false;
                            }
                            else
                            {
                                try
                                {
                                    // Commit disk image to disk.
                                    using (FileStream packStream = new FileStream(_pack.PackName, FileMode.OpenOrCreate, FileAccess.Write))
                                    {
                                        Log.Write(LogType.Verbose, LogComponent.CopyDisk, "Saving {0}...", _pack.PackName);
                                        _pack.Save(packStream, true /* reverse byte order */);
                                        Log.Write(LogType.Verbose, LogComponent.CopyDisk, "Saved.");
                                    }                                   
                                }
                                catch(Exception e)
                                {
                                    // Log error, reset state.
                                    Log.Write(LogType.Error, LogComponent.CopyDisk, "Failed to save pack {0} - {1}", _pack.PackName, e.Message);                                    
                                }                               
                            }                            
                        }
                        break;

                    case CopyDiskBlock.SendErrors:
                        {
                            Log.Write(LogType.Verbose, LogComponent.CopyDisk, "Sending error summary...");
                            // No data in block.  Send list of errors we encountered.  (There should always be none since we're perfect and have no disk errors.)
                            HereAreErrorsBFSBlock errorBlock = new HereAreErrorsBFSBlock(0, 0);
                            _channel.Send(Serializer.Serialize(errorBlock));
                        }
                        break;

                    default:
                        Log.Write(LogType.Warning, LogComponent.CopyDisk, "Unhandled CopyDisk block {0}", blockType);
                        break;
                }
            }

            if (OnExit != null)
            {
                OnExit(this);
            }
        }        

        private void ShutdownWorker()
        {
            // Tell the thread to exit and give it a short period to do so...
            _running = false;

            Log.Write(LogType.Verbose, LogComponent.CopyDisk, "Asking CopyDisk worker thread to exit...");
            _workerThread.Join(1000);

            if (_workerThread.IsAlive)
            {
                Logging.Log.Write(LogType.Verbose, LogComponent.CopyDisk, "CopyDisk worker thread did not exit, terminating.");
                _workerThread.Abort();

                if (OnExit != null)
                {
                    OnExit(this);
                }
            }            
        }

        private UserToken AuthenticateUser(string userName, string password)
        {
            //
            // If no username is specified then we default to the guest account.
            //
            if (string.IsNullOrEmpty(userName))
            {
                return UserToken.Guest;
            }

            UserToken user = Authentication.Authenticate(userName, password);            

            return user;

        }

        /// <summary>
        /// Builds a relative path to the directory that holds the disk images.
        /// </summary>
        /// <param name="packName"></param>
        /// <returns></returns>
        private static string GetPathForDiskImage(string packName)
        {
            return Path.Combine(Configuration.CopyDiskRoot, packName);
        }

        private Thread _workerThread;
        private bool _running;

        // The user token for this transaction.  We assume guest access by default.
        private UserToken _userToken = UserToken.Guest;

        // The pack being read / stored by this server
        private DiabloPack _pack = null;

        // Current position and range of a write operation
        private int _currentAddress = 0;
        private int _startAddress = 0;
        private int _endAddress = 0;
    }
}
