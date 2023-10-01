using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IFS.Transport
{
    /// <summary>
    /// Helper functions for building Ethernet frames in a variety of ways. 
    /// </summary>
    public static class PupPacketBuilder
    {
        // The ethertype used in the encapsulated 3mbit frame
        public static readonly ushort PupFrameType = 512;

        public static byte[] BuildEncapsulatedEthernetFrameFromPup(PUP p)
        {
            return BuildEncapsulatedEthernetFrameFromRawData(p.RawData, p.SourcePort.Host, p.DestinationPort.Host, PupFrameType);
        }

        public static byte[] BuildEncapsulatedEthernetFrameFromRawData(byte[] data, byte source, byte destination, ushort frameType)
        {
            // Build the outgoing data; this is:
            // 1st word: length of data following
            // 2nd word: 3mbit destination / source bytes
            // 3rd word: frame type
            byte[] newFrame = new byte[6 + data.Length];

            // 3mbit Packet length
            newFrame[0] = (byte)((data.Length / 2 + 2) >> 8);
            newFrame[1] = (byte)(data.Length / 2 + 2);

            // addressing
            newFrame[2] = destination;
            newFrame[3] = source;

            // frame type
            newFrame[4] = (byte)(frameType >> 8);
            newFrame[5] = (byte)frameType;

            // Actual data
            data.CopyTo(newFrame, 6);

            return newFrame;
        }

        public static byte[] BuildEthernetFrameFromPup(PUP p)
        {
            return BuildEthernetFrameFromRawData(p.RawData, p.SourcePort.Host, p.DestinationPort.Host, PupFrameType);
        }

        public static byte[] BuildEthernetFrameFromRawData(byte[] data, byte source, byte destination, ushort frameType)
        {
            // Build the full raw frame data; this is:
            // 2nd word: 3mbit destination / source bytes
            // 3rd word: frame type
            byte[] newFrame = new byte[4 + data.Length];

            // addressing
            newFrame[0] = destination;
            newFrame[1] = source;

            // frame type
            newFrame[2] = (byte)(frameType >> 8);
            newFrame[3] = (byte)frameType;

            // Actual data
            data.CopyTo(newFrame, 4);

            return newFrame;
        }
    }
}
