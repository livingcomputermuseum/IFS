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

using IFS.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IFS.Mail
{
    /// <summary>
    /// MailManager implements the filesystem-based portions of the mail system.
    /// (The network transport portions are lumped in with the FTP Server).
    /// 
    /// It provides methods for retrieving, deleting, and storing mail to/from
    /// a specific mailbox.
    /// 
    /// Mailboxes in our implementation are provided for each user account.  There
    /// is one mailbox per user, and the mailbox name is the same as the user's login
    /// name.  Authentication is handled as one would expect -- each user has read/delete 
    /// access only to his/her own mailbox, all other mailboxes can only be sent to.
    /// 
    /// Mailbox directories are lazy-init -- they're only created when a user first receives an
    /// e-mail.
    /// 
    /// The guest account has its own mailbox, shared by all guest users.
    /// 
    /// Each user's mailbox is stored in a subdirectory of the Mail directory.
    /// Rather than keeping a single mail file that must be appended and maintained, each
    /// mail that is added to a mailbox is an individual text file.
    /// 
    /// This class does no authentication, it merely handles the tedious chore of managing
    /// users' mailboxes.  (See the FTP server, where auth takes place.)
    /// </summary>
    public static class MailManager
    {
        static MailManager()
        {

        }

        /// <summary>
        /// Retrieves a list of mail files for the specified mailbox.
        /// These are not full paths, and are relative to the mailbox in question.
        /// Use RetrieveMail to retrieve an individual mail.
        /// </summary>
        /// <param name="mailbox"></param>
        /// <returns></returns>
        public static IEnumerable<string> EnumerateMail(string mailbox)
        {
            if (Directory.Exists(GetMailboxPath(mailbox)))
            {
                // Get the mail files in this directory
                return Directory.EnumerateFiles(GetMailboxPath(mailbox), "*.mail", SearchOption.TopDirectoryOnly);
            }
            else
            {
                // No mail by default, the mailbox does not exist at this time.
                return null;
            }
        }

        /// <summary>
        /// Retrieves a stream for the given mail file in the specified mailbox.
        /// </summary>
        /// <param name="mailbox"></param>
        /// <param name="mailFile"></param>
        /// <returns></returns>
        public static Stream RetrieveMail(string mailbox, string mailFile)
        {
            if (File.Exists(GetMailboxPathForFile(mailbox, mailFile)))
            {
                //
                // Open the requested mail file.
                //
                return new FileStream(GetMailboxPathForFile(mailbox, mailFile), FileMode.Open, FileAccess.Read);
            }
            else
            {
                // This shouldn't normally happen, but we handle it gracefully if it does.
                Log.Write(LogType.Verbose, LogComponent.Mail, "Specified mail file {0} does not exist in mailbox {1}.", mailFile, mailbox);
                return null;
            }
        }

        public static string GetReceivedTime(string mailbox, string mailFile)
        {            
            if (File.Exists(GetMailboxPathForFile(mailbox, mailFile)))
            {
                //
                // Get the timestamp for the mail file
                //
                return "26-MAY-79 02:33:00";

                // TODO: need to adjust real date; as per a lot of Alto utilities they don't like
                // dates too far past the 70s...
                //return File.GetCreationTime(GetMailboxPathForFile(mailbox, mailFile)).ToString("dd-MMM-yy HH:mm:ss");
            }
            else
            {
                // This shouldn't normally happen, but we handle it gracefully if it does.
                Log.Write(LogType.Verbose, LogComponent.Mail, "Specified mail file {0} does not exist in mailbox {1}.", mailFile, mailbox);
                return String.Empty;
            }
        }

        /// <summary>
        /// Deletes the specified mail file from the specified mailbox.
        /// </summary>
        /// <param name="mailbox"></param>
        /// <param name="mailFile"></param>
        public static void DeleteMail(string mailbox, string mailFile)
        {
            if (File.Exists(GetMailboxPathForFile(mailbox, mailFile)))
            {
                //
                // Delete the requested mail file.
                //
                File.Delete(GetMailboxPathForFile(mailbox, mailFile));
            }
            else
            {
                // This shouldn't normally happen, but we handle it gracefully if it does.
                Log.Write(LogType.Verbose, LogComponent.Mail, "Specified mail file {0} does not exist in mailbox {1}.", mailFile, mailbox);                
            }
        }

        public static Stream StoreMail(string mailbox)
        {
            string newMailFile = Path.GetRandomFileName() + ".mail";

            //
            // Create the user's mail directory if it doesn't already exist.
            //
            if (!Directory.Exists(GetMailboxPath(mailbox)))
            {
                Directory.CreateDirectory(GetMailboxPath(mailbox));
            }

            //
            // Create the new mail file.
            //
            return new FileStream(GetMailboxPathForFile(mailbox, newMailFile), FileMode.CreateNew, FileAccess.ReadWrite);
        }

        private static string GetMailboxPath(string mailbox)
        {
            return Path.Combine(Configuration.MailRoot, mailbox);
        }

        private static string GetMailboxPathForFile(string mailbox, string mailFile)
        {
            return Path.Combine(Configuration.MailRoot, mailbox, mailFile);
        }
    }
}
