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

    /// <summary>
    /// IPupPacketInterface provides an abstraction over a transport (Ethernet, IP, Carrier Pigeon)
    /// which can provide encapsulation for raw Ethernet frames.
    /// 
    /// For the time being, this exists only to provide support for BreathOfLife packets (the only non-PUP
    /// Ethernet Packet the IFS suite deals with).  This only requires being able to send packets, so no
    /// receive is implemented.
    /// </summary>
    public interface IRawPacketInterface
    {
        void Send(byte[] data, byte source, byte destination, ushort frameType);
    }
}
