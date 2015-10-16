using PcapDotNet.Packets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IFS.Transport
{

    public delegate void HandlePup(PUP pup);

    /// <summary>
    /// IPupPacketInterface provides an abstraction over a transport (Ethernet, IP, Carrier Pigeon)
    /// which can provide encapsulation for PUPs.
    /// </summary>
    public interface IPupPacketInterface
    {
        void Send(PUP p);

        void RegisterReceiveCallback(HandlePup callback);
    }
}
