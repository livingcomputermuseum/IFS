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
        struct foo
        {
            public ushort Bar;
            public short Baz;
            public byte Thing;            
            public int Inty;
            public uint Uinty;
            public BCPLString Quux;
        }

        static void Main(string[] args)
        {
            foo newFoo = new foo();
            newFoo.Bar = 0x1234;
            newFoo.Baz = 0x5678;
            newFoo.Thing = 0xcc;
            newFoo.Inty = 0x01020304;
            newFoo.Uinty = 0x05060708;
            newFoo.Quux = new BCPLString("The quick brown fox jumped over the lazy dog's tail.");

            byte[] data = Serializer.Serialize(newFoo);


            foo oldFoo = (foo) Serializer.Deserialize(data, typeof(foo));
            

            Logging.Log.Level = Logging.LogLevel.All;

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

            // TODO: MAKE THIS CONFIGURABLE.
            PUPProtocolDispatcher.Instance.RegisterInterface(ifaces[2]);

            while (true)
            {
                System.Threading.Thread.Sleep(100);
            }
        }
    }
}
