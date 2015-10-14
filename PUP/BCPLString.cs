using System;
using System.Collections.Generic;
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
            
            _string = new byte[_string.Length];

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
        /// Returns the raw representation of the BCPL string
        /// </summary>
        /// <returns></returns>
        public byte[] ToArray()
        {
            byte[] a = new byte[_string.Length + 1];

            a[0] = (byte)_string.Length;
            _string.CopyTo(a, 1);

            return a;
        }

        private byte[] _string;
    }
}
