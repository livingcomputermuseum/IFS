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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IFS.IfsConsole
{
    public class ConsoleCommands
    {
        private ConsoleCommands()
        {

        }

        public static ConsoleCommands Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ConsoleCommands();
                }

                return _instance;
            }
        }

        [ConsoleFunction("show users", "Displays the current user database")]
        private bool ShowUsers()
        {
            foreach(UserToken user in Authentication.EnumerateUsers())
            {
                Console.WriteLine("{0}: ({1}) - {2},{3}", user.UserName, user.Privileges, user.FullName, user.HomeDirectory);
            }

            return false;
        }

        [ConsoleFunction("show user", "Displays information for the specified user", "<username>")]
        private bool ShowUser(string username)
        {
            UserToken user = Authentication.GetUser(username);

            if (user == null)
            {
                Console.WriteLine("User '{0}' does not exist.", username);
            }
            else
            {
                Console.WriteLine("{0}: ({1}) - {2},{3}", user.UserName, user.Privileges, user.FullName, user.HomeDirectory);
            }

            return false;
        }

        [ConsoleFunction("set password", "Sets the password for the specified user", "<username>")]
        private bool SetPassword(string username)
        {
            UserToken user = Authentication.GetUser(username);

            if (user == null)
            {
                Console.WriteLine("User '{0}' does not exist.", username);
            }
            else
            {
                Console.Write("Enter new password:");
                string newPassword = Console.ReadLine();

                Console.Write("Confirm new password:");
                string confPassword = Console.ReadLine();

                if (newPassword != confPassword)
                {
                    Console.WriteLine("Passwords do not match, password not changed.");
                }
                else
                {
                    Authentication.SetPassword(username, newPassword);
                }
            }

            return false;
        }

        [ConsoleFunction("add user", "Adds a new user account", "<username> <password> [User|Admin] <full name> <home directory>")]
        private bool AddUser(string username, string newPassword, string privileges, string fullName, string homeDir)
        {
            IFSPrivileges privs = IFSPrivileges.ReadOnly;

            switch(privileges.ToLowerInvariant())
            {
                case "user":
                    privs = IFSPrivileges.ReadOnly;
                    break;

                case "admin":
                    privs = IFSPrivileges.ReadWrite;
                    break;
            }

            if (Authentication.AddUser(username, newPassword, fullName, homeDir, privs))
            {
                Console.WriteLine("User added.");
            }
            else
            {
                Console.WriteLine("User already exists.");
            }

            return false;
        }

        [ConsoleFunction("remove user", "Removes an existing user account", "<username>")]
        private bool RemoveUser(string username)
        {
            if (Authentication.RemoveUser(username))
            {
                Console.WriteLine("User removed.");
            }
            else
            {
                Console.WriteLine("User could not be removed.");
            }

            return false;
        }

        [ConsoleFunction("show active servers", "Displays active server statistics.")]
        private bool ShowServers()
        {
            List<BSP.BSPWorkerBase> workers = BSP.BSPManager.EnumerateActiveWorkers();

            Console.WriteLine("BSP Channels:");
            foreach(BSP.BSPWorkerBase w in workers)
            {
                Console.WriteLine("{0} - Client port {1}, Server port {2}", w.GetType(), w.Channel.ClientPort, w.Channel.ServerPort);
            }

            IEnumerable<EFTP.EFTPChannel> channels = EFTP.EFTPManager.EnumerateActiveChannels();

            Console.WriteLine("EFTP Channels:");
            foreach (EFTP.EFTPChannel c in channels)
            {
                Console.WriteLine("EFTP - Client port {0}, Server port {1}", c.ClientPort, c.ServerPort);
            }

            return false;
        }

        [ConsoleFunction("quit", "Terminates the IFS process")]
        private bool Quit()
        {            
            return true;
        }

        private static ConsoleCommands _instance;
    }
}
