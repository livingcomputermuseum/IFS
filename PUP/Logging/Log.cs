using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IFS.Logging
{
    [Flags]
    public enum LogLevel
    {
        None = 0,
        Normal = 1,
        Warning = 2,
        Error = 4,
        DroppedPacket = 8,
        InvalidPacket = 0x10,
        UnhandledProtocol = 0x20,
        DuplicateHostNumber = 0x40,

        All = 0x7fffffff,
    }

    public static class Log
    {
        static Log()
        {
            _level = LogLevel.None;
        }

        public static LogLevel Level
        {
            get { return _level; }
            set { _level = value; }
        }

        public static void Write(LogLevel level, string message)
        {
            if ((level & _level) != 0)
            {
                // My log has something to tell you...
                Console.WriteLine("{0}: {1} - {2}", DateTime.Now, level, message);
            }
        }

        private static LogLevel _level;
    }
}
