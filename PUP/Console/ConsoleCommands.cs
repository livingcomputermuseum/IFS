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
        private void ShowUsers()
        {
            foreach(UserToken user in Authentication.EnumerateUsers())
            {
                Console.WriteLine("{0}: ({1}) - {2},{3}", user.UserName, user.Privileges, user.FullName, user.HomeDirectory);
            }
        }

        [ConsoleFunction("show user", "Displays information for the specified user")]
        private void ShowUser(string username)
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
        }

        [ConsoleFunction("set password", "Sets the password for the specified user")]
        private void SetPassword(string username, string newPassword)
        {
            UserToken user = Authentication.GetUser(username);

            if (user == null)
            {
                Console.WriteLine("User '{0}' does not exist.", username);
            }
            else
            {
                Authentication.SetPassword(username, newPassword);
            }
        }

        [ConsoleFunction("add user", "Adds a new user", "<username> <password> [User|Admin] <full name> <home directory>")]
        private void AddUser(string username, string newPassword, string privileges, string fullName, string homeDir)
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
        }

        private static ConsoleCommands _instance;
    }
}
