using IFS.CopyDisk;
using IFS.FTP;
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
            List<EthernetInterface> ifaces = EthernetInterface.EnumerateDevices();

            Console.WriteLine("available interfaces are:");
            foreach(EthernetInterface i in ifaces)
            {
                Console.WriteLine(String.Format("{0} - address {1}, desc {2} ", i.Name, i.MacAddress, i.Description));
            }           

            // Set up protocols:

            // Connectionless
            PUPProtocolDispatcher.Instance.RegisterProtocol(new PUPProtocolEntry("Gateway Information", 2, ConnectionType.Connectionless, new GatewayInformationProtocol()));
            PUPProtocolDispatcher.Instance.RegisterProtocol(new PUPProtocolEntry("Misc Services", 0x4, ConnectionType.Connectionless, new MiscServicesProtocol()));
            PUPProtocolDispatcher.Instance.RegisterProtocol(new PUPProtocolEntry("Echo", 0x5, ConnectionType.Connectionless, new EchoProtocol()));

            // RTP/BSP based:            
            PUPProtocolDispatcher.Instance.RegisterProtocol(new PUPProtocolEntry("CopyDisk", 0x15  /* 25B */, ConnectionType.BSP, new CopyDiskServer()));
            PUPProtocolDispatcher.Instance.RegisterProtocol(new PUPProtocolEntry("FTP", 0x3, ConnectionType.BSP, new FTPServer()));

            // TODO: MAKE THIS CONFIGURABLE.
            PUPProtocolDispatcher.Instance.RegisterInterface(ifaces[2]);

            while (true)
            {
                System.Threading.Thread.Sleep(100);
            }
        }
    }
}
