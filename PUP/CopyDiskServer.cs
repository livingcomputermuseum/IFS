using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IFS
{
    public class CopyDiskServer : PUPProtocolBase
    {
        /// <summary>
        /// Called by dispatcher to send incoming data destined for this protocol.
        /// </summary>
        /// <param name="p"></param>
        public override void RecvData(PUP p)
        {
            BSPChannel newChannel = BSPManager.RecvData(p);

            if (newChannel != null)
            {
                // spwan new worker thread with new BSP channel
            }
        }
    }
}
