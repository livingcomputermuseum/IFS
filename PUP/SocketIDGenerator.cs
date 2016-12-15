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
