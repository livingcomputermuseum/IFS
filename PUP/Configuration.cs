using IFS.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace IFS
{

    public class InvalidConfigurationException : Exception
    {
        public InvalidConfigurationException(string message) : base(message)
        {

        }
    }


    /// <summary>
    /// Encapsulates global server configuration information.
    /// </summary>
    public static class Configuration
    {
        static Configuration()
        {
            ReadConfiguration();

            //
            // Ensure that required values were read from the config file.  If not,
            // throw so that startup is aborted.
            //
            if (string.IsNullOrWhiteSpace(FTPRoot) || !Directory.Exists(FTPRoot))
            {
                throw new InvalidConfigurationException("FTP root path is invalid.");
            }

            if (string.IsNullOrWhiteSpace(CopyDiskRoot) || !Directory.Exists(CopyDiskRoot))
            {
                throw new InvalidConfigurationException("CopyDisk root path is invalid.");
            }

            if (string.IsNullOrWhiteSpace(BootRoot) || !Directory.Exists(BootRoot))
            {
                throw new InvalidConfigurationException("Boot root path is invalid.");
            }

            if (MaxWorkers < 1)
            {
                throw new InvalidConfigurationException("MaxWorkers must be >= 1.");
            }
        }

        /// <summary>
        /// The type of interface (UDP or RAW) to communicate over
        /// </summary>
        public static readonly string InterfaceType;

        /// <summary>
        /// The name of the network interface to use
        /// </summary>
        public static readonly string InterfaceName;

        /// <summary>
        /// The network that this server lives on
        /// </summary>
        public static readonly int ServerNetwork;

        /// <summary>
        /// The host number for this server.
        /// </summary>
        public static readonly int ServerHost;

        /// <summary>
        /// The root directory for the FTP file store.
        /// </summary>
        public static readonly string FTPRoot;

        /// <summary>
        /// The root directory for the CopyDisk file store.
        /// </summary>
        public static readonly string CopyDiskRoot;

        /// <summary>
        /// The root directory for the Boot file store.
        /// </summary>
        public static readonly string BootRoot;

        /// <summary>
        /// The maximum number of worker threads for protocol handling.
        /// </summary>
        public static readonly int MaxWorkers = 256;

        /// <summary>
        /// The components to display logging messages for.
        /// </summary>
        public static readonly LogComponent LogComponents;

        /// <summary>
        /// The type (Verbosity) of messages to log.
        /// </summary>
        public static readonly LogType LogTypes;


        private static void ReadConfiguration()
        {
            using (StreamReader configStream = new StreamReader(Path.Combine("Conf", "ifs.cfg")))
            {
                //
                // Config file consists of text lines containing name / value pairs:
                //      <Name>=<Value>
                // Whitespace is ignored.
                //
                int lineNumber = 0;
                while (!configStream.EndOfStream)
                {
                    lineNumber++;
                    string line = configStream.ReadLine().Trim();

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

                    // Find the '=' separating tokens and ensure there are just two.
                    string[] tokens = line.Split(new char[] { '=' }, StringSplitOptions.RemoveEmptyEntries);

                    if (tokens.Length < 2)
                    {
                        Log.Write(LogType.Warning, LogComponent.Configuration,
                            "ifs.cfg line {0}: Invalid syntax.", lineNumber);
                        continue;
                    }

                    string parameter = tokens[0].Trim();
                    string value = tokens[1].Trim();

                    // Reflect over the public, static properties in this class and see if the parameter matches one of them
                    // If not, it's an error, if it is then we attempt to coerce the value to the correct type.
                    System.Reflection.FieldInfo[] info = typeof(Configuration).GetFields(BindingFlags.Public | BindingFlags.Static);

                    bool bMatch = false;
                    foreach (FieldInfo field in info)
                    {
                        // Case-insensitive compare.
                        if (field.Name.ToLowerInvariant() == parameter.ToLowerInvariant())
                        {
                            bMatch = true;

                            //
                            // Switch on the type of the field and attempt to convert the value to the appropriate type.
                            // At this time we support only strings and integers.
                            //
                            try
                            {
                                switch (field.FieldType.Name)
                                {
                                    case "Int32":
                                        {
                                            int v = int.Parse(value);
                                            field.SetValue(null, v);
                                        }
                                        break;

                                    case "String":
                                        {
                                            field.SetValue(null, value);
                                        }
                                        break;

                                    case "LogType":
                                        {
                                            field.SetValue(null, Enum.Parse(typeof(LogType), value, true));
                                        }
                                        break;

                                    case "LogComponent":
                                        {
                                            field.SetValue(null, Enum.Parse(typeof(LogComponent), value, true));
                                        }
                                        break;

                                }
                            }
                            catch
                            {
                                Log.Write(LogType.Warning, LogComponent.Configuration,
                                    "ifs.cfg line {0}: Value '{1}' is invalid for parameter '{2}'.", lineNumber, value, parameter);
                            }
                        }

                    }


                    if (!bMatch)
                    {
                        Log.Write(LogType.Warning, LogComponent.Configuration,
                            "ifs.cfg line {0}: Unknown configuration parameter '{1}'.", lineNumber, parameter);
                    }
                }
            }                     
        }
    }
}
