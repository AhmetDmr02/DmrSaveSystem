using System;
using System.IO;
using UnityEngine;

namespace DmrSaveScriber
{
    public static class BinaryWriterExtensions
    {
        public static void Write<T>(this BinaryWriter writer, T obj) 
        {
            var surrogates = DmrSaveManager.GetSurrogates();
            Type type = typeof(T);

            if (surrogates.TryGetValue(type, out ISaveableSurrogate surrogate))
            {
                if (type.IsValueType)
                {
                    ((ISaveableSurrogate<T>)surrogate).Save(obj, writer);
                }
                else
                {
                    // It is a Class It MIGHT be null.
                    if (obj == null)
                    {
                        writer.Write(false);
                        Debug.LogWarning($"Trying to save null object of type {type.Name}");
                        // We stop here. We don't call the surrogate.
                    }
                    else
                    {
                        writer.Write(true); // This is a marker that says This object exists
                        ((ISaveableSurrogate<T>)surrogate).Save(obj, writer);
                    }
                }
            }
            else
            {
                throw new SaveSystemException("Unknown", $"No surrogate found for type {type.Name}. Register it first.");
            }
        }
    }
}
