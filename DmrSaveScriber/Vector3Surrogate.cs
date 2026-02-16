using System;
using System.IO;
using UnityEngine;

namespace DmrSaveScriber
{
    public class Vector3Surrogate : ISaveableSurrogate<Vector3>
    {
        public void Save(Vector3 obj, BinaryWriter writer)
        {
            writer.Write(obj.x);
            writer.Write(obj.y);
            writer.Write(obj.z);
        }

        public Vector3 Load(BinaryReader reader)
        {
            float x = reader.ReadSingle();
            float y = reader.ReadSingle();
            float z = reader.ReadSingle();
            return new Vector3(x, y, z);
        }

        public bool CanHandle(Type type)
        {
            return type == typeof(Vector3);
        }
    }
}
