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

using IFS.Boot;
using IFS.EFTP;
using IFS.Gateway;
using IFS.Logging;
using IFS.Mail;
using System;
using System.Collections.Generic;
using System.IO;
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

                case PupType.BootDirectoryRequest:
                    SendBootDirectory(p);
                    break;

                case PupType.AuthenticateRequest:
                    SendAuthenticationResponse(p);
                    break;

                case PupType.MailCheckRequestLaurel:
                    SendMailCheckResponse(p);
                    break;

                case PupType.MicrocodeRequest:
                    SendMicrocodeResponse(p);
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

            Router.Instance.SendPup(response);
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

            Router.Instance.SendPup(response);
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
                
                PUPPort localPort = new PUPPort(DirectoryServices.Instance.LocalHostAddress, p.DestinationPort.Socket);
                PUP lookupReply = new PUP(PupType.AddressLookupResponse, p.ID, p.SourcePort, localPort, interNetworkName);

                Router.Instance.SendPup(lookupReply);
            }
            else
            {
                // Unknown host, send an error reply
                string errorString = "Unknown host.";
                PUPPort localPort = new PUPPort(DirectoryServices.Instance.LocalHostAddress, p.DestinationPort.Socket);
                PUP errorReply = new PUP(PupType.DirectoryLookupErrorReply, p.ID, p.SourcePort, localPort, Helpers.StringToArray(errorString));

                Router.Instance.SendPup(errorReply);
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
            Log.Write(LogType.Verbose, LogComponent.MiscServices, "Name lookup is for '{0}'", lookupName);

            HostAddress address = DirectoryServices.Instance.NameLookup(lookupName);

            if (address != null)
            {
                // We found an address, pack the port into the response.
                PUPPort lookupPort = new PUPPort(address, 0);
                PUPPort localPort = new PUPPort(DirectoryServices.Instance.LocalHostAddress, p.DestinationPort.Socket);
                PUP lookupReply = new PUP(PupType.NameLookupResponse, p.ID, p.SourcePort, localPort, lookupPort.ToArray());

                Router.Instance.SendPup(lookupReply);

                Log.Write(LogType.Verbose, LogComponent.MiscServices, "Address is '{0}'", address);
            }
            else
            {
                // Unknown host, send an error reply
                string errorString = "Unknown host.";
                PUPPort localPort = new PUPPort(DirectoryServices.Instance.LocalHostAddress, p.DestinationPort.Socket);
                PUP errorReply = new PUP(PupType.DirectoryLookupErrorReply, p.ID, p.SourcePort, localPort, Helpers.StringToArray(errorString));

                Router.Instance.SendPup(errorReply);

                Log.Write(LogType.Verbose, LogComponent.MiscServices, "Host is unknown.");
            }
        }

        private void SendBootFile(PUP p)
        {
            //
            // The request PUP contains the file number in the lower-order 16-bits of the pup ID.
            // Assuming the number is a valid bootfile, we start sending it to the client's port via EFTP.
            // 
            ushort fileNumber = (ushort)p.ID;

            Log.Write(LogType.Verbose, LogComponent.MiscServices, "Boot file request is for file {0}.", fileNumber);

            FileStream bootFile = BootServer.GetStreamForNumber(fileNumber);

            if (bootFile == null)
            {
                Log.Write(LogType.Warning, LogComponent.MiscServices, "Boot file {0} does not exist or could not be opened.", fileNumber);
            }
            else
            {
                // Send the file.
                EFTPManager.SendFile(p.SourcePort, bootFile);
            }
        }

        private void SendBootDirectory(PUP p)
        {
            // 
            // From etherboot.bravo
            // "Pup ID: if it is in reply to a BootDirRequest, the ID should match the request.
            // Pup Contents: 1 or more blocks of the following format: A boot file number (the number that goes in the low 16 bits of a 
            // BootFileRequest Pup), an Alto format date (2 words), a boot file name in BCPL string format."
            //
            MemoryStream ms = new MemoryStream(PUP.MAX_PUP_SIZE);

            List<BootFileEntry> bootFiles = BootServer.EnumerateBootFiles();

            foreach(BootFileEntry entry in bootFiles)
            {
                BootDirectoryBlock block;
                block.FileNumber = entry.BootNumber;
                block.FileDate = 0;
                block.FileName = new BCPLString(entry.Filename);

                byte[] serialized = Serializer.Serialize(block);
                
                //
                // If this block fits into the current PUP, add it to the stream, otherwise send off the current PUP
                // and start a new one.
                // 
                if (serialized.Length + ms.Length <= PUP.MAX_PUP_SIZE)
                {
                    ms.Write(serialized, 0, serialized.Length);
                }
                else
                {
                    PUPPort localPort = new PUPPort(DirectoryServices.Instance.LocalHostAddress, p.DestinationPort.Socket);
                    PUP bootDirReply = new PUP(PupType.BootDirectoryReply, p.ID, p.SourcePort, localPort, ms.ToArray());
                    Router.Instance.SendPup(bootDirReply);

                    ms.Seek(0, SeekOrigin.Begin);
                    ms.SetLength(0);
                }
            }

            // Shuffle out any remaining data.
            if (ms.Length > 0)
            {
                PUPPort localPort = new PUPPort(DirectoryServices.Instance.LocalHostAddress, p.DestinationPort.Socket);
                PUP bootDirReply = new PUP(PupType.BootDirectoryReply, p.ID, p.SourcePort, localPort, ms.ToArray());
                Router.Instance.SendPup(bootDirReply);
            }

        }

        private void SendAuthenticationResponse(PUP p)
        {
            //
            // "Pup Contents: Two Mesa strings (more precisely StringBodys), packed in such a way that
            //  the maxLength of the first string may be used to locate the second string.  The first
            //  string is a user name and the second a password."
            //

            // I have chosen not to write a helper class encapsulating Mesa strings since this is the
            // first (and so far *only*) instance in which Mesa strings are used in IFS communications.
            //

            // Empirical analysis shows the format of a Mesa string to be:
            // Word 1: Length (bytes)
            // Word 2: MaxLength (bytes)
            // Byte 4 thru 4 + MaxLength: string data
            // data is padded to a word length.
            //
            string userName = Helpers.MesaArrayToString(p.Contents, 0);

            int passwordOffset = (userName.Length % 2) == 0 ? userName.Length : userName.Length + 1;
            string password = Helpers.MesaArrayToString(p.Contents, passwordOffset + 4);
            
            UserToken token = Authentication.Authenticate(userName, password);

            if (token == null)
            {
                string errorString = "Invalid username or password.";
                PUPPort localPort = new PUPPort(DirectoryServices.Instance.LocalHostAddress, p.DestinationPort.Socket);
                PUP errorReply = new PUP(PupType.AuthenticateNegativeResponse, p.ID, p.SourcePort, localPort, Helpers.StringToArray(errorString));

                Router.Instance.SendPup(errorReply);
            }
            else
            {
                // S'ok!
                PUPPort localPort = new PUPPort(DirectoryServices.Instance.LocalHostAddress, p.DestinationPort.Socket);
                PUP okReply = new PUP(PupType.AuthenticatePositiveResponse, p.ID, p.SourcePort, localPort, new byte[] { });

                Router.Instance.SendPup(okReply);
            }
        }

        private void SendMailCheckResponse(PUP p)
        {
            //
            // "Pup Contents: A string specifying the mailbox name."
            //

            //
            // See if there is any mail for the specified mailbox.
            //
            string mailboxName = Helpers.ArrayToString(p.Contents);

            //
            // If mailbox name has a host/registry appended, we will strip it off.
            // TODO: probably should validate host...
            //
            mailboxName = Authentication.GetUserNameFromFullName(mailboxName);            

            IEnumerable<string> mailList = MailManager.EnumerateMail(mailboxName);

            if (mailList == null || mailList.Count() == 0)
            {
                PUPPort localPort = new PUPPort(DirectoryServices.Instance.LocalHostAddress, p.DestinationPort.Socket);
                PUP noMailReply = new PUP(PupType.NoNewMailExistsReply, p.ID, p.SourcePort, localPort, new byte[] { });

                Router.Instance.SendPup(noMailReply);
            }
            else
            {
                PUPPort localPort = new PUPPort(DirectoryServices.Instance.LocalHostAddress, p.DestinationPort.Socket);
                PUP mailReply = new PUP(PupType.NewMailExistsReply, p.ID, p.SourcePort, localPort, Helpers.StringToArray("You've got mail!"));

                Router.Instance.SendPup(mailReply);
            }
        }

        private void SendMicrocodeResponse(PUP p)
        {
            //
            // The request PUP contains the file number in the lower-order 16-bits of the pup ID.
            // Assuming the number is a valid bootfile, we start sending it to the client's port via EFTP.
            // 
            ushort fileNumber = (ushort)p.ID;

            Log.Write(LogType.Verbose, LogComponent.MiscServices, "Microcode request is for file {0}.", fileNumber);

            FileStream microcodeFile = BootServer.GetStreamForNumber(fileNumber);

            if (microcodeFile == null)
            {
                Log.Write(LogType.Warning, LogComponent.MiscServices, "Microcode file {0} does not exist or could not be opened.", fileNumber);
            }
            else
            {
                // Send the file.  The MicrocodeReply protocol is extremely simple:
                // Just send a sequence of MicrocodeReply PUPs containing the microcode data,
                // there are no acks or flow control of any kind.
                Log.Write(LogType.Warning, LogComponent.MiscServices, "Sending microcode file {0}.", fileNumber);
                SendMicrocodeFile(p.SourcePort, microcodeFile);
            }
        }

        private void SendMicrocodeFile(PUPPort sourcePort, Stream microcodeFile)
        {
            //
            // "For version 1 of the protocol, a server willing to supply the data simply sends a sequence of packets
            // of type MicrocodeReply as fast as it can.  The high half of its pupID contains the version number(1)
            // and the low half of the pupID contains the packet sequence number. After all the data packets
            // have been sent, the server sends an empty (0 data bytes) packet for an end marker.  There are no
            // acknowledgments. This protocol is used by Dolphins and Dorados.
            // Currently, the version 1 servers send packets containing 3 * n words of data.  This constraint is imposed by the
            // Rev L Dolphin EPROM microcode.  I’d like to remove this restriction if I get a chance, so please don’t take
            // advantage of it unless you need to.The Rev L Dolphin EPROM also requires the second word of the source
            // socket to be 4. / HGM May - 80."
            //

            // TODO: this should happen in a worker thread.
            //

            //
            // We send 192 words of data per PUP (3 * 64) in an attempt to make the Dolphin happy.
            // We space these out a bit to give the D-machine time to keep up, we're much much faster than they are.
            //
            PUPPort localPort = new PUPPort(DirectoryServices.Instance.LocalHostAddress, 4);

            byte[] buffer = new byte[384];
            bool done = false;
            uint id = 0;

            while (!done)
            {
                int read = microcodeFile.Read(buffer, 0, buffer.Length);

                if (read < buffer.Length)
                {
                    done = true;
                }

                if (read > 0)
                {
                    PUP microcodeReply = new PUP(PupType.MicrocodeReply, (id | 0x00010000), sourcePort, localPort, buffer);
                    Router.Instance.SendPup(microcodeReply);
                }

                // Pause a bit to give the D0 time to breathe.
                System.Threading.Thread.Sleep(5);

                id++;
            }

            //
            // Send an empty packet to conclude the transfer.
            //
            PUP endReply = new PUP(PupType.MicrocodeReply, (id | 0x00010000), sourcePort, localPort, new byte[] { });
            Router.Instance.SendPup(endReply);

            Log.Write(LogType.Warning, LogComponent.MiscServices, "Microcode file sent.");

        }

        private struct BootDirectoryBlock
        {
            public ushort FileNumber;
            public UInt32 FileDate;
            public BCPLString FileName;
        }

    }
}
