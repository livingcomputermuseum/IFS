using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IFS.FTP
{
    /// <summary>
    /// Defines the well-known set of FTP property names, both Mandatory and Optional.
    /// </summary>
    public static class KnownPropertyNames
    {
        // Mandatory
        public static readonly string ServerFilename = "Server-Filename";
        public static readonly string Type = "Type";
        public static readonly string EndOfLineConvention = "End-of-Line-Convention";
        public static readonly string ByteSize = "Byte-Size";
        public static readonly string Device = "Device";
        public static readonly string Directory = "Directory";
        public static readonly string NameBody = "Name-Body";
        public static readonly string Version = "Version";

        // Optional
        public static readonly string Size = "Size";
        public static readonly string UserName = "User-Name";
        public static readonly string UserPassword = "User-Password";
        public static readonly string UserAccount = "User-Account";
        public static readonly string ConnectName = "Connect-Name";
        public static readonly string ConnectPassword = "Connect-Password";
        public static readonly string CreationDate = "Creation-Date";
        public static readonly string WriteDate = "Write-Date";
        public static readonly string ReadDate = "Read-Date";
        public static readonly string Author = "Author";
        public static readonly string Checksum = "Checksum";
        public static readonly string DesiredProperty = "Desired-Property";
    }         

    /// <summary>
    /// Defines an FTP PropertyList and methods to work with the contents of one.
    /// From the FTP spec:
    /// 
    /// "5.1 Syntax of a file property list
    /// 
    /// A file property list consists of a string of ASCII characters, beginning with a left parenthesis and
    /// ending with a matching right parenthesis.  Within that list, each property is represented similarly
    /// as a parenthesized list.  For example:
    ///    ((Server-Filename TESTFILE.7)(Byte-Size 36))
    /// 
    /// This scheme has the advantage of being human readable, although it will require some form of 
    /// scanner or interpreter.  Nevertheless, this is a rigid format, with minimum flexibility ni form; FTP is
    /// a machine-to-machine protocol, not a programming language.
    /// 
    /// The first item in each property (delimited by a left parenthesis and a space) is the property name,
    /// taken from a fixed but extensible set.  Upper- and lower-case letters are considered equivalent in the
    /// property name.  The text between the first space and the right parenthesis is the property value.  All
    /// characters in the property value are taken literally, except in accordance with the quoting convention
    /// described below.
    /// 
    /// All spaces are significant, and multiple spaces may not be arbitrarily included.  There should be no space
    /// between the two leading parentheses, for example, and a single space separates a property 
    /// name from the property value.  Other spaces in a property value will become part of that value, so 
    /// that the following example will work properly:
    ///   ((Server-Filename xxxxx)(Read-Date 23-Jan-76 11:30:22 PST))
    /// 
    /// A single apostrophe is used as the quote character in a property value, and should be used before a 
    /// parenthesis or a desired apostrophe:
    ///   Don't(!)Goof ==> (PropertyName Don''t'(!')Goof)"
    /// 
    /// 
    /// </summary>
    public class PropertyList
    {
        public PropertyList()
        {
            _propertyList = new Dictionary<string, string>();
        }

        public PropertyList(string list) : this()
        {
            ParseList(list);
        }

        /// <summary>
        /// Indicates whether the Property List contains the specified property
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public bool ContainsPropertyValue(string name)
        {
            return (_propertyList.ContainsKey(name.ToLowerInvariant()));
        }

        /// <summary>
        /// Returns the value for the specified property, if present.  Otherwise returns null.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public string GetPropertyValue(string name)
        {
            name = name.ToLowerInvariant();

            if (_propertyList.ContainsKey(name))
            {
                return _propertyList[name];
            }
            else
            {
                return null;
            }
        }

        public void SetPropertyValue(string name, string value)
        {
            name = name.ToLowerInvariant();            

            if (_propertyList.ContainsKey(name))
            {
                _propertyList[name] = value;
            }
            else
            {
                _propertyList.Add(name, value);
            }
        }

        /// <summary>
        /// Serialize the PropertyList back to its string representation.
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();

            // Opening paren
            sb.Append("(");

            foreach(string key in _propertyList.Keys)
            {
                sb.AppendFormat("({0} {1})", key, EscapeString(_propertyList[key]));                
            }

            // Closing paren
            sb.Append(")");

            return sb.ToString();
        }

        private string EscapeString(string value)
        {
            StringBuilder sb = new StringBuilder(value.Length);

            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == '\'' || value[i] == '(' || value[i] == ')')
                {
                    // Escape this thing
                    sb.Append('\'');
                    sb.Append(value[i]);
                }
                else
                {
                    sb.Append(value[i]);
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Parses a string representation of a property list into our hash table.
        /// </summary>
        /// <param name="list"></param>
        private void ParseList(string list)
        {
            //
            // First check the basics; the string must start and end with left and right parens, respectively.
            // We do not trim whitespace as there should not be any per the spec.
            //
            if (!list.StartsWith("(") || !list.EndsWith(")"))
            {
                throw new InvalidOperationException("Property list must begin and end with parentheses.");
            }            

            //
            // Looking good so far; parse individual properties now.  These also start and end with
            // left and right parens.
            //
            int index = 1;

            //
            // Loop until we hit the end of the string (minus the closing paren)
            //
            while (index < list.Length - 1)
            { 
                // Start of next property, must begin with a left paren.
                if (list[index] != '(')
                {
                    throw new InvalidOperationException("Property must begin with a left parenthesis.");
                }

                index++;                

                //
                // Read in the full property name.  Property names can't have escaped characters in them
                // so we don't need to watch out for those, just find the first space.
                //
                int endIndex = list.IndexOf(' ', index);

                if (endIndex < 0)
                {
                    throw new InvalidOperationException("Badly formed property list, no space delimiter found.");
                }

                string propertyName = list.Substring(index, endIndex - index).ToLowerInvariant();
                index = endIndex + 1;       // Move past space

                //
                // Read in the property value.  This may contain spaces or escaped characters and it ends with an
                // unescaped right paren.
                //
                StringBuilder propertyValue = new StringBuilder();

                while(true)
                {
                    // End of value?
                    if (list[index] == ')')
                    {
                        // Move past closing paren
                        index++;

                        // And we're done with this property.
                        break;
                    }
                    // Quoted value?
                    else if (list[index] == '\'')
                    {
                        // Add quoted character
                        index++;

                        // Ensure we don't walk off the end of the string
                        if (index >= list.Length)
                        {
                            throw new InvalidOperationException("Invalid property list syntax.");
                        }

                        propertyValue.Append(list[index]);
                    }
                    // Just a normal character
                    else
                    {
                        propertyValue.Append(list[index]);
                    }

                    index++;

                    // Ensure we don't walk off the end of the string
                    if (index >= list.Length)
                    {
                        throw new InvalidOperationException("Invalid property list syntax.");
                    }
                }

                //
                // Add name/value pair to the hash table.
                //
                if (!_propertyList.ContainsKey(propertyName))
                {
                    _propertyList.Add(propertyName, propertyValue.ToString());
                }
                else
                {
                    throw new InvalidOperationException(String.Format("Duplicate property entry for '{0}", propertyName));
                }                
            }
        }        

        private Dictionary<string, string> _propertyList;
    }
}
