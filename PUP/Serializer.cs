using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace IFS
{
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
            //
            // Struct fields are serialized in the order they are defined in the struct.  Only Public instance fields are considered.
            // If any unsupported fields are present in the considered field types, an exception will be thrown.
            //
            MemoryStream ms = new MemoryStream(data);
            System.Reflection.FieldInfo[] info = t.GetFields(BindingFlags.Public | BindingFlags.Instance);

            object o = Activator.CreateInstance(t);

            for (int i = 0; i < info.Length; i++)
            {                
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
                switch(info[i].FieldType.Name)
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

                    default:
                        throw new InvalidOperationException(String.Format("Type {0} is unsupported for serialization.", info[i].FieldType.Name));
                }
            }

            return ms.ToArray();
        }
    }
}
