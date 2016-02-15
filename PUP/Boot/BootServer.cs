using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IFS.Boot
{
    public class BootServerProtocol : PUPProtocolBase
    {
        public BootServerProtocol()
        {

        }

        /// <summary>
        /// Called by dispatcher to send incoming data destined for this protocol
        /// </summary>
        /// <param name="p"></param>
        public override void RecvData(PUP p)
        {
        }
    }
}
