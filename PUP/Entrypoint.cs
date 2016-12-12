using IFS.Boot;
using IFS.CopyDisk;
using IFS.FTP;
using IFS.Gateway;
using IFS.IfsConsole;
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

            RegisterInterface();

            // This runs forever, or until the user tells us to exit.
            RunCommandPrompt();

            // Shut things down
            Console.WriteLine("Shutting down, please wait...");
            Router.Instance.Shutdown();

        }

        private static void PrintHerald()
        {
            Console.WriteLine("LCM IFS v0.1, 4/19/2016.");
            Console.WriteLine();
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
                                Router.Instance.RegisterUDPInterface(iface);
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
                                Router.Instance.RegisterRAWInterface(device);
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
            ConsoleExecutor.Instance.Run();
        }
    }
}
