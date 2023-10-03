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
using IFS.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace IFS.Transport
{
    /// <summary>
    /// This provides a packet interface implementation that talks to Ken Shirriff's 3mbit interface on the BeagleBone.
    /// See https://github.com/shirriff/alto-ethernet-interface for the original code.  This class effectively replaces
    /// the "gateway" C program and talks directly to the PRUs on the beaglebone to exchange packets with the hardware.
    /// The PRU data and code files etherdata.bin and ethertext.bin are used to load the PRU with the appropriate
    /// 3mbit driver code; these are included with this project and must be placed alongside IFS.exe in order to be
    /// found and loaded.
    /// 
    /// This code is more or less a direct port of Ken's code over to C#, with a bit of cleanup to make it more palatable
    /// for C# coding styles.  Though it's still pretty rough.
    /// </summary>
    public class Ether3MbitInterface : IPacketInterface
    {
        public Ether3MbitInterface()
        {
            _ledController = new BeagleBoneLedController();
            InitializePRU();
            StartReceiver();
            StartHeartbeat();
        }

        public void RegisterRouterCallback(ReceivedPacketCallback callback)
        {
            _routerCallback = callback;
        }

        public void Send(PUP p)
        {
            byte[] frameData = PupPacketBuilder.BuildEthernetFrameFromPup(p);
            SendToNetworkInterface(frameData);
        }

        public void Send(byte[] data, byte source, byte destination, ushort frameType)
        {
            byte[] frameData = PupPacketBuilder.BuildEthernetFrameFromRawData(data, source, destination, frameType);
            SendToNetworkInterface(frameData);
        }

        public void Send(MemoryStream encapsulatedPacketStream)
        {
            byte[] encapsulatedFrameData = encapsulatedPacketStream.ToArray();
            // Skip the first two bytes (encapsulated length info).  This is annoying.
            byte[] frameData = new byte[encapsulatedFrameData.Length - 2];
            Array.Copy(encapsulatedFrameData, 2, frameData, 0, frameData.Length);

            SendToNetworkInterface(frameData);
        }

        public void Shutdown()
        {

        }

        private void InitializePRU()
        {
            Log.Write(LogType.Normal, LogComponent.E3Mbit, "PRU Initialization started.");

            PRU.prussdrv_init();

            if (PRU.prussdrv_open(PRU.PRU_EVTOUT_0) == -1)
            {
                throw new InvalidOperationException("Unable to open PRU.");
            }

            PRU.tpruss_intc_initdata initData;
            initData.sysevts_enabled = new byte[]{ PRU.PRU0_PRU1_INTERRUPT, PRU.PRU1_PRU0_INTERRUPT, PRU.PRU0_ARM_INTERRUPT, PRU.PRU1_ARM_INTERRUPT, PRU.ARM_PRU0_INTERRUPT, PRU.ARM_PRU1_INTERRUPT,  15, 0xff };
            initData.sysevt_to_channel_map = new PRU.tsysevt_to_channel_map[]
            {
                new PRU.tsysevt_to_channel_map(PRU.PRU0_PRU1_INTERRUPT, PRU.CHANNEL1),
                new PRU.tsysevt_to_channel_map(PRU.PRU1_PRU0_INTERRUPT, PRU.CHANNEL0),
                new PRU.tsysevt_to_channel_map(PRU.PRU0_ARM_INTERRUPT, PRU.CHANNEL2),
                new PRU.tsysevt_to_channel_map(PRU.PRU1_ARM_INTERRUPT, PRU.CHANNEL3),
                new PRU.tsysevt_to_channel_map(PRU.ARM_PRU0_INTERRUPT, PRU.CHANNEL0),
                new PRU.tsysevt_to_channel_map(PRU.ARM_PRU1_INTERRUPT, PRU.CHANNEL1),
                new PRU.tsysevt_to_channel_map(15, PRU.CHANNEL0),
                new PRU.tsysevt_to_channel_map(-1, -1),
            };

            initData.channel_to_host_map = new PRU.tchannel_to_host_map[]
            {
                new PRU.tchannel_to_host_map(PRU.CHANNEL0, PRU.PRU0),
                new PRU.tchannel_to_host_map(PRU.CHANNEL1, PRU.PRU1),
                new PRU.tchannel_to_host_map(PRU.CHANNEL2, PRU.PRU_EVTOUT0),
                new PRU.tchannel_to_host_map(PRU.CHANNEL3, PRU.PRU_EVTOUT1),
                new PRU.tchannel_to_host_map(-1, -1),
            };

            initData.host_enable_bitmask = PRU.PRU0_HOSTEN_MASK | PRU.PRU1_HOSTEN_MASK | PRU.PRU_EVTOUT0_HOSTEN_MASK | PRU.PRU_EVTOUT1_HOSTEN_MASK;

            PRU.prussdrv_pruintc_init(ref initData);

            if (PRU.prussdrv_load_datafile(0, "etherdata.bin") < 0)
            {
                throw new InvalidOperationException("Unable to load PRU data file 'etherdata.bin'.");
            }

            if (PRU.prussdrv_exec_program(0, "ethertext.bin") < 0)
            {
                throw new InvalidOperationException("Unable to load and exec PRU program file 'ethertext.bin'.");
            }

            if (PRU.prussdrv_map_prumem(PRU.PRUSS0_PRU0_DATARAM, out _sharedPruMemory) < 0)
            {
                throw new InvalidOperationException("Unable to map PRU shared memory.");
            }

            Log.Write(LogType.Verbose, LogComponent.E3Mbit, "Shared PRU memory at 0x{0:x}", _sharedPruMemory.ToInt64());

            // Initialize PRU control block:
            PruInterfaceControlBlock cb;
            cb.r_owner = OWNER_PRU;
            cb.r_buf = R_PTR_OFFSET;
            cb.r_max_length = MAX_SIZE;
            cb.r_received_length = 0;
            cb.r_status = 0;
            cb.r_truncated = 0;
            cb.w_owner = OWNER_ARM;
            cb.w_buf = W_PTR_OFFSET;
            cb.w_length = 0;
            cb.w_status = 0;

            SetInterfaceControlBlock(cb);

            Log.Write(LogType.Normal, LogComponent.E3Mbit, "PRU Initialization completed.");
        }

        private void StartHeartbeat()
        {
            ThreadPool.QueueUserWorkItem((ctx) =>
            {
                while(true)
                {
                    _ledController.BlinkLed(0, 500);
                    Thread.Sleep(75);
                    _ledController.BlinkLed(0, 100);
                    Thread.Sleep(100);                    
                }
            }, null);
        }

        private void StartReceiver()
        {
            ThreadPool.QueueUserWorkItem((ctx) =>
            {
                Log.Write(LogType.Normal, LogComponent.E3Mbit, "Starting receiver thread.");
                ReceiveWorker();
            }, null);
        }

        /// <summary>
        /// Worker thread function.  Waits for incoming packets on the 3mbit network and handles them
        /// when they arrive.
        /// </summary>
        private void ReceiveWorker()
        {
            while(true)
            {
                // Wait for the next wakeup from the PRU
                PRU.prussdrv_pru_wait_event(PRU.PRU_EVTOUT_0);

                // Clear it
                PRU.prussdrv_pru_clear_event(PRU.PRU_EVTOUT_0, PRU.PRU0_ARM_INTERRUPT);

                if (HostOwnsReadBuffer())
                {                    
                    // PRU gave us a read packet from the 3mbit Ether, handle it.
                    ReceiveFromNetworkInterface();
                }
            }
        }

        //
        // The following functions read and write the control block located in Host/PRU shared memory.
        //

        private void SetInterfaceControlBlock(PruInterfaceControlBlock controlBlock)
        {
            Marshal.StructureToPtr(controlBlock, _sharedPruMemory, false);
        }

        private PruInterfaceControlBlock GetInterfaceControlBlock()
        {
            return (PruInterfaceControlBlock)Marshal.PtrToStructure(_sharedPruMemory, typeof(PruInterfaceControlBlock));
        }

        private bool HostOwnsReadBuffer()
        {
            // r_owner is at offset + 0
            return Marshal.ReadInt32(_sharedPruMemory) == OWNER_ARM;
        }

        private bool HostOwnsWriteBuffer()
        {
            // w_owner is at offset + 24
            return Marshal.ReadInt32(new IntPtr(_sharedPruMemory.ToInt64() + 24)) == OWNER_ARM;
        }

        private void SetReadBufferOwner(UInt32 owner)
        {
            // r_owner is at offset + 0
            Marshal.WriteInt32(_sharedPruMemory, (int)owner);
        }

        private void SetWriteBufferOwner(UInt32 owner)
        {
            // w_owner is at offset + 24
            Marshal.WriteInt32(new IntPtr(_sharedPruMemory.ToInt64() + 24), (int)owner);
        }

        private void SetWriteBufferLength(UInt32 length)
        {
            // w_length is at offset + 28
            Marshal.WriteInt32(new IntPtr(_sharedPruMemory.ToInt64() + 28), (int)length);
        }

        /// <summary>
        /// Pulls data received from the 3mbit interface and passes it to the router.
        /// </summary>
        private void ReceiveFromNetworkInterface()
        {
            PruInterfaceControlBlock cb = GetInterfaceControlBlock();

            if (cb.r_truncated != 0)
            {
                Log.Write(LogType.Warning, LogComponent.E3Mbit, "Truncated packet recieved.");
                cb.r_truncated = 0;
                SetInterfaceControlBlock(cb);
                SetReadBufferOwner(OWNER_PRU);
                return;
            }

            if (cb.r_status != STATUS_INPUT_COMPLETE)
            {
                Log.Write(LogType.Warning, LogComponent.E3Mbit, "Bad PRU status 0x{0:x}", cb.r_status);
                SetReadBufferOwner(OWNER_PRU);
                return;
            }

            int receivedDataLength = (int)cb.r_received_length;
            if (receivedDataLength > MAX_SIZE)
            {
                Log.Write(LogType.Warning, LogComponent.E3Mbit, "Received data too long (0x{0:x} bytes)", receivedDataLength);
                SetReadBufferOwner(OWNER_PRU);
                return;
            }

            if (receivedDataLength == 0)
            {
                Log.Write(LogType.Warning, LogComponent.E3Mbit, "Received 0 bytes of duration data.  Ignoring packet.");
                SetReadBufferOwner(OWNER_PRU);
                return;
            }

            _ledController.SetLed(3, 1);

            // Grab the received data from the shared PRU memory:
            byte[] durationBuffer = new byte[receivedDataLength];
            Marshal.Copy(new IntPtr(_sharedPruMemory.ToInt64() + R_PTR_OFFSET), durationBuffer, 0, receivedDataLength);

            // Ready for next packet
            SetReadBufferOwner(OWNER_PRU);

            byte[] decodedPacket = DecodeDurationBuffer(durationBuffer);
            if (decodedPacket == null)
            {
                Log.Write(LogType.Warning, LogComponent.E3Mbit, "Received bad packet.");
                _ledController.SetLed(3, 0);
                return;
            }

            // Prepend packet length for our internal encapsulation (annoying since we're just going to strip it off again...)
            byte[] encapsulatedPacket = new byte[decodedPacket.Length + 2];
            Array.Copy(decodedPacket, 0, encapsulatedPacket, 2, decodedPacket.Length);

            int encapsulatedLength = decodedPacket.Length / 2 + 2;
            encapsulatedPacket[0] = (byte)(encapsulatedLength >> 8);
            encapsulatedPacket[1] = (byte)encapsulatedLength;

            MemoryStream packetStream = new MemoryStream(encapsulatedPacket);
            _routerCallback(packetStream, this);
            _ledController.SetLed(3, 0);

            Log.Write(LogType.Verbose, LogComponent.E3Mbit, "Received packet (0x{0:x} bytes), sent to router.", receivedDataLength);
        }

        /// <summary>
        /// Sends data to the 3mbit interface.
        /// </summary>
        private void SendToNetworkInterface(byte[] data)
        {
            if (!HostOwnsWriteBuffer())
            {
                // Shouldn't happen
                Log.Write(LogType.Error, LogComponent.E3Mbit, "SendToNetworkInterface called when PRU is not ready.");
                return;
            }

            _ledController.SetLed(2, 1);

            ushort crcVal = CalculateCRC(data, data.Length);

            // Construct a new buffer with space for the CRC
            byte[] fullPacket = new byte[data.Length + 2];
            Array.Copy(data, fullPacket, data.Length);

            fullPacket[fullPacket.Length - 2] = (byte)(crcVal >> 8);
            fullPacket[fullPacket.Length - 1] = (byte)(crcVal);

            // Copy the buffer to the shared PRU memory.
            Marshal.Copy(fullPacket, 0, new IntPtr(_sharedPruMemory.ToInt64() + W_PTR_OFFSET), fullPacket.Length);
            SetWriteBufferLength((uint)fullPacket.Length);

            // Signal PRU to send the data in the write buffer.
            SetWriteBufferOwner(OWNER_PRU);
            _ledController.SetLed(2, 0);

            Log.Write(LogType.Verbose, LogComponent.E3Mbit, "Packet sent to 3mbit interface.");
        }

        /// <summary>
        /// Decodes bit timings into packet data.  Returns null if issues were found with the data.
        /// </summary>
        /// <param name="durationBuf"></param>
        /// <returns></returns>
        byte[] DecodeDurationBuffer(byte[] durationBuf) {

            Log.Write(LogType.Verbose, LogComponent.E3Mbit, $"Decoding duration buffer of length {durationBuf.Length}.");

            List<byte> byteBuffer = new List<byte>();
            bool[] bitBuf = new bool[8 * PUP.MAX_PUP_SIZE];
            const int RECV_WIDTH = 2; // Recv values are in units of 2 ns (to fit in byte)

            // Convert timings in durationBuf into high/low vector in bitBuf
            // bitBuf holds values like 1, 0, 0, 1, 0, 1, indicating if the input
            // was high or low during that time interval.
            // A Manchester-encoded data bit consists of two values in bitBuf.
            int offset1; // Offset into timing vector
            int offset2 = 0; // Offset into bit vector
            bool value = true; // Current high/low value
            for (offset1 = 0; offset1 < durationBuf.Length; offset1++)
            {
                int width = durationBuf[offset1] * RECV_WIDTH;
                if (width < 120)
                {
                    Log.Write(LogType.Error, LogComponent.E3Mbit, $"Bad width {width} at {offset1} of {durationBuf.Length}");
                    return null;
                }
                else if (width < 230)
                {
                    value = !value;
                    bitBuf[offset2++] = value;
                }
                else if (width < 280)
                {
                    Log.Write(LogType.Error, LogComponent.E3Mbit, $"Bad width {width} at {offset1} of {durationBuf.Length}");
                    return null;
                }
                else if (width < 400)
                {
                    value = !value;
                    bitBuf[offset2++] = value;
                    bitBuf[offset2++] = value;
                }
                else
                {
                    Log.Write(LogType.Error, LogComponent.E3Mbit, $"Bad width {width} at {offset1} of {durationBuf.Length}");
                    return null;
                }
            }

            // Convert bit pairs in bitBuf to bytes in byteBuffer
            byte b = 0;
            int i;
            if ((offset2 % 2) == 0)
            {
                // For a 0 bit, the last 1 signal gets combined with the no-signal state and lost.
                // So add it back.
                bitBuf[offset2] = true;
                offset2 += 1;
            }

            Log.Write(LogType.Verbose, LogComponent.E3Mbit, $"Offset2 is {offset2}.");
            // Start at 1 to skip sync
            for (i = 1; i < offset2; i += 2)
            {
                if (bitBuf[i] == bitBuf[i + 1])
                {
                    Log.Write(LogType.Error, LogComponent.E3Mbit, $"Bad bit sequence at {i} of {offset2}: {bitBuf[i]}, {bitBuf[i+1]}");
                    b = (byte)(b << 1);
                }
                else
                {
                    b = (byte)((b << 1) | (bitBuf[i] ? 1 : 0));
                }
                if ((i % 16) == 15)
                {
                    byteBuffer.Add(b);
                    b = 0;
                }
            }
            if ((offset2 % 16) != 1)
            {
                Log.Write(LogType.Error, LogComponent.E3Mbit, $"Bad offset2: {offset2}");
                return null;
            }

            // Check the Ethernet CRC
            byte[] byteArray = byteBuffer.ToArray();
            ushort crcVal = CalculateCRC(byteArray, byteArray.Length - 2);
            ushort readCrcVal = (ushort)((byteBuffer[byteBuffer.Count - 2] << 8) | byteBuffer[byteBuffer.Count - 1]);
            if (crcVal != readCrcVal)
            {
                Log.Write(LogType.Error, LogComponent.E3Mbit, "Bad CRC, {0:x} vs {1:x}", crcVal, readCrcVal);
                return null;
            }

            return byteArray;
        }

        // Generate CRC-16 for 3Mbit Ethernet
        // buf is sequence of words stored big-endian.
        ushort CalculateCRC(byte[] buf, int lengthInBytes)
        {
            ushort crc = 0x8005; // Due to the sync bit
            for (int index = 0; index < lengthInBytes; index++)
            {
                ushort data = (ushort)(buf[index] << 8);
                for (int i = 0; i < 8; i++)
                {
                    ushort xorFeedback = (ushort)((crc ^ data) & 0x8000); // Test upper bit
                    crc = (ushort)(crc << 1);
                    data = (ushort)(data << 1);
                    if (xorFeedback != 0)
                    {
                        crc ^= 0x8005; // CRC-16 polynomial constant
                    }
                }
            }

            return crc;
        }

        private IntPtr _sharedPruMemory;
        private ReceivedPacketCallback _routerCallback;
        private BeagleBoneLedController _ledController;

        // Interface between host and PRU
        // The idea is there are two buffers: r_ and w_.
        // Ownership is passed back and forth between the PRU and the ARM processor.
        // The PRU sends a signal whenever it gives a buffer back to the ARM.
        // "in" and "out" below are from the perspective of the PRU.
        //
        // This struct is here more for convenience of debugging than actual use in C#
        // since it's not really possible to map a C# object directly to volatile memory
        // in a way that I feel good about using.
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        struct PruInterfaceControlBlock
        {
            public UInt32 r_owner; // in
            public UInt32 r_max_length; // in, bytes
            public UInt32 r_received_length; // out, bytes
            public UInt32 r_buf; // in (pointer offset)
            public UInt32 r_truncated; // out, boolean
            public UInt32 r_status; // out
            public UInt32 w_owner; // in
            public UInt32 w_length; // bytes, in (buffer length)
            public UInt32 w_buf; // in (pointer offset)
            public UInt32 w_status; // out
        };

        const uint STATUS_INPUT_COMPLETE = (0 << 8);
        const uint STATUS_OUTPUT_COMPLETE = (1 << 8);
        const uint STATUS_INPUT_OVERRUN = (2 << 8);
        const uint STATUS_SOFTWARE_RESET = (5 << 8); // Internal only

        const uint STATUS_TRUNCATED      = 36; // Not part of real interface
        const uint STATUS_TIMING_ERROR   = 32; // Not part of real interface
        const uint STATUS_BIT_COLLISION  = 16;
        const uint STATUS_BIT_CRC_BAD    = 8; // unused
        const uint STATUS_BIT_ICMD       = 4; // unused
        const uint STATUS_BIT_OCMD       = 2; // unused
        const uint STATUS_BIT_INCOMPLETE = 1; // Not byte boundary

        const uint COMMAND_NONE = 0;
        const uint COMMAND_SEND = 1;
        const uint COMMAND_RECV = 2;
        const uint COMMAND_HALT = 3;

        const uint OWNER_ARM = 1;
        const uint OWNER_PRU = 2;

        const uint W_PTR_OFFSET = 0x400;
        const uint R_PTR_OFFSET = 0x10000;
        const uint MAX_SIZE = 12 * 1024;
    }

    public class BeagleBoneLedController
    {
        const string _ledPath = "/sys/class/leds/beaglebone:green:usr";

        public BeagleBoneLedController()
        {
            try
            {
                Init();
            }
            catch(Exception e)
            {
                Log.Write(LogType.Warning, LogComponent.E3Mbit, "Failed to intialize LEDs.  Error: {0}", e.Message);
            }
        }

        public void SetLed(int n, int brightness)
        {
            if (n >= 0 && n <= _ledStream?.Length)
            {
                _ledStream?[n]?.WriteLine($"{brightness}");
            }
        }

        public void BlinkLed(int n, int durationMsec)
        {
            SetLed(n, 1);
            Thread.Sleep(durationMsec);
            SetLed(n, 0);
        }

        private void Init()
        {
            _ledStream = new StreamWriter[4];

            for (int i = 0; i < 4; i++)
            {
                using (StreamWriter sw = new StreamWriter(Path.Combine(_ledPath + $"{i}", "trigger")))
                {
                    sw.WriteLine("none");
                }

                _ledStream[i] = new StreamWriter(Path.Combine(_ledPath + $"{i}", "brightness"));
                _ledStream[i].AutoFlush = true;
                SetLed(i, 0);
            }
        }

        private StreamWriter[] _ledStream;
    }

    /// <summary>
    /// Provides constants, structs, and functions needed to P/Invoke into prussdrv lib calls.
    /// </summary>
    public static class PRU
    {
        public const int NUM_PRU_HOSTIRQS   = 8;
        public const int NUM_PRU_HOSTS      = 10;
        public const int NUM_PRU_CHANNELS   = 10;
        public const int NUM_PRU_SYS_EVTS   = 64;

        public const uint PRUSS0_PRU0_DATARAM = 0;
        public const uint PRUSS0_PRU1_DATARAM = 1;
        public const uint PRUSS0_PRU0_IRAM  = 2;
        public const uint PRUSS0_PRU1_IRAM  = 3;

        public const uint PRUSS_V1          = 1; // AM18XX
        public const uint PRUSS_V2          = 2; // AM33XX

        //Available in AM33xx series - begin
        public const uint PRUSS0_SHARED_DATARAM = 4;
        public const uint PRUSS0_CFG        = 5;
        public const uint PRUSS0_UART       = 6;
        public const uint PRUSS0_IEP        = 7;
        public const uint PRUSS0_ECAP       = 8;
        public const uint PRUSS0_MII_RT     = 9;
        public const uint PRUSS0_MDIO       = 10;
        //Available in AM33xx series - end

        public const uint PRU_EVTOUT_0      = 0;
        public const uint PRU_EVTOUT_1      = 1;
        public const uint PRU_EVTOUT_2      = 2;
        public const uint PRU_EVTOUT_3      = 3;
        public const uint PRU_EVTOUT_4      = 4;
        public const uint PRU_EVTOUT_5      = 5;
        public const uint PRU_EVTOUT_6      = 6;
        public const uint PRU_EVTOUT_7      = 7;

        public const byte PRU0_PRU1_INTERRUPT = 17;
        public const byte PRU1_PRU0_INTERRUPT = 18;
        public const byte PRU0_ARM_INTERRUPT  = 19;
        public const byte PRU1_ARM_INTERRUPT  = 20;
        public const byte ARM_PRU0_INTERRUPT  = 21;
        public const byte ARM_PRU1_INTERRUPT  = 22;

        public const byte CHANNEL0 = 0;
        public const byte CHANNEL1 = 1;
        public const byte CHANNEL2 = 2;
        public const byte CHANNEL3 = 3;
        public const byte CHANNEL4 = 4;
        public const byte CHANNEL5 = 5;
        public const byte CHANNEL6 = 6;
        public const byte CHANNEL7 = 7;
        public const byte CHANNEL8 = 8;
        public const byte CHANNEL9 = 9;

        public const byte PRU0        = 0;
        public const byte PRU1        = 1;
        public const byte PRU_EVTOUT0 = 2;
        public const byte PRU_EVTOUT1 = 3;
        public const byte PRU_EVTOUT2 = 4;
        public const byte PRU_EVTOUT3 = 5;
        public const byte PRU_EVTOUT4 = 6;
        public const byte PRU_EVTOUT5 = 7;
        public const byte PRU_EVTOUT6 = 8;
        public const byte PRU_EVTOUT7 = 9;

        public const uint PRU0_HOSTEN_MASK        = 0x0001;
        public const uint PRU1_HOSTEN_MASK        = 0x0002;
        public const uint PRU_EVTOUT0_HOSTEN_MASK = 0x0004;
        public const uint PRU_EVTOUT1_HOSTEN_MASK = 0x0008;
        public const uint PRU_EVTOUT2_HOSTEN_MASK = 0x0010;
        public const uint PRU_EVTOUT3_HOSTEN_MASK = 0x0020;
        public const uint PRU_EVTOUT4_HOSTEN_MASK = 0x0040;
        public const uint PRU_EVTOUT5_HOSTEN_MASK = 0x0080;
        public const uint PRU_EVTOUT6_HOSTEN_MASK = 0x0100;
        public const uint PRU_EVTOUT7_HOSTEN_MASK = 0x0200;

        public struct tsysevt_to_channel_map
        {
            public tsysevt_to_channel_map(short s, short c)
            {
                sysevt = s;
                channel = c;
            }
            public short sysevt;
            public short channel;
        }
        
        public struct tchannel_to_host_map
        {
            public tchannel_to_host_map(short c, short h)
            {
                channel = c;
                host = h;
            }
            public short channel;
            public short host;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public struct tpruss_intc_initdata
        {
            //Enabled SYSEVTs - Range:0..63
            //{-1} indicates end of list
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = NUM_PRU_SYS_EVTS)]
            public byte[] sysevts_enabled;

            //SysEvt to Channel map. SYSEVTs - Range:0..63 Channels -Range: 0..9
            //{-1, -1} indicates end of list
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = NUM_PRU_SYS_EVTS)]
            public tsysevt_to_channel_map[] sysevt_to_channel_map;

            //Channel to Host map.Channels -Range: 0..9  HOSTs - Range:0..9
            //{-1, -1} indicates end of list
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = NUM_PRU_CHANNELS)]
            public tchannel_to_host_map[] channel_to_host_map;

            //10-bit mask - Enable Host0-Host9 {Host0/1:PRU0/1, Host2..9 : PRUEVT_OUT0..7}
            public UInt32 host_enable_bitmask;
        }

        [DllImport("prussdrv")]
        public static extern int prussdrv_init();

        [DllImport("prussdrv")]
        public static extern int prussdrv_open(UInt32 host_interrupt);

        [DllImport("prussdrv")]
        public static extern int prussdrv_pruintc_init(ref tpruss_intc_initdata prussintc_init_data);

        [DllImport("prussdrv")]
        public static extern int prussdrv_load_datafile(int prunum, [MarshalAs(UnmanagedType.LPStr)] string filename);

        [DllImport("prussdrv")]
        public static extern int prussdrv_exec_program(int prunum, [MarshalAs(UnmanagedType.LPStr)] string filename);

        [DllImport("prussdrv")]
        public static extern int prussdrv_map_prumem(UInt32 pru_ram_id, out IntPtr address);

        [DllImport("prussdrv")]
        public static extern int prussdrv_pru_wait_event(UInt32 host_interrupt);

        [DllImport("prussdrv")]
        public static extern int prussdrv_pru_clear_event(UInt32 host_interrupt, UInt32 sysevent);
    }
}
