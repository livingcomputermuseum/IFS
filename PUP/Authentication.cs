using IFS.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace IFS
{
    /// <summary>
    /// Provides very (very) rudimentary security.
    /// Exposes facilities for user authentication (via password) and
    /// access control.
    /// 
    /// Since all IFS transactions are done in plaintext, I don't want to expose real NTLM passwords,
    /// (or deal with the security issues that would entail)
    /// so IFS usernames/passwords are completely separate entities from Windows auth 
    /// and access is controlled very coarsely.  (More fine-grained ACLs are really overkill for the
    /// use-cases we need for IFS, at least at this time.)
    /// 
    /// Accounts are split into two categories:  Users and Administrators.
    /// Users can read any file, but can only write files in their home directory.
    /// Administrators can read/write files in any directory.
    /// 
    /// The concept of a "guest" account is provided -- this user has no home directory and has read-only
    /// access only to specifically marked public directories.
    /// </summary>
    public static class Authentication
    {
        static Authentication()
        {
            ReadAccountDatabase();
        }

        public static List<UserToken> EnumerateUsers()
        {
            return _accounts.Values.ToList<UserToken>();
        }

        public static UserToken GetUser(string userName)
        {
            if (_accounts.ContainsKey(userName))
            {
                return _accounts[userName];
            }
            else
            {
                return null;
            }
        }

        public static UserToken Authenticate(string userName, string password)
        {
            //
            // Look up the user
            //
            UserToken token = null;

            // 
            // Verify that the username's host/registry (if present) matches
            // our hostname.
            //
            if (ValidateUserRegistry(userName))
            {
                //
                // Strip off any host/registry on the username, lookup based on username only.
                //
                userName = GetUserNameFromFullName(userName);

                if (_accounts.ContainsKey(userName))
                {
                    UserToken accountToken = _accounts[userName];

                    //
                    // Account exists; compare password hash against the hash of the password provided.
                    // (If there is no hash then no password is set and we do no check.)
                    // 
                    if (!string.IsNullOrEmpty(accountToken.PasswordHash))
                    {
                        // Convert hash to base64 string and compare with actual password hash
                        if (ValidatePassword(accountToken, password))
                        {
                            // Yay!
                            token = accountToken;
                        }
                        else
                        {
                            // No match, password is incorrect.
                            token = null;
                        }
                    }
                    else if (string.IsNullOrEmpty(password))
                    {
                        // Just ensure both passwords are empty.
                        token = accountToken;
                    }
                }
            }

            return token;
        }

        public static bool AddUser(string userName, string password, string fullName, string homeDirectory, IFSPrivileges privileges)
        {
            bool bSuccess = false;

            if (!_accounts.ContainsKey(userName))
            {
                // Add the user to the database
                UserToken newUser = new UserToken(userName, String.Empty, fullName, homeDirectory, privileges);
                _accounts.Add(userName, newUser);

                // Set password (which has the side-effect of committing the database)
                bSuccess = SetPassword(userName, password);
            }

            return bSuccess;
        }

        public static bool RemoveUser(string userName)
        {
            if (_accounts.ContainsKey(userName))
            {
                _accounts.Remove(userName);
                WriteAccountDatabase();
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Changes the user's password.  This is intended to be executed from the IFS console or by
        /// an authenticated administrator.
        /// </summary>
        /// <param name="userName"></param>
        /// <param name="currentPassword"></param>
        /// <param name="newPassword"></param>
        /// <returns></returns>
        public static bool SetPassword(string userName, string newPassword)
        {
            bool bSuccess = false;

            //
            // Look up the user
            //
            if (_accounts.ContainsKey(userName))
            {
                UserToken accountToken = _accounts[userName];

                if (!string.IsNullOrEmpty(newPassword))
                {
                    // Calculate hash of new password                    
                    SHA1 sha = new SHA1CryptoServiceProvider();

                    byte[] passwordHash = sha.ComputeHash(Encoding.UTF8.GetBytes(newPassword));

                    // Convert hash to base64 string and compare with actual password hash
                    accountToken.PasswordHash = Convert.ToBase64String(passwordHash);
                }
                else
                {
                    // Just set an empty password.
                    accountToken.PasswordHash = String.Empty;
                }

                // Commit to accounts file
                WriteAccountDatabase();

                bSuccess = true;
            }

            return bSuccess;
        }

        /// <summary>
        /// Verifies whether the specified user account is registered.
        /// </summary>
        /// <param name="userName"></param>
        /// <returns></returns>
        public static bool UserExists(string userName)
        {
            return _accounts.ContainsKey(userName);
        }

        /// <summary>
        /// Given a full user name (i.e. username.HOST), validates that the
        /// HOST (or "registry") portion matches our hostname.
        /// </summary>
        /// <param name="fullUserName"></param>
        /// <returns></returns>
        public static bool ValidateUserRegistry(string fullUserName)
        {
            if (fullUserName.Contains("."))
            {
                // Strip off the host/registry name and compare to our hostname.
                string hostName = fullUserName.Substring(fullUserName.IndexOf(".") + 1);

                return hostName.ToLowerInvariant() == DirectoryServices.Instance.LocalHostName.ToLowerInvariant();
            }
            else
            {
                // No registry appended, we assume this is destined for us by default.
                return true;
            }
        }


        /// <summary>
        /// Given a full user name (i.e. username.HOST), returns only the username portion.
        /// (Given just a username, returns it unchanged.)
        /// </summary>
        /// <param name="fullUserName"></param>
        /// <returns></returns>
        public static string GetUserNameFromFullName(string fullUserName)
        {
            // If user name has a host/registry appended, we will strip it off.
            if (fullUserName.Contains("."))
            {
                return fullUserName.Substring(0, fullUserName.IndexOf("."));
            }
            else
            {
                return fullUserName;
            }
        }

        private static bool ValidatePassword(UserToken accountToken, string password)
        {
            // Convert to UTF-8 byte array
            byte[] passwordBytes = Encoding.UTF8.GetBytes(password);

            // And hash
            SHA1 sha = new SHA1CryptoServiceProvider();

            byte[] passwordHash = sha.ComputeHash(passwordBytes);

            // Convert hash to base64 string and compare with actual password hash
            return accountToken.PasswordHash == Convert.ToBase64String(passwordHash);
        }

        private static void WriteAccountDatabase()
        {
            using (StreamWriter accountStream = new StreamWriter(Path.Combine("Conf", "accounts.txt")))
            {
                accountStream.WriteLine("# accounts.txt:");
                accountStream.WriteLine("#");
                accountStream.WriteLine("# The format for a user account is:");
                accountStream.WriteLine("# <username>:<password hash>:[Admin|User]:<Full Name>:<home directory>");
                accountStream.WriteLine("#");

                foreach(UserToken account in _accounts.Values)
                {
                    accountStream.WriteLine(account.ToString());
                }
            }
        }

        private static void ReadAccountDatabase()
        {
            _accounts = new Dictionary<string, UserToken>();

            using (StreamReader accountStream = new StreamReader(Path.Combine("Conf", "accounts.txt")))
            {
                int lineNumber = 0;
                while (!accountStream.EndOfStream)
                {
                    lineNumber++;
                    string line = accountStream.ReadLine().Trim();

                    if (string.IsNullOrEmpty(line))
                    {
                        // Empty line, ignore.
                        continue;
                    }

                    if (line.StartsWith("#"))
                    {
                        // Comment to EOL, ignore.
                        continue;
                    }

                    // Each entry is of the format:
                    // <username>:<password hash>:[Admin|User]:<Full Name>:<home directory>
                    //
                    // Find the ':' separating tokens and ensure there are exactly five
                    string[] tokens = line.Split(new char[] { ':' }, StringSplitOptions.None);

                    if (tokens.Length != 5)
                    {
                        Log.Write(LogType.Warning, LogComponent.Configuration,
                            "accounts.txt line {0}: Invalid syntax.", lineNumber);
                    }

                    IFSPrivileges privs;

                    switch(tokens[2].ToLowerInvariant())
                    {
                        case "admin":
                            privs = IFSPrivileges.ReadWrite;
                            break;

                        case "user":
                            privs = IFSPrivileges.ReadOnly;
                            break;

                        default:
                            Log.Write(LogType.Warning, LogComponent.Configuration,
                                "accounts.txt line {0}: Invalid account type '{1}'.", lineNumber, tokens[2]);
                            continue;                            
                    }

                    UserToken token =
                        new UserToken(
                            tokens[0],
                            tokens[1],
                            tokens[3],
                            tokens[4],
                            privs);

                    if (_accounts.ContainsKey(tokens[0].ToLowerInvariant()))
                    {
                        Log.Write(LogType.Warning, LogComponent.Configuration,
                            "accounts.txt line {0}: Duplicate user entry for '{1}'.", lineNumber, tokens[0]);
                        continue;
                    }
                    else
                    {
                        _accounts.Add(tokens[0].ToLowerInvariant(), token);
                    }
                }
            }
        }

        private static Dictionary<string, UserToken> _accounts;
    }

    public enum IFSPrivileges
    {
        ReadOnly = 0,           // Read-only except home directory
        ReadWrite               // Read/write everywhere
    }

    public class UserToken
    {
        public UserToken(string userName, string passwordHash, string fullName, string homeDirectory, IFSPrivileges privileges)
        {
            UserName = userName;
            PasswordHash = passwordHash;
            FullName = fullName;
            HomeDirectory = homeDirectory;
            Privileges = privileges;
        }

        public override string ToString()
        {
            return String.Format("{0}:{1}:{2}:{3}:{4}",
                UserName,
                PasswordHash,
                Privileges == IFSPrivileges.ReadOnly ? "User" : "Admin",
                FullName,
                HomeDirectory);
        }

        public static UserToken Guest = new UserToken("guest", String.Empty, "No one", String.Empty, IFSPrivileges.ReadOnly);

        public string UserName;
        public string PasswordHash;
        public string FullName;
        public string HomeDirectory;        
        public IFSPrivileges Privileges;
    }
}
