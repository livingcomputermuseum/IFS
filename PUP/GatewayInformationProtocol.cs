using IFS.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IFS
{
    /// <summary>
    /// Gateway Information Protocol (see http://xeroxalto.computerhistory.org/_cd8_/pup/.gatewayinformation.press!1.pdf)
    /// </summary>
    public class GatewayInformationProtocol : PUPProtocolBase
    {
        public GatewayInformationProtocol()
        {
            // TODO:
            // load host tables, etc.
        }

        /// <summary>
        /// Called by dispatcher to send incoming data destined for this protocol
        /// </summary>
        /// <param name="p"></param>
        public override void RecvData(PUP p)
        {
            switch (p.Type)
            {
                

                default:
                    Log.Write(LogLevel.UnhandledProtocol, String.Format("Unhandled Gateway protocol {0}", p.Type));
                    break;
            }
        }


    }
}
