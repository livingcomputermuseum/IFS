using IFS.BSP;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IFS
{    
    /// <summary>
    /// A BCPL string is one byte of length N followed by N bytes of ASCII. 
    /// This class is a (very) simple encapsulation of that over a byte array.
    /// </summary>
    public class BCPLString
    {
        public BCPLString(string s)
        {
            if (s.Length > 255)
            {
                throw new InvalidOperationException("Max length for a BCPL string is 255 characters.");
            }
            
            _string = new byte[s.Length];

            // We simply take the low 8-bits of each Unicode character and stuff it into the
            // byte array.  This works fine for the ASCII subset of Unicode but obviously
            // is bad for everything else.  This is unlikely to be an issue given the lack of
            // any real internationalization support on the IFS end of things, but might be
            // something to look at.
            for(int i=0;i< _string.Length; i++)
            {
                _string[i] = (byte)s[i];
            }
        }

        /// <summary>
        /// Build a new BCPL string from the raw representation
        /// </summary>
        /// <param name="rawData"></param>
        public BCPLString(byte[] rawData)
        {            
            if (rawData.Length > 256)
            {
                throw new InvalidOperationException("Max length for a BCPL string is 255 characters.");
            }

            // Sanity check that first byte matches length of data sent to us
            if (rawData.Length < 1 || rawData[0] != rawData.Length - 1)
            {
                throw new InvalidOperationException("BCPL length data is invalid.");
            }

            _string = new byte[rawData.Length - 1];
            Array.Copy(rawData, 1, _string, 0, rawData.Length - 1);
        }

        /// <summary>
        /// Build a new BCPL string from the raw representation at the given position in the array
        /// </summary>
        /// <param name="rawData"></param>
        public BCPLString(byte[] rawData, int offset)
        {            
            int length = rawData[offset];

            // Sanity check that BCPL length fits within specified array
            if (length > rawData.Length - offset)
            {
                throw new InvalidOperationException("BCPL length data is invalid.");
            }

            _string = new byte[length];
            Array.Copy(rawData, offset + 1, _string, 0, length);
        }

        public BCPLString(BSPChannel channel)
        {
            byte length = channel.ReadByte();
            _string = new byte[length];

            channel.Read(ref _string, length);
        }

        public BCPLString(Stream s)
        {
            byte length = (byte)s.ReadByte();
            _string = new byte[length];

            s.Read(_string, 0, length);
        }

        public int Length
        {
            get { return _string.Length; }
        }

        /// <summary>
        /// Returns a native representation of the BCPL string
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();

            // See notes in constructor re: unicode.
            for (int i = 0; i < _string.Length; i++)
            {
                sb.Append((char)_string[i]);                
            }

            return sb.ToString();
        }

        /// <summary>
        /// Returns the raw representation of the BCPL string.
        /// This returned array is padded to a word boundary.
        /// </summary>
        /// <returns></returns>
        public byte[] ToArray()
        {
            int length = _string.Length + ((_string.Length % 2) == 0 ? 2 : 1);
            byte[] a = new byte[length];

            a[0] = (byte)_string.Length;
            _string.CopyTo(a, 1);

            return a;
        }

        private byte[] _string;
    }
}
