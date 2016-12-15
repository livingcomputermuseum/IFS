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

namespace IFS
{
    public enum ConnectionType
    {
        Connectionless,     /* echo, name resolution, etc. */
        BSP,                /* FTP, Telnet, CopyDisk, etc. */
        EFTP,               /* EFTP-based (boot, printing) */
    }    
    
    public struct PUPProtocolEntry
    {
        public PUPProtocolEntry(string friendlyName, UInt32 socket, ConnectionType connectionType, PUPProtocolBase implementation)
        {
            FriendlyName = friendlyName;
            Socket = socket;
            ConnectionType = connectionType;
            ProtocolImplementation = implementation;
            WorkerType = null;
        }

        public PUPProtocolEntry(string friendlyName, UInt32 socket, ConnectionType connectionType, Type workerType)
        {
            FriendlyName = friendlyName;
            Socket = socket;
            ConnectionType = connectionType;
            WorkerType = workerType;
            ProtocolImplementation = null;
        }

        /// <summary>
        /// Indicates the 'friendly' name for the protocol.
        /// </summary>
        public string FriendlyName;

        /// <summary>
        /// Indicates the socket used by the protocol
        /// </summary>
        public UInt32 Socket;

        /// <summary>
        /// Indicates the type of connection (connectionless, BSP-based or EFTP)
        /// </summary>
        public ConnectionType ConnectionType;

        public PUPProtocolBase ProtocolImplementation;

        public Type WorkerType;
    }

    /// <summary>
    /// Base class for all PUP-based protocols.
    /// </summary>
    public abstract class PUPProtocolBase
    {
        public PUPProtocolBase()
        {
            
        }
       
        /// <summary>
        /// Called by dispatcher to send incoming data destined for this protocol.
        /// </summary>
        /// <param name="p"></param>
        public abstract void RecvData(PUP p);

    }

}
