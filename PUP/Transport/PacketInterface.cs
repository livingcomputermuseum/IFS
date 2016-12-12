using IFS.Gateway;
using PcapDotNet.Packets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IFS.Transport
{
    /// <summary>
    /// IPupPacketInterface provides an abstraction over a transport (Ethernet, IP, Carrier Pigeon)
    /// which can provide encapsulation for PUPs.
    /// </summary>
    public interface IPupPacketInterface
    {
        /// <summary>
        /// Sends the given PUP over the transport.
        /// </summary>
        /// <param name="p"></param>
        void Send(PUP p);

        /// <summary>
        /// Registers a callback (into the router) to be invoked on receipt of a PUP.
        /// </summary>
        /// <param name="callback"></param>
        void RegisterRouterCallback(RoutePupCallback callback);

        /// <summary>
        /// Shuts down the interface.
        /// </summary>
        void Shutdown();
    }

    /// <summary>
    /// IRawPacketInterface provides an abstraction over a transport (Ethernet, IP, Carrier Pigeon)
    /// which can provide encapsulation for raw Ethernet frames.
    /// 
    /// For the time being, this exists only to provide support for BreathOfLife packets (the only non-PUP
    /// Ethernet Packet the IFS suite deals with).  This only requires being able to send packets, so no
    /// receive is implemented.
    /// 
    /// Note also that no routing will be provided for raw packets; they are sent to the local 'net only.
    /// </summary>
    public interface IRawPacketInterface
    {
        /// <summary>
        /// Sends the specified data over the transport.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="source"></param>
        /// <param name="destination"></param>
        /// <param name="frameType"></param>
        void Send(byte[] data, byte source, byte destination, ushort frameType);
    }
}
