using System.IO;

namespace DmrSaveScriber
{
    public interface IDmrSaveable 
    {
        public void Save(BinaryWriter writer);
        public void Load(BinaryReader reader, bool isSaveFound);

        // Must be persistent and unique
        public string GetSaveId();
    }
}
