using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace IFS
{

    /// <summary>
    /// Custom attribute allowing specification of Word (16-bit) alignment
    /// of a given field.  (Alignment is byte-oriented by default).
    /// </summary>
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public class WordAligned : System.Attribute
    {
        public WordAligned()
        {

        }
    }

    /// <summary>
    /// Custom attribute allowing static specification of size (in elements) of array
    /// fields.  Used during deserialization.
    /// </summary>
    public class ArrayLength : System.Attribute
    {
        public ArrayLength(int i)
        {
            Length = i;
        }

        public int Length;
    }

    public static class Serializer
    {

        /// <summary>
        /// Deserializes the specified byte array into the specified object.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="t"></param>
        /// <returns></returns>
        public static object Deserialize(byte[] data, Type t)
        {
            //
            // We support serialization of structs containing only:
            //  - byte   
            //  - ushort 
            //  - short  
            //  - int
            //  - uint
            //  - BCPLString
            //  - byte[]
            //
            // Struct fields are serialized in the order they are defined in the struct.  Only Public instance fields are considered.
            // If any unsupported fields are present in the considered field types, an exception will be thrown.
            //
            MemoryStream ms = new MemoryStream(data);
            System.Reflection.FieldInfo[] info = t.GetFields(BindingFlags.Public | BindingFlags.Instance);

            object o = Activator.CreateInstance(t);

            for (int i = 0; i < info.Length; i++)
            {                
                // Check alignment of the field; if word aligned we need to ensure proper positioning of the stream.
                if (IsWordAligned(info[i]))
                {
                    if ((ms.Position % 2) != 0)
                    {
                        // Eat up a padding byte
                        ms.ReadByte();
                    }
                }

                // Now read in the appropriate type.
                switch (info[i].FieldType.Name)
                {
                    case "Byte":
                        info[i].SetValue(o, (byte)ms.ReadByte());                        
                        break;

                    case "UInt16":
                        {
                            info[i].SetValue(o, Helpers.ReadUShort(ms));                            
                        }
                        break;

                    case "Int16":
                        {
                            info[i].SetValue(o, (short)Helpers.ReadUShort(ms));
                        }
                        break;

                    case "UInt32":
                        {
                            info[i].SetValue(o, Helpers.ReadUInt(ms));
                        }
                        break;

                    case "Int32":
                        {
                            info[i].SetValue(o, (int)Helpers.ReadUInt(ms));
                        }
                        break;

                    case "BCPLString":
                        {
                            info[i].SetValue(o, new BCPLString(ms));                            
                        }
                        break;

                    case "Byte[]":
                        {
                            // The field MUST be annotated with a length value.
                            int length = GetArrayLength(info[i]);

                            if (length == -1)
                            {
                                throw new InvalidOperationException("Byte arrays must be annotated with an ArrayLength attribute to be deserialized into.");
                            }

                            byte[] value = new byte[length];
                            ms.Read(value, 0, value.Length);                            

                            info[i].SetValue(o, value);
                        }
                        break;

                    default:
                        throw new InvalidOperationException(String.Format("Type {0} is unsupported for deserialization.", info[i].FieldType.Name));
                }                
            }

            return o;
        }

        /// <summary>
        /// Serialize the object (if supported) to an array.
        /// </summary>
        /// <param name="o"></param>
        /// <param name="channel"></param>
        public static byte[] Serialize(object o)
        {            
            MemoryStream ms = new MemoryStream();
            
            //
            // We support serialization of structs containing only:
            //  - byte   
            //  - ushort 
            //  - short  
            //  - int
            //  - uint
            //  - BCPLString
            //
            // Struct fields are serialized in the order they are defined in the struct.  Only Public instance fields are considered.
            // If any unsupported fields are present in the considered field types, an exception will be thrown.
            //
            System.Reflection.FieldInfo[] info = o.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);            

            for(int i=0;i<info.Length;i++)
            {
                // Check alignment of the field; if word aligned we need to pad as necessary to align the next field.
                if (IsWordAligned(info[i]))
                {
                    if ((ms.Position % 2) != 0)
                    {
                        // Write a padding byte
                        ms.WriteByte(0);
                    }
                }

                switch (info[i].FieldType.Name)
                {
                    case "Byte":
                        ms.WriteByte((byte)info[i].GetValue(o));
                        break;

                    case "UInt16":                    
                        {
                            ushort value = (ushort)(info[i].GetValue(o));
                            Helpers.WriteUShort(ms, value);
                        }
                        break;

                    case "Int16":
                        {
                            short value = (short)(info[i].GetValue(o));
                            Helpers.WriteUShort(ms, (ushort)value);
                        }
                        break;

                    case "UInt32":                    
                        {
                            uint value = (uint)(info[i].GetValue(o));
                            Helpers.WriteUInt(ms, value);
                        }
                        break;

                    case "Int32":
                        {
                            int value = (int)(info[i].GetValue(o));
                            Helpers.WriteUInt(ms, (uint)value);
                        }
                        break;

                    case "BCPLString":
                        {
                            BCPLString value = (BCPLString)(info[i].GetValue(o));
                            byte[] bcplArray = value.ToArray();
                            ms.Write(bcplArray, 0, bcplArray.Length);
                        }
                        break;

                    case "Byte[]":
                        {                            
                            byte[] value = (byte[])(info[i].GetValue(o));

                            // Sanity check length of array vs. the specified annotation (if any -- not required
                            // for serialization)
                            int length = GetArrayLength(info[i]);

                            if (length > 0 && length != value.Length)
                            {
                                throw new InvalidOperationException("Array size does not match the size required by the ArraySize annotation.");
                            }                            

                            ms.Write(value, 0, value.Length);
                        }
                        break;
                    

                    default:
                        throw new InvalidOperationException(String.Format("Type {0} is unsupported for serialization.", info[i].FieldType.Name));
                }
            }

            return ms.ToArray();
        }

        private static void SwapBytes(byte[] data)
        {
            for (int i = 0; i < data.Length; i += 2)
            {
                byte t = data[i];
                data[i] = data[i + 1];
                data[i + 1] = t;
            }
        }


        private static bool IsWordAligned(FieldInfo field)
        {
            foreach(CustomAttributeData attribute in field.CustomAttributes)
            {
                if (attribute.AttributeType == typeof(WordAligned))
                {
                    return true;
                }
            }

            return false;
        }

        private static int GetArrayLength(FieldInfo field)
        {
            foreach (Attribute attribute in System.Attribute.GetCustomAttributes(field))
            {
                if (attribute is ArrayLength)
                {
                    return ((ArrayLength)attribute).Length;
                }
            }

            return -1;
        }
    }
}
