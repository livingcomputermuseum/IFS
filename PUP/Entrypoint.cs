using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IFS
{
    public class Entrypoint
    {
        static void Main(string[] args)
        {
            // Set up protocols:

            // Connectionless
            Dispatcher.Instance.RegisterProtocol(new PUPProtocolEntry("Misc Services", 0x4, ConnectionType.Connectionless, new MiscServicesProtocol()));
            Dispatcher.Instance.RegisterProtocol(new PUPProtocolEntry("Echo", 0x5, ConnectionType.Connectionless, new EchoProtocol()));

            // RTP/BSP based:            
            Dispatcher.Instance.RegisterProtocol(new PUPProtocolEntry("CopyDisk", 0x15  /* 25B */, ConnectionType.BSP, new CopyDiskServer()));

        }
}
