using System;
using System.Collections.Generic;
using System.Linq;
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

            _localHost = new HostAddress(1, 34);
        }

        public string AddressLookup(HostAddress address)
        {
            // TODO: actually look things up
            return "Alto";
        }

        public HostAddress NameLookup(string hostName)
        {
            // TODO: actually look things up
            return new HostAddress(1, 0x80);
        }

        public static DirectoryServices Instance
        {
            get { return _instance; }
        }

        public HostAddress LocalHostAddress
        {
            get { return _localHost; }
        }

        private HostAddress _localHost;

        private static DirectoryServices _instance = new DirectoryServices();
    }
}
