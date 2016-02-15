using IFS.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IFS
{
    //
    // From the spec, the AltoTime response is:
    // "10 bytes in all, organized as 5 16-bit words:
    //    words 0, 1    Present date and time: a 32-bit integer representing number of
    //                  seconds since midnight, January 1, 1901, Greenwich Mean Time (GMT).
    //    
    //    word 2        Local time zone information.  Bit 0 is zero if west of Greenwich
    //                  and one if east.  Bits 1-7 are the number of hours east or west of
    //                  Greenwich.  Bits 8-15 are an additional number of minutes.
    // 
    //    word 3        Day of the year on or before which Daylight Savings Time takes
    //                  effect locally, where 1 = January 1 and 366 = Dcember 31.  (The
    //                  actual day is the next preceding Sunday.)
    //
    //    word 4        Day of the year on or before which Daylight Savings Time ends.  If
    //                  Daylight Savings Time is not observed locally, both the start and
    //                  end dates should be 366.
    //
    //    The local time parameters in words 2 and 4 are those in effect at the server's 
    //    location.
    //            
    struct AltoTime
    {
        public uint DateTime;
        public ushort TimeZone;
        public ushort DSTStart;
        public ushort DSTEnd;
    }

    /// <summary>
    /// Implements PUP Miscellaneous Services (see miscSvcsProto.pdf)
    /// which include:
    ///   - Date and Time services
    ///   - Mail check
    ///   - Network Directory Lookup
    ///   - Alto Boot protocols
    ///   - Authenticate/Validate
    /// </summary>
    public class MiscServicesProtocol : PUPProtocolBase
    {
        public MiscServicesProtocol()
        {
            // TODO:
            // load host tables, etc.
        }

        /// <summary>
        /// Called by dispatcher to send incoming data destined for this protocol
        /// </summary>
        /// <param name="p"></param>
        public override void RecvData(PUP p)
        {
            Log.Write(LogType.Verbose, LogComponent.MiscServices, String.Format("Misc. protocol request is for {0}.", p.Type));
            switch (p.Type)
            {
                case PupType.StringTimeRequest:
                    SendStringTimeReply(p);
                    break;

                case PupType.AltoTimeRequest:
                    SendAltoTimeReply(p);
                    break;

                case PupType.AddressLookupRequest:
                    SendAddressLookupReply(p);
                    break;

                case PupType.NameLookupRequest:
                    SendNameLookupReply(p);
                    break;

                case PupType.SendBootFileRequest:
                    SendBootFile(p);
                    break;           

                default:
                    Log.Write(LogComponent.MiscServices, String.Format("Unhandled misc. protocol {0}", p.Type));
                    break;
            }
        }


        private void SendStringTimeReply(PUP p)
        {
            //
            // From the spec, the response is:
            // "A string consisting of the current date and time in the form
            // '11-SEP-75 15:44:25'"
            // NOTE: This is *not* a BCPL string, just the raw characters.
            //
            // It makes no mention of timezone or DST, so I am assuming local time here.
            // Good enough for government work.
            //
            DateTime currentTime = DateTime.Now;

            byte[] timeString = Helpers.StringToArray(currentTime.ToString("dd-MMM-yy HH:mm:ss"));            
            
            PUPPort localPort = new PUPPort(DirectoryServices.Instance.LocalHostAddress, p.SourcePort.Socket);
            PUP response = new PUP(PupType.StringTimeReply, p.ID, p.SourcePort, localPort, timeString);

            PUPProtocolDispatcher.Instance.SendPup(response);
        }

        private void SendAltoTimeReply(PUP p)
        {            
            // So the Alto epoch is 1/1/1901.  For the time being to keep things simple we're assuming
            // GMT and no DST at all.  TODO: make this take into account our TZ, etc.
            //
            // Additionally:  While certain routines seem to be Y2K compliant (the time requests made from
            // the Alto's "puptest" diagnostic, for example), the Executive is not.  To keep things happy,
            // we move things back 28 years so that the calendar at least matches up.
            //
            DateTime currentTime =
                new DateTime(
                    DateTime.Now.Year - 28,
                    DateTime.Now.Month,
                    DateTime.Now.Day,
                    DateTime.Now.Hour,
                    DateTime.Now.Minute,
                    DateTime.Now.Second);

            // The epoch for .NET is 1/1/0001 at 12 midnight and is counted in 100-ns intervals.
            // Some conversion is needed, is what I'm saying.            
            DateTime altoEpoch = new DateTime(1901, 1, 1);            
                        
            TimeSpan timeSinceAltoEpoch = new TimeSpan(currentTime.Ticks - altoEpoch.Ticks);

            UInt32 altoTime = (UInt32)timeSinceAltoEpoch.TotalSeconds;

            // Build the response data
            AltoTime time = new AltoTime();
            time.DateTime = altoTime;
            time.TimeZone = 0;      // Hardcoded to GMT
            time.DSTStart = 366;    // DST not specified yet
            time.DSTEnd = 366;            

            PUPPort localPort = new PUPPort(DirectoryServices.Instance.LocalHostAddress,  p.DestinationPort.Socket);

            // Response must contain our network number; this is used to tell clients what network they're on if they don't already know.
            PUPPort remotePort = new PUPPort(DirectoryServices.Instance.LocalNetwork, p.SourcePort.Host, p.SourcePort.Socket);

            PUP response = new PUP(PupType.AltoTimeResponse, p.ID, remotePort, localPort, Serializer.Serialize(time));

            PUPProtocolDispatcher.Instance.SendPup(response);
        }

        private void SendAddressLookupReply(PUP p)
        {
            //
            // Need to find more... useful documentation, but here's what I have:
            // For the request PUP:
            //  A port (6 bytes).
            // 
            // Response:
            //  A string consisting of an inter-network name expression that matches the request Port.
            //

            //
            // I am at this time unsure what exactly an "inter-network name expression" consists of.
            // Empirically, a simple string name seems to make the Alto happy.
            //

            //
            // The request PUP contains a port address, we will check the host and network (and ignore the socket).
            // and see if we have a match.
            //
            PUPPort lookupAddress = new PUPPort(p.Contents, 0);
            string hostName = DirectoryServices.Instance.AddressLookup(new HostAddress(lookupAddress.Network, lookupAddress.Host));

            if (!String.IsNullOrEmpty(hostName))
            {
                // We have a result, pack the name into the response.
                // NOTE: This is *not* a BCPL string, just the raw characters.
                byte[] interNetworkName = Helpers.StringToArray(hostName);
                
                PUPPort localPort = new PUPPort(DirectoryServices.Instance.LocalHostAddress, p.SourcePort.Socket);
                PUP lookupReply = new PUP(PupType.AddressLookupResponse, p.ID, p.SourcePort, localPort, interNetworkName);

                PUPProtocolDispatcher.Instance.SendPup(lookupReply);
            }
            else
            {
                // Unknown host, send an error reply
                string errorString = "Unknown host.";
                PUPPort localPort = new PUPPort(DirectoryServices.Instance.LocalHostAddress, p.SourcePort.Socket);
                PUP errorReply = new PUP(PupType.DirectoryLookupErrorReply, p.ID, p.SourcePort, localPort, Helpers.StringToArray(errorString));

                PUPProtocolDispatcher.Instance.SendPup(errorReply);
            }
        }

        private void SendNameLookupReply(PUP p)
        {
            //
            // For the request PUP:
            //  A string consisting of an inter-network name expression.      
            //  NOTE: This is *not* a BCPL string, just the raw characters.      
            //
            // Response:
            //  One or more 6-byte blocks containing the address(es) corresponding to the
            //  name expression.  Each block is a Pup Port structure, with the network and host numbers in
            //  the first two bytes and the socket number in the last four bytes.
            //

            //
            // For now, the assumption is that each name maps to at most one address.
            //
            string lookupName = Helpers.ArrayToString(p.Contents);

            HostAddress address = DirectoryServices.Instance.NameLookup(lookupName);

            if (address != null)
            {
                // We found an address, pack the port into the response.
                PUPPort lookupPort = new PUPPort(address, 0);
                PUPPort localPort = new PUPPort(DirectoryServices.Instance.LocalHostAddress, p.SourcePort.Socket);
                PUP lookupReply = new PUP(PupType.NameLookupResponse, p.ID, p.SourcePort, localPort, lookupPort.ToArray());

                PUPProtocolDispatcher.Instance.SendPup(lookupReply);
            }
            else
            {
                // Unknown host, send an error reply
                string errorString = "Unknown host.";
                PUPPort localPort = new PUPPort(DirectoryServices.Instance.LocalHostAddress, p.SourcePort.Socket);
                PUP errorReply = new PUP(PupType.DirectoryLookupErrorReply, p.ID, p.SourcePort, localPort, Helpers.StringToArray(errorString));

                PUPProtocolDispatcher.Instance.SendPup(errorReply);
            }
        }

        private void SendBootFile(PUP p)
        {
            //
            // The request PUP contains the file number in the lower-order 16-bits of the pup ID.
            // Assuming the number is a valid bootfile, we start sending it to the client's port via EFTP.
            // 
            uint fileNumber = p.ID & 0xffff;

            Log.Write(LogType.Verbose, LogComponent.MiscServices, "Boot file request is for file {0}.", fileNumber);


        }
    }
}
