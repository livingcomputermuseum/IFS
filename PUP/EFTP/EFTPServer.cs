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
    /// via FTP, so that actual servers (Boot, Printing, etc.) can serve their clients.
    /// </summary>
    public class EFTPServer : PUPProtocolBase
    {
        /// <summary>
        /// Called by dispatcher to send incoming data destined for this protocol.
        /// </summary>
        /// <param name="p"></param>
        public override void RecvData(PUP p)
        {
            
        }        
    }

    public class EFTPWorker
    {
        public EFTPWorker(BSPChannel channel)
        {

        }
    }
}
