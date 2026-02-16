using System;
using System.IO;
using UnityEngine;

namespace DmrSaveScriber
{
    public static class BinaryReaderExtensions
    {
        public static T Read<T>(this BinaryReader reader)
        {
            Type type = typeof(T);
            var surrogates = DmrSaveManager.GetSurrogates();

            if (surrogates.TryGetValue(type, out ISaveableSurrogate surrogate))
            {
                if (type.IsValueType)
                {
                    return ((ISaveableSurrogate<T>)surrogate).Load(reader);
                }
                else
                {
                    // Classes have a boolean header.
                    bool isNotNull = reader.ReadBoolean();

                    if (!isNotNull)
                    {
                        Debug.LogWarning($"Trying to load null object of type {type.Name}");
                        return default(T); // Returns null

                    }

                    return ((ISaveableSurrogate<T>)surrogate).Load(reader);
                }
            }
            else
            {
                throw new SaveSystemException("Unknown", $"No surrogate found for type {type.Name}. Register it first.");
            }
        }
    }
}
