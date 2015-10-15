using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using PcapDotNet.Core;
using PcapDotNet.Core.Extensions;
using PcapDotNet.Packets;
using PcapDotNet.Packets.Ethernet;

namespace IFS.Transport
{
    public struct EthernetInterface
    {
        public EthernetInterface(string name, string description, MacAddress macAddress)
        {
            Name = name;
            Description = description;
            MacAddress = macAddress;
        }

        public static List<EthernetInterface> EnumerateDevices()
        {
            List<EthernetInterface> interfaces = new List<EthernetInterface>();

            foreach (LivePacketDevice device in LivePacketDevice.AllLocalMachine)
            {
                interfaces.Add(new EthernetInterface(device.Name, device.Description, device.GetMacAddress()));
            }

            return interfaces;
        }

        public string       Name;
        public string       Description;
        public MacAddress   MacAddress;
    }

    /// <summary>
    /// Defines interface "to the metal" (raw ethernet frames) which may wrap the underlying transport (for example, winpcap)
    /// </summary>
    public class Ethernet : IPacketInterface
    {
        public Ethernet(EthernetInterface iface, HandlePacket callback)
        {
            AttachInterface(iface);
            _callback = callback;

            Open(false, int.MaxValue);
            BeginReceive();
        }

        public void Open(bool promiscuous, int timeout)
        {
            _communicator = _interface.Open(0xffff, promiscuous ? PacketDeviceOpenAttributes.Promiscuous : PacketDeviceOpenAttributes.None, timeout);            
        }

        /// <summary>
        /// Begin receiving packets, forever.
        /// </summary>
        public void BeginReceive()
        {            
            _communicator.ReceivePackets(-1, ReceiveCallback);
        }

        public void SendPacket(Packet p)
        {
            _communicator.SendPacket(p);
        }

        public object GetDeviceAddress()
        {
            return (object)_interface.GetMacAddress();
        }

        private void ReceiveCallback(Packet p)
        {            
            _callback(p);            
        }

        private void AttachInterface(EthernetInterface iface)
        {
            _interface = null;

            // Find the specified device by name
            foreach (LivePacketDevice device in LivePacketDevice.AllLocalMachine)
            {
                if (device.Name == iface.Name && device.GetMacAddress() == iface.MacAddress)
                {
                    _interface = device;
                    break;
                }
            }

            if (_interface == null)
            {
                throw new InvalidOperationException("Requested interface not found.");
            }
        }

        private LivePacketDevice _interface;
        private PacketCommunicator _communicator;
        private HandlePacket _callback;

    }
}
