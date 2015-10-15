using PcapDotNet.Packets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IFS.Transport
{
    /// <summary>
    /// PacketInterface provides an abstraction over a transport (Ethernet, IP, Carrier Pigeon)
    /// which can provide raw packets.
    /// </summary>
    public interface IPacketInterface
    {
        void SendPacket(Packet p);

        object GetDeviceAddress();
    }
}
