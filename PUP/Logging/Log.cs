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

#define LOGGING_ENABLED

using System;
using System.IO;

namespace IFS.Logging
{
    /// <summary>
    /// Specifies a component to specify logging for
    /// </summary>
    [Flags]
    public enum LogComponent
    {
        None = 0,
        Ethernet = 0x1,
        RTP = 0x2,
        BSP = 0x4,
        MiscServices = 0x8,
        CopyDisk = 0x10,
        DirectoryServices = 0x20,
        PUP = 0x40,
        FTP = 0x80,
        BreathOfLife = 0x100,
        EFTP = 0x200,
        BootServer = 0x400,
        UDP = 0x800,
        Mail = 0x1000,

        Configuration = 0x1000,
        All = 0x7fffffff
    }

    /// <summary>
    /// Specifies the type (or severity) of a given log message
    /// </summary>
    [Flags]
    public enum LogType
    {
        None = 0,
        Normal = 0x1,
        Warning = 0x2,
        Error = 0x4,
        Verbose = 0x8,
        All = 0x7fffffff
    }

    /// <summary>
    /// Provides basic functionality for logging messages of all types.
    /// </summary>
    public static class Log
    {
        static Log()
        {            
            _components = Configuration.LogComponents;
            _type = Configuration.LogTypes;

            //_logStream = new StreamWriter("log.txt");
        }

        public static LogComponent LogComponents
        {
            get { return _components; }
            set { _components = value; }
        }

        /// <summary>
        /// Logs a message without specifying type/severity for terseness;
        /// will not log if Type has been set to None.
        /// </summary>
        /// <param name="level"></param>
        /// <param name="message"></param>
        /// <param name="args"></param>
        public static void Write(LogComponent component, string message, params object[] args)
        {
            Write(LogType.Normal, component, message, args);
        }

        public static void Write(LogType type, LogComponent component, string message, params object[] args)
        {
            if ((_type & type) != 0 &&
                (_components & component) != 0)
            {
                //
                // My log has something to tell you...                
                Console.WriteLine(component.ToString() + ": " + message, args);

                if (_logStream != null)
                {
                    _logStream.WriteLine(component.ToString() + ": " + message, args);
                }
            }
        }

        private static LogComponent _components;
        private static LogType _type;
        private static StreamWriter _logStream;
    }
}
