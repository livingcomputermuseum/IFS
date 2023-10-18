/*  
    This file is part of IFS.

    IFS is free software: you can redistribute it and/or modify
    it under the terms of the GNU Affero General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    IFS is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Affero General Public License for more details.

    You should have received a copy of the GNU Affero General Public License
    along with IFS.  If not, see <http://www.gnu.org/licenses/>.
*/

using IFS.Gateway;
using IFS.IfsConsole;
using PcapDotNet.Core;
using PcapDotNet.Core.Extensions;
using System;
using System.Net.NetworkInformation;

namespace IFS
{
    public class Entrypoint
    {        
        static void Main(string[] args)
        {
            PrintHerald();

            RegisterInterfaces();

            // This runs forever, or until the user tells us to exit.
            RunCommandPrompt();

            // Shut things down
            Console.WriteLine("Shutting down, please wait...");
            Router.Instance.Shutdown();
        }

        private static void PrintHerald()
        {
            Console.WriteLine($"LCM+L IFS {typeof(Entrypoint).Assembly.GetName().Version}, 9/30/2023");
            Console.WriteLine("(c) 2015-2020 Living Computers: Museum+Labs, 2020-2023 Josh Dersch");
            Console.WriteLine();
            Console.WriteLine();
        }

        private static void RegisterInterfaces()
        {
            bool bFound = false;

            string[] selectedInterfaces = Configuration.InterfaceTypes.ToLowerInvariant().Split(new char[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string ifaceName in selectedInterfaces)
            {
                switch (ifaceName)
                {
                    case "udp":
                        // Find matching network interface
                        {
                            NetworkInterface[] ifaces = NetworkInterface.GetAllNetworkInterfaces();
                            foreach (NetworkInterface iface in ifaces)
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
                                if (device.GetNetworkInterface() != null &&
                                    device.GetNetworkInterface().Name.ToLowerInvariant() == Configuration.InterfaceName.ToLowerInvariant())
                                {
                                    Router.Instance.RegisterRAWInterface(device);
                                    bFound = true;
                                    break;
                                }
                            }
                        }
                        break;

                    case "3mbit":
                        Router.Instance.RegisterBeagleBoneInterface();
                        break;

                    default:
                        throw new InvalidConfigurationException(
                            String.Format("The specified interface type ({0}) is invalid.", Configuration.InterfaceTypes));
                }

                // Not found.
                if (!bFound)
                {
                    throw new InvalidConfigurationException(
                        String.Format("The specified network interface ({0}) is invalid or unusable by IFS.", Configuration.InterfaceName));
                }
            }
        }

        private static void RunCommandPrompt()
        {            
            ConsoleExecutor.Instance.Run();
        }
    }
}
