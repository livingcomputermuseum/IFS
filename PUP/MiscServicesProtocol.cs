using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IFS
{
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

                default:
                    // Unhandled, TODO: log it
                    break;
            }
        }


        private void SendStringTimeReply(PUP p)
        {
            //
            // From the spec, the response is:
            // "A string consisting of the current date and time in the form
            // '11-SEP-75 15:44:25'"
            //
            // It makes no mention of timezone or DST, so I am assuming local time here.
            // Good enough for government work.
            //
            DateTime currentTime = DateTime.Now;            

            BCPLString bcplDateString = new BCPLString(currentTime.ToString("dd-MMM-yy HH:mm:ss"));
            
            PUPPort localPort = new PUPPort(DirectoryServices.Instance.LocalHostAddress, p.SourcePort.Socket);
            PUP response = new PUP(PupType.StringTimeReply, p.ID, p.SourcePort, localPort, bcplDateString.ToArray());

            Dispatcher.Instance.SendPup(response);
        }

        private void SendAltoTimeReply(PUP p)
        {
            //
            // From the spec, the response is:
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

            // So the Alto epoch is 1/1/1901.  For the time being to keep things simple we're assuming
            // GMT and no DST at all.  TODO: make this take into account our TZ, etc.
            //
            DateTime currentTime = DateTime.Now;

            // The epoch for .NET is 1/1/0001 at 12 midnight and is counted in 100-ns intervals.
            // Some conversion is needed, is what I'm saying.
            DateTime altoEpoch = new DateTime(1901, 1, 1);
            TimeSpan timeSinceAltoEpoch = new TimeSpan(currentTime.Ticks - altoEpoch.Ticks);

            UInt32 altoTime = (UInt32)timeSinceAltoEpoch.TotalSeconds;

            // Build the response data
            byte[] data = new byte[10];
            Helpers.WriteUInt(ref data, altoTime, 0);
            Helpers.WriteUShort(ref data, 0, 4);        // Timezone, hardcoded to GMT
            Helpers.WriteUShort(ref data, 366, 6);      // DST info, not used right now.
            Helpers.WriteUShort(ref data, 366, 8);

            PUPPort localPort = new PUPPort(DirectoryServices.Instance.LocalHostAddress, p.SourcePort.Socket);
            PUP response = new PUP(PupType.AltoTimeResponse, p.ID, p.SourcePort, localPort, data);

            Dispatcher.Instance.SendPup(response);
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
            string hostName = DirectoryServices.Instance.AddressLookup(lookupAddress);

            if (!String.IsNullOrEmpty(hostName))
            {
                // We have a result, pack the name into the response.
                BCPLString interNetworkName = new BCPLString(hostName);
                PUPPort localPort = new PUPPort(DirectoryServices.Instance.LocalHostAddress, p.SourcePort.Socket);
                PUP lookupReply = new PUP(PupType.AddressLookupResponse, p.ID, p.SourcePort, localPort, interNetworkName.ToArray());

                Dispatcher.Instance.SendPup(lookupReply);
            }
            else
            {
                // Unknown host, send an error reply
                BCPLString errorString = new BCPLString("Unknown host for address.");
                PUPPort localPort = new PUPPort(DirectoryServices.Instance.LocalHostAddress, p.SourcePort.Socket);
                PUP errorReply = new PUP(PupType.DirectoryLookupErrorReply, p.ID, p.SourcePort, localPort, errorString.ToArray());

                Dispatcher.Instance.SendPup(errorReply);
            }
        }

        private void SendNameLookupReply(PUP p)
        {
            //
            // For the request PUP:
            //  A string consisting of an inter-network name expression
            //
            // Response:
            //  One or more 6-byte blocks containing the address(es) corresponding to the
            //  name expression.  Each block is a Pup Port structure, with the network and host numbers in
            //  the first two bytes and the socket number in the last four bytes.
            //

            //
            // I'm not sure what would cause a name to resolve to multiple addresses at this time.
            // Also still not sure what an 'inter-network name expression' is.
            // As above, assuming this is a simple hostname.
            //
            BCPLString lookupName = new BCPLString(p.Contents);

            HostAddress address = DirectoryServices.Instance.NameLookup(lookupName.ToString());

            if (address != null)
            {
                // We found an address, pack the port into the response.
                PUPPort lookupPort = new PUPPort(address, 0);
                PUPPort localPort = new PUPPort(DirectoryServices.Instance.LocalHostAddress, p.SourcePort.Socket);
                PUP lookupReply = new PUP(PupType.NameLookupResponse, p.ID, p.SourcePort, localPort, lookupPort.ToArray());

                Dispatcher.Instance.SendPup(lookupReply);
            }
            else
            {
                // Unknown host, send an error reply
                BCPLString errorString = new BCPLString("Unknown host for name.");
                PUPPort localPort = new PUPPort(DirectoryServices.Instance.LocalHostAddress, p.SourcePort.Socket);
                PUP errorReply = new PUP(PupType.DirectoryLookupErrorReply, p.ID, p.SourcePort, localPort, errorString.ToArray());

                Dispatcher.Instance.SendPup(errorReply);
            }
        }
    }
}
