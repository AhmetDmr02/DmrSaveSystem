using System;

namespace DmrSaveScriber
{
    public class SaveSystemException : Exception
    {
        public string SaveableId { get; }
        public SaveSystemException(string saveableId, string message, Exception innerException = null)
            : base($"Save system error for '{saveableId}': {message}", innerException)
        {
            SaveableId = saveableId;
        }
    }
}