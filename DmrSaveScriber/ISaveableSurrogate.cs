using System;
using System.IO;

namespace DmrSaveScriber
{
    public interface ISaveableSurrogate
    {
        bool CanHandle(Type type);
    }

    public interface ISaveableSurrogate<T> : ISaveableSurrogate
    {
        public void Save(T obj, BinaryWriter writer);

        public T Load(BinaryReader reader);
    }
}
