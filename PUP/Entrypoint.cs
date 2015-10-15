using IFS.Transport;
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

            Logging.Log.Level = Logging.LogLevel.All;

            List<EthernetInterface> ifaces = EthernetInterface.EnumerateDevices();

            Console.WriteLine("available interfaces are:");
            foreach(EthernetInterface i in ifaces)
            {
                Console.WriteLine(String.Format("{0} - address {1}", i.Name, i.MacAddress));
            }

            // TODO: MAKE THIS CONFIGURABLE.
            Dispatcher.Instance.RegisterInterface(ifaces[1]);

            // Set up protocols:

            // Connectionless
            Dispatcher.Instance.RegisterProtocol(new PUPProtocolEntry("Misc Services", 0x4, ConnectionType.Connectionless, new MiscServicesProtocol()));
            Dispatcher.Instance.RegisterProtocol(new PUPProtocolEntry("Echo", 0x5, ConnectionType.Connectionless, new EchoProtocol()));

            // RTP/BSP based:            
            Dispatcher.Instance.RegisterProtocol(new PUPProtocolEntry("CopyDisk", 0x15  /* 25B */, ConnectionType.BSP, new CopyDiskServer()));

            while (true)
            {
                System.Threading.Thread.Sleep(100);
            }
        }
    }
}
