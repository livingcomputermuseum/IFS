using IFS.Boot;
using IFS.CopyDisk;
using IFS.FTP;
using IFS.Transport;
using PcapDotNet.Core;
using PcapDotNet.Core.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;

namespace IFS
{
    public class Entrypoint
    {        
        static void Main(string[] args)
        {
            PrintHerald();

            RegisterProtocols();            

            RegisterInterface();

            // This runs forever, or until the user tells us to exit.
            RunCommandPrompt();            
        }

        private static void PrintHerald()
        {
            Console.WriteLine("LCM IFS v0.1, 4/19/2016.");
            Console.WriteLine();
        }

        private static void RegisterProtocols()
        {
            // Set up protocols:

            // Connectionless
            PUPProtocolDispatcher.Instance.RegisterProtocol(new PUPProtocolEntry("Gateway Information", 2, ConnectionType.Connectionless, new GatewayInformationProtocol()));
            PUPProtocolDispatcher.Instance.RegisterProtocol(new PUPProtocolEntry("Misc Services", 0x4, ConnectionType.Connectionless, new MiscServicesProtocol()));
            PUPProtocolDispatcher.Instance.RegisterProtocol(new PUPProtocolEntry("Echo", 0x5, ConnectionType.Connectionless, new EchoProtocol()));

            // RTP/BSP based:            
            PUPProtocolDispatcher.Instance.RegisterProtocol(new PUPProtocolEntry("CopyDisk", 0x15  /* 25B */, ConnectionType.BSP, typeof(CopyDiskWorker)));
            PUPProtocolDispatcher.Instance.RegisterProtocol(new PUPProtocolEntry("FTP", 0x3, ConnectionType.BSP, typeof(FTPWorker)));

            // Breath Of Life
            _breathOfLifeServer = new BreathOfLife();
        }

        private static void RegisterInterface()
        {
            bool bFound = false;

            switch (Configuration.InterfaceType.ToLowerInvariant())
            {
                case "udp":
                    // Find matching network interface
                    {
                        NetworkInterface[] ifaces = NetworkInterface.GetAllNetworkInterfaces();
                        foreach(NetworkInterface iface in ifaces)
                        {
                            if (iface.Name.ToLowerInvariant() == Configuration.InterfaceName.ToLowerInvariant())
                            {
                                PUPProtocolDispatcher.Instance.RegisterUDPInterface(iface);
                                bFound = true;
                                break;
                            }
                        }
                    }
                    break;

                case "raw":
                    // Find matching RAW interface
                    {
                        foreach (LivePacketDevice device in LivePacketDevice.AllLocalMachine)
                        {
                            if (device.GetNetworkInterface().Name.ToLowerInvariant() == Configuration.InterfaceName.ToLowerInvariant())
                            {
                                PUPProtocolDispatcher.Instance.RegisterRAWInterface(device);
                                bFound = true;
                                break;
                            }
                        }                        
                    }
                    break;
            }

            // Not found.
            if (!bFound)
            {
                throw new InvalidConfigurationException("The specified network interface is invalid.");
            }
        }

        private static void RunCommandPrompt()
        {
            List<UserToken> users = Authentication.EnumerateUsers();

            Authentication.SetPassword(users[0].UserName, "hamdinger");

            UserToken user = Authentication.Authenticate(users[0].UserName, "hamdinger");

            while (true)
            {
                Console.Write(">>>");
                string command = Console.ReadLine();
            }
        }

    private static BreathOfLife _breathOfLifeServer;
    }
}
