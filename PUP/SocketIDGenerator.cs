using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IFS
{
    static class SocketIDGenerator
    {
        static SocketIDGenerator()
        {
            //
            // Initialize the socket ID counter; we start with a
            // number beyond the range of well-defined sockets.
            // For each request for a new ID, we will
            // increment this counter to ensure that each channel gets
            // a unique ID.  (Well, until we wrap around...)
            //            
            _nextSocketID = _startingSocketID;

            _idLock = new ReaderWriterLockSlim();
        }


        /// <summary>
        /// Generates what should be a unique Socket ID.
        /// Uniqueness is not guaranteed, but the probability of a collision
        /// is extremely low given its intended use.
        /// 
        /// ID Generation is sequential, but this behavior should not be
        /// relied upon.
        /// <returns></returns>
        public static UInt32 GetNextSocketID()
        {
            _idLock.EnterWriteLock();
            UInt32 next = _nextSocketID;

            _nextSocketID++;

            //
            // Handle the wrap around case (which we're very unlikely to
            // ever hit, but why not do the right thing).
            // Start over at the initial ID.  This is very unlikely to
            // collide with any pending channels.
            //
            if (_nextSocketID < _startingSocketID)
            {
                _nextSocketID = _startingSocketID;
            }
            _idLock.ExitWriteLock();

            return next;
        }

        private static UInt32 _nextSocketID;

        private static readonly UInt32 _startingSocketID = 0x1000;

        private static ReaderWriterLockSlim _idLock;
    }
}
