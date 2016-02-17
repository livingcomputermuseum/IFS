using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IFS
{
    /// <summary>
    /// Encapsulates global server configuration information.
    /// 
    /// TODO: read in configuration from a text file.
    /// TODO also: make cross-platform compatible (no hardcoding of path delimiters).
    /// </summary>
    public static class Configuration
    {
        /// <summary>
        /// The root directory for the FTP file store.
        /// </summary>
        public static readonly string FTPRoot = "C:\\ifs\\ftp";

        /// <summary>
        /// The root directory for the CopyDisk file store.
        /// </summary>
        public static readonly string CopyDiskRoot = "C:\\ifs\\copydisk";

        /// <summary>
        /// The root directory for the Boot file store.
        /// </summary>
        public static readonly string BootRoot = "C:\\ifs\\boot";

    }
}
