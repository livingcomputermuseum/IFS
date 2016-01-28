using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace IFS
{

    public class HostAddress
    {
        public HostAddress(byte network, byte host)
        {
            Network = network;
            Host = host;
        }

        public byte Network;
        public byte Host;

        /// <summary>
        /// Non-existent address
        /// </summary>
        public static HostAddress Empty = new HostAddress(0, 0);
    }

    /// <summary>
    /// Provides a basic database used by the Misc. Services to do name lookup and whatnot.
    /// 
    /// </summary>
    public class DirectoryServices
    {
        private DirectoryServices()
        {
            // Get our host address; for now just hardcode it.
            // TODO: need to define config files, etc.

            _localHost = new HostAddress(1, 1);

            // Load in hosts table from hosts file.
            LoadHostTable();

            Logging.Log.Write(Logging.LogLevel.Normal, "Directory services initialized.");
        }

        public string AddressLookup(HostAddress address)
        {
            //
            // First look up the network.  If specified network is Zero,
            // we will assume our network number.
            //
            byte network = address.Network;

            if (network == 0)
            {
                network = _localHost.Network;
            }

            if (_hostAddressTable.ContainsKey(network))
            {
                //
                // We have entries for this network, see if the host is specified.
                //
                if (_hostAddressTable[network].ContainsKey(address.Host))
                {
                    return _hostAddressTable[network][address.Host];
                }
            }

            // Not found.
            return null;
        }

        public HostAddress NameLookup(string hostName)
        {            
            if (_hostNameTable.ContainsKey(hostName.ToLowerInvariant()))
            {
                return _hostNameTable[hostName.ToLowerInvariant()];
            }

            return null;
        }

        public static DirectoryServices Instance
        {
            get { return _instance; }
        }

        public HostAddress LocalHostAddress
        {
            get { return _localHost; }
        }

        public byte LocalNetwork
        {
            get { return _localHost.Network; }
        }

        public byte LocalHost
        {
            get { return _localHost.Host; }
        }

        private void LoadHostTable()
        {
            _hostAddressTable = new Dictionary<byte, Dictionary<byte, string>>();
            _hostNameTable = new Dictionary<string, HostAddress>();

            // TODO: do not hardcode path like this.
            using (StreamReader sr = new StreamReader("Conf\\hosts.txt"))
            {
                int lineNumber = 0;
                while (!sr.EndOfStream)
                {
                    lineNumber++;

                    //
                    // A line is either:
                    //  '#' followed by comment to EOL
                    // <inter-network name> <hostname>
                    // Any whitespace is ignored
                    // 
                    // Format for Inter-Network name expressions is either:
                    //  network#host#  (to specify hosts on another network)
                    //    or
                    //  host#          (to specify hosts on our network)
                    //

                    string line = sr.ReadLine().Trim().ToLowerInvariant();

                    if (line.StartsWith("#") || String.IsNullOrWhiteSpace(line))
                    {
                        // Comment or empty, just ignore
                        continue;
                    }

                    // Tokenize on whitespace
                    string[] tokens = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                    // We need at least two tokens (inter-network name and one hostname)
                    if (tokens.Length < 2)
                    {
                        // Log warning and continue.
                        Logging.Log.Write(Logging.LogLevel.Warning,
                            String.Format("hosts.txt line {0}: Invalid syntax.", lineNumber));

                        continue;
                    }

                    // First token should be an inter-network name, which should end with '#'.
                    if (!tokens[0].EndsWith("#"))
                    {
                        // Log warning and continue.
                        Logging.Log.Write(Logging.LogLevel.Warning,
                            String.Format("hosts.txt line {0}: Improperly formed inter-network name '{1}'.", lineNumber, tokens[0]));

                        continue;
                    }

                    // tokenize on '#'
                    string[] networkTokens = tokens[0].Split(new char[] { '#' }, StringSplitOptions.RemoveEmptyEntries);

                    HostAddress host = new HostAddress(0, 0);
                    // 1 token means a local name, 2 means on other network, anything else is illegal
                    if (networkTokens.Length == 1)
                    {
                        try
                        {
                            host.Host = Convert.ToByte(networkTokens[0], 8);
                            host.Network = _localHost.Network;
                        }
                        catch
                        {
                            // Log warning and continue.
                            Logging.Log.Write(Logging.LogLevel.Warning,
                                String.Format("hosts.txt line {0}: Invalid host number in inter-network address '{1}'.", lineNumber, tokens[0]));

                            continue;
                        }
                    }
                    else if (networkTokens.Length == 2)
                    {
                        try
                        {
                            host.Network = Convert.ToByte(networkTokens[0], 8);
                            host.Host = Convert.ToByte(networkTokens[1], 8);
                        }
                        catch
                        {
                            // Log warning and continue.
                            Logging.Log.Write(Logging.LogLevel.Warning,
                                String.Format("hosts.txt line {0}: Invalid host or network number in inter-network address '{1}'.", lineNumber, tokens[0]));

                            continue;
                        }
                    }
                    else
                    {
                        // Log warning and continue.
                        Logging.Log.Write(Logging.LogLevel.Warning,
                            String.Format("hosts.txt line {0}: Improperly formed inter-network name '{1}'.", lineNumber, tokens[0]));

                        continue;
                    }

                    // Hash the host by one or more names
                    for (int i=1;i<tokens.Length;i++)
                    {
                        string hostName = tokens[i];

                        // Add to name table
                        if (_hostNameTable.ContainsKey(hostName))
                        {
                            // Duplicate name entry!  Skip this line.
                            Logging.Log.Write(Logging.LogLevel.Warning,
                                String.Format("hosts.txt line {0}: Duplicate hostname '{1}'.", lineNumber, hostName));
                            break;
                        }

                        _hostNameTable.Add(hostName, host);

                        // Add to address table:
                        // - See if network has entry
                        Dictionary<byte, string> networkTable = null;

                        if (_hostAddressTable.ContainsKey(host.Network))
                        {
                            networkTable = _hostAddressTable[host.Network];
                        }
                        else
                        {
                            // No entry for this network yet, add it now
                            networkTable = new Dictionary<byte, string>();
                            _hostAddressTable.Add(host.Network, networkTable);
                        }

                        // Add to network table
                        if (networkTable.ContainsKey(host.Host))
                        {
                            // Duplicate host entry!  Skip this line.
                            Logging.Log.Write(Logging.LogLevel.Warning,
                               String.Format("hosts.txt line {0}: Duplicate host ID '{1}'.", lineNumber, host.Host));
                            break;
                        }

                        networkTable.Add(host.Host, hostName);
                        
                    }                    
                }

            }

        }

        /// <summary>
        /// Points to us.
        /// </summary>
        private HostAddress _localHost;

        /// <summary>
        /// Hash table for address resolution; outer hash finds the dictionary
        /// for a given network, inner hash finds names for hosts.
        /// </summary>
        private Dictionary<byte, Dictionary<byte, string>> _hostAddressTable;

        /// <summary>
        /// Hash table for name resolution.
        /// </summary>
        private Dictionary<string, HostAddress> _hostNameTable;

        private static DirectoryServices _instance = new DirectoryServices();
    }
}
