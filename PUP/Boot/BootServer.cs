using IFS.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IFS.Boot
{
    public struct BootFileEntry
    {
        public ushort BootNumber;
        public string Filename;
        public DateTime Date;
    }

    /// <summary>
    /// BootServer provides encapsulation of boot file mappings and enumerations.
    /// </summary>
    public static class BootServer
    {
        static BootServer()
        {
            LoadBootFileTables();
        }

        private static void LoadBootFileTables()
        {
            _numberToNameTable = new Dictionary<ushort, string>();            

            // TODO: fix hardcoded path
            using (StreamReader sr = new StreamReader("Conf\\bootdirectory.txt"))
            {
                int lineNumber = 0;
                while (!sr.EndOfStream)
                {
                    //
                    // Read in the next line.  This is either:
                    //  - A comment to EOL (starting with "#")
                    //  - empty
                    //  - a mapping, consisting of two tokens: a number and a name.
                    //
                    lineNumber++;
                    string line = sr.ReadLine().Trim();

                    if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                    {
                        continue;
                    }

                    string[] tokens = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                    if (tokens.Length != 2)
                    {
                        Log.Write(LogType.Warning, LogComponent.BootServer,
                           "bootdirectory.txt line {0}: Invalid syntax: expected two tokens, got {1}.  Ignoring.", lineNumber, tokens.Length);
                        continue;
                    }
                    else
                    {
                        ushort bootNumber = 0;
                        try
                        {
                            bootNumber = Convert.ToUInt16(tokens[0], 8);
                        }
                        catch
                        {
                            Log.Write(LogType.Warning, LogComponent.BootServer,
                                "bootdirectory.txt line {0}: Invalid syntax: '{1}' is not a valid octal integer value.", lineNumber, tokens[0]);
                            continue;
                        }

                        //
                        // Validate that the file exists in the boot subdirectory.
                        //
                        if (!File.Exists(Path.Combine(Configuration.BootRoot, tokens[1])))
                        {
                            Log.Write(LogType.Warning, LogComponent.BootServer,
                                "bootdirectory.txt line {0}: Specified boot file '{1}' does not exist.", lineNumber, tokens[1]);
                            continue;
                        }

                        //
                        // Looks like this entry is OK syntactically, add it if it isn't a duplicate.
                        //
                        if (!_numberToNameTable.ContainsKey(bootNumber))
                        {
                            _numberToNameTable.Add(bootNumber, tokens[1]);
                        }
                        else
                        {
                            Log.Write(LogType.Warning, LogComponent.BootServer,
                                "bootdirectory.txt line {0}: Specified boot file '{1}' has already been specified.  Ignoring duplicate.", lineNumber, bootNumber);
                            continue;
                        }
                    }
                }
            }
        }

        public static string GetFileNameForNumber(ushort number)
        {
            if (_numberToNameTable.ContainsKey(number))
            {
                return _numberToNameTable[number];
            }
            else
            {
                return null;
            }
        }

        public static FileStream GetStreamForNumber(ushort number)
        {
            if (_numberToNameTable.ContainsKey(number))
            {
                string filePath = Path.Combine(Configuration.BootRoot, _numberToNameTable[number]);
                try
                {
                    return new FileStream(filePath, FileMode.Open, FileAccess.Read);
                }
                catch (Exception e)
                {
                    Log.Write(LogType.Warning, LogComponent.BootServer, "Bootfile {0} could not be opened, error is '{1}'", filePath, e.Message);
                    return null;
                }
            }
            else
            {
                return null;
            }
        }

        public static List<BootFileEntry> EnumerateBootFiles()
        {
            List<BootFileEntry> bootFiles = new List<BootFileEntry>();

            foreach(ushort key in _numberToNameTable.Keys)
            {
                BootFileEntry newEntry = new BootFileEntry();
                newEntry.BootNumber = key;
                newEntry.Filename = _numberToNameTable[key];
                newEntry.Date = new DateTime(1978, 2, 15);  // todo: get real date
                bootFiles.Add(newEntry);
            }

            return bootFiles;
        }

        private static Dictionary<ushort, string> _numberToNameTable;        
    }
}
