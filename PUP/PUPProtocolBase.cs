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
    }    
    
    public struct PUPProtocolEntry
    {
        public PUPProtocolEntry(string friendlyName, UInt32 socket, ConnectionType connectionType, PUPProtocolBase implementation)
        {
            FriendlyName = friendlyName;
            Socket = socket;
            ConnectionType = connectionType;
            ProtocolImplementation = implementation;
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
        /// Indicates the type of connection (connectionless or BSP-based)
        /// </summary>
        public ConnectionType ConnectionType;

        public PUPProtocolBase ProtocolImplementation;
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
