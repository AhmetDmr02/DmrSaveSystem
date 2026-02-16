using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DmrSaveScriber
{
    public static class DmrSaveManager
    {
        // Settings - can be modified at runtime
        public static bool EnableBackups { get; set; } = true;
        public static int MaxBackupCount { get; set; } = 3;
        public static bool EnableLogging { get; set; } = true;

        private static readonly List<IDmrSaveable> _saveables = new List<IDmrSaveable>();
        private static readonly Dictionary<Type, ISaveableSurrogate> _surrogates = new Dictionary<Type, ISaveableSurrogate>();
        private static readonly Dictionary<string, int> _saveableOrder = new Dictionary<string, int>();

        private const uint SAVE_VERSION = 1;
        private const uint MAGIC_NUMBER = 0x65647573;

        private static readonly MemoryStream _sharedStream = new MemoryStream(4096);

        private static readonly BinaryWriter _sharedStreamWriter = new BinaryWriter(_sharedStream);
        private static readonly BinaryReader _sharedStreamReader = new BinaryReader(_sharedStream);

        static DmrSaveManager() 
        {
            RegisterSurrogate<Vector3>(new Vector3Surrogate());
        }

        // Internal surrogate access for extensions
        internal static Dictionary<Type, ISaveableSurrogate> GetSurrogates()
        {
            return _surrogates;
        }

        public static bool IsSurrogateRegistered<T>()
        {
            return _surrogates.ContainsKey(typeof(T));
        }

        public static void RegisterSaveable(IDmrSaveable saveable)
        {
            if (saveable == null)
            {
                LogError("Cannot register null saveable");
                return;
            }

            string saveId = saveable.GetSaveId();

            if (string.IsNullOrEmpty(saveId))
            {
                LogError($"Saveable {saveable.GetType().Name} has null or empty SaveId");
                return;
            }

            if (_saveables.Contains(saveable))
            {
                LogWarning($"Saveable {saveId} already registered");
                return;
            }

            if (_saveableOrder.ContainsKey(saveId))
            {
                LogWarning($"Duplicate SaveID: {saveId} not processed");
                return;
            }

            _saveables.Add(saveable);
            _saveableOrder[saveId] = _saveables.Count - 1;
            Log($"Registered saveable: {saveId}");
        }

        public static void UnregisterSaveable(IDmrSaveable saveable)
        {
            if (saveable == null) return;

            string saveId = saveable.GetSaveId();

            if (!_saveables.Contains(saveable))
            {
                LogWarning($"Saveable {saveId} not registered");
                return;
            }

            if (_saveables.Remove(saveable))
            {
                _saveableOrder.Remove(saveId);
                // Rebuild order dictionary
                _saveableOrder.Clear();
                for (int i = 0; i < _saveables.Count; i++)
                {
                    _saveableOrder[_saveables[i].GetSaveId()] = i;
                }
                Log($"Unregistered saveable: {saveId}");
            }
        }

        public static void RegisterSurrogate<T>(ISaveableSurrogate<T> surrogate)
        {
            if (surrogate == null)
            {
                LogError("Cannot register null surrogate");
                return;
            }

            _surrogates[typeof(T)] = surrogate;
            Log($"Registered surrogate for type: {typeof(T).Name}");
        }

        public static int GetRegisteredCount()
        {
            return _saveables.Count;
        }

        public static string[] GetRegisteredIds()
        {
            List<string> validIds = new List<string>();

            for (int i = 0; i < _saveables.Count; i++)
            {
                if (_saveables[i] == null)
                {
                    LogWarning($"Skipping null saveable at index {i} in GetRegisteredIds");
                    continue;
                }

                try
                {
                    string saveId = _saveables[i].GetSaveId();
                    if (!string.IsNullOrEmpty(saveId))
                    {
                        validIds.Add(saveId);
                    }
                    else
                    {
                        LogWarning($"Skipping saveable at index {i} with null/empty SaveID in GetRegisteredIds");
                    }
                }
                catch (Exception ex)
                {
                    LogWarning($"Failed to get SaveID from saveable at index {i} in GetRegisteredIds: {ex.Message}");
                }
            }

            return validIds.ToArray();
        }

        public static void ClearAllSaveables()
        {
            _saveables.Clear();
            _saveableOrder.Clear();
            Log("Cleared all registered saveables");
        }

        public static bool SaveToFile(string savename, string customPath = null)
        {
            savename = NormalizeSaveName(savename);
            string finalSaveName = savename + ".sua";

            string savePath = customPath ?? GetSavePath(finalSaveName);

            try
            {
                // First pass: Clean up null objects and validate SaveIDs
                List<IDmrSaveable> validSaveables = new List<IDmrSaveable>();
                List<int> nullIndices = new List<int>();
                int originalCount = _saveables.Count;

                for (int i = 0; i < _saveables.Count; i++)
                {
                    IDmrSaveable saveable = _saveables[i];

                    if (saveable == null)
                    {
                        nullIndices.Add(i);
                        LogWarning($"Found null saveable at index {i}, will be cleaned up");
                        continue;
                    }

                    // Check if it's a Unity object that has been destroyed
                    if (saveable is UnityEngine.Object unityObj && unityObj == null)
                    {
                        nullIndices.Add(i);
                        LogWarning($"Found destroyed Unity object at index {i}, will be cleaned up");
                        continue;
                    }

                    string saveId = null;
                    try
                    {
                        saveId = saveable.GetSaveId();
                    }
                    catch (Exception ex)
                    {
                        // This catches the "has been destroyed" exception and others
                        LogWarning($"Failed to get SaveID from saveable at index {i}: {ex.Message}. Marking for cleanup.");
                        nullIndices.Add(i);
                        continue;
                    }

                    if (string.IsNullOrEmpty(saveId))
                    {
                        LogWarning($"Saveable at index {i} has null or empty SaveID. Skipping this object.");
                        continue;
                    }

                    validSaveables.Add(saveable);
                }

                // Clean up null objects if any were found
                if (nullIndices.Count > 0)
                {
                    LogWarning($"Cleaning up {nullIndices.Count} null objects from save registry");

                    // Remove null objects in reverse order to maintain indices
                    for (int i = nullIndices.Count - 1; i >= 0; i--)
                    {
                        _saveables.RemoveAt(nullIndices[i]);
                    }

                    // Rebuild the order dictionary
                    _saveableOrder.Clear();
                    for (int i = 0; i < _saveables.Count; i++)
                    {
                        try
                        {
                            string saveId = _saveables[i]?.GetSaveId();
                            if (!string.IsNullOrEmpty(saveId))
                            {
                                _saveableOrder[saveId] = i;
                            }
                        }
                        catch (Exception ex)
                        {
                            LogWarning($"Failed to rebuild order for saveable at index {i}: {ex.Message}");
                        }
                    }

                    Log($"Registry cleanup complete. Reduced from {originalCount} to {_saveables.Count} objects");
                }

                if (validSaveables.Count == 0)
                {
                    LogWarning("No valid saveables found to save");
                    return false;
                }

                Log($"Proceeding with save of {validSaveables.Count} valid objects");

                // Create backup if enabled and file exists
                if (EnableBackups && File.Exists(savePath))
                {
                    CreateBackup(savePath);
                }

                // Use a temporary file to ensure atomic writes
                string tempPath = savePath + ".tmp";

                using (FileStream fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
                using (BinaryWriter writer = new BinaryWriter(fileStream))
                {
                    // Write header
                    writer.Write(MAGIC_NUMBER);
                    writer.Write(SAVE_VERSION);
                    writer.Write(validSaveables.Count);

                    // Save all valid saveables
                    int successCount = 0;
                    for (int i = 0; i < validSaveables.Count; i++)
                    {
                        if (SaveSingleObject(validSaveables[i], writer))
                        {
                            successCount++;
                        }
                        else
                        {
                            // Log the failure but continue with other objects
                            LogWarning($"Failed to save object at index {i}, continuing with remaining objects");
                        }
                    }

                    try
                    {
                        writer.Flush();
                        fileStream.Flush(true);
                    }
                    catch (Exception flushEx)
                    {
                        LogError($"Failed to flush save data: {flushEx.Message}");
                        return false;
                    }

                    Log($"Successfully saved {successCount}/{validSaveables.Count} objects");
                }

                bool success = TryFinalizeSaveFile(tempPath, savePath);

                return success;
            }
            catch (Exception ex)
            {
                LogError($"Save failed: {ex.Message}");
                return false;
            }
        }

        public static bool LoadFromFile(string savename, string customPath = null)
        {
            savename = NormalizeSaveName(savename);
            string finalSaveName = savename + ".sua";

            string savePath = customPath ?? GetSavePath(finalSaveName);

            if (!File.Exists(savePath))
            {
                LogWarning($"Save file not found: {savePath}");
                return false;
            }

            try
            {
                using (FileStream fileStream = new FileStream(savePath, FileMode.Open, FileAccess.Read))
                using (BinaryReader fileReader = new BinaryReader(fileStream))
                {
                    // Verify header
                    uint magic = fileReader.ReadUInt32();
                    if (magic != MAGIC_NUMBER)
                    {
                        LogError("Invalid save file format");
                        return false;
                    }

                    uint version = fileReader.ReadUInt32();
                    if (version > SAVE_VERSION)
                    {
                        LogError($"Save file version {version} is newer than supported version {SAVE_VERSION}");
                        return false;
                    }

                    int savedObjectCount = fileReader.ReadInt32();

                    HashSet<string> loadedIds = new HashSet<string>();

                    for (int i = 0; i < savedObjectCount; i++)
                    {
                        string id = fileReader.ReadString();
                        int dataLength = fileReader.ReadInt32();

                        if (_saveableOrder.TryGetValue(id, out int index))
                        {
                            IDmrSaveable saveable = _saveables[index];

                            if (saveable == null || (saveable is UnityEngine.Object uObj && uObj == null))
                            {
                                fileStream.Seek(dataLength, SeekOrigin.Current); // Skip
                                LogWarning($"Saveable object is destroyed or null, skipping");
                                continue;
                            }

                            if (_sharedStream.Capacity < dataLength)
                                _sharedStream.Capacity = dataLength;

                            _sharedStream.Position = 0;
                            _sharedStream.SetLength(dataLength);

                            fileStream.Read(_sharedStream.GetBuffer(), 0, dataLength);

                            _sharedStream.Position = 0;

                            try
                            {
                                // Load into object
                                saveable.Load(_sharedStreamReader, true);
                                loadedIds.Add(id);
                            }
                            catch (Exception ex)
                            {
                                LogError($"Failed to load object {id}: {ex.Message}");
                            }
                        }
                        else
                        {
                            // Object in file, but not in scene (Destroyed? Unloaded?) -> Skip
                            fileStream.Seek(dataLength, SeekOrigin.Current);
                            LogWarning($"Saveable with ID '{id}' not found in scene, skipping");
                        }
                    }

                    for (int i = 0; i < _saveables.Count; i++)
                    {
                        var saveable = _saveables[i];

                        if (saveable == null) continue;

                        if (saveable is UnityEngine.Object uObj && uObj == null) continue;

                        string id = null;
                        try
                        {
                            id = saveable.GetSaveId();
                        }
                        catch
                        {
                            continue; 
                        }

                        if (string.IsNullOrEmpty(id)) continue;


                        if (!loadedIds.Contains(id))
                        {
                            try
                            {
                                // null to indicate "No save data found" so script can do its own logic with it
                                Log($"Object Id:{saveable.GetSaveId()} exists in game, but not in save file (New object?)");
                                saveable.Load(null, false);
                            }
                            catch (Exception ex)
                            {
                                LogWarning($"Failed to default-load object {id}: {ex.Message}");
                            }
                        }
                    }

                    Log($"Load completed. Found {loadedIds.Count} objects in save file.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogError($"Load failed: {ex.Message}");
                return false;
            }
        }

        public static int CleanupNullAndDestroyedSaveables()
        {
            int cleanedCount = 0;

            for (int i = _saveables.Count - 1; i >= 0; i--)
            {
                bool shouldRemove = false;

                if (_saveables[i] == null)
                {
                    shouldRemove = true;
                }
                else if (_saveables[i] is UnityEngine.Object unityObj && unityObj == null)
                {
                    shouldRemove = true;
                }
                else
                {
                    // Test if GetSaveID throws (destroyed object)
                    try
                    {
                        _saveables[i].GetSaveId();
                    }
                    catch
                    {
                        shouldRemove = true;
                    }
                }

                if (shouldRemove)
                {
                    _saveables.RemoveAt(i);
                    cleanedCount++;
                }
            }

            if (cleanedCount > 0)
            {
                // Rebuild order dictionary
                _saveableOrder.Clear();
                for (int i = 0; i < _saveables.Count; i++)
                {
                    try
                    {
                        if (_saveables[i] is UnityEngine.Object unityObj && unityObj == null)
                            continue;

                        string saveId = _saveables[i]?.GetSaveId();
                        if (!string.IsNullOrEmpty(saveId))
                        {
                            _saveableOrder[saveId] = i;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogWarning($"Failed to rebuild order for saveable at index {i}: {ex.Message}");
                    }
                }

                Log($"Cleaned up {cleanedCount} null/destroyed saveables from registry");
            }

            return cleanedCount;
        }

        private static bool SaveSingleObject(IDmrSaveable saveable, BinaryWriter fileWriter)
        {
            string saveId = saveable.GetSaveId();

            try
            {
                _sharedStream.Position = 0;
                _sharedStream.SetLength(0);

                saveable.Save(_sharedStreamWriter);
                _sharedStreamWriter.Flush();

                int dataLength = (int)_sharedStream.Length;

                string getSaveID = saveable.GetSaveId();

                fileWriter.Write(getSaveID);

                fileWriter.Write(dataLength);

                fileWriter.Write(_sharedStream.GetBuffer(), 0, dataLength);

                return true;
            }
            catch (Exception ex)
            {
                LogError($"Failed to save object '{saveId}': {ex.Message}");
                return false;
            }
        }

        private static void CreateBackup(string originalPath)
        {
            try
            {
                string backupDir = Path.Combine(Path.GetDirectoryName(originalPath), "Backups");
                if (!Directory.Exists(backupDir))
                    Directory.CreateDirectory(backupDir);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string backupPath = Path.Combine(backupDir, $"{Path.GetFileNameWithoutExtension(originalPath)}_{timestamp}.sua");

                File.Copy(originalPath, backupPath);

                // Clean up old backups
                CleanupOldBackups(backupDir);

                Log($"Backup created: {backupPath}");
            }
            catch (Exception ex)
            {
                LogWarning($"Failed to create backup: {ex.Message}");
            }
        }

        private static void CleanupOldBackups(string backupDir)
        {
            try
            {
                var backupFiles = Directory.GetFiles(backupDir, "*.sua");
                if (backupFiles.Length <= MaxBackupCount) return;

                Array.Sort(backupFiles);
                for (int i = 0; i < backupFiles.Length - MaxBackupCount; i++)
                {
                    File.Delete(backupFiles[i]);
                }
            }
            catch (Exception ex)
            {
                LogWarning($"Failed to cleanup old backups: {ex.Message}");
            }
        }

        /// <summary>
        /// Attempts to atomically finalize the temporary save file into its final location.
        /// Tries File.Replace first (atomic on supported platforms), falls back to delete+move,
        /// and always removes the temp file on failure. Returns true on success.
        /// </summary>
        private static bool TryFinalizeSaveFile(string tempPath, string savePath)
        {
            bool success = false;

            try
            {
                if (File.Exists(savePath))
                {
                    try
                    {
                        // Preferred atomic path
                        File.Replace(tempPath, savePath, null);
                        success = true;
                    }
                    catch (Exception replaceEx)
                    {
                        // Fallback
                        LogWarning($"File.Replace failed: {replaceEx.Message}. Attempting fallback delete+move.");

                        try
                        {
                            File.Delete(savePath);
                            File.Move(tempPath, savePath);

                            LogWarning($"Fallback delete+move succeeded.");

                            success = true;
                        }
                        catch (Exception fbEx)
                        {
                            LogError($"Fallback delete+move failed: {fbEx.Message}");
                            success = false;
                        }
                    }
                }
                else
                {
                    try
                    {
                        File.Move(tempPath, savePath);
                        success = true;
                    }
                    catch (Exception mvEx)
                    {
                        LogError($"Failed to move {tempPath} to {savePath} - {mvEx.Message}");
                        success = false;
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"Unexpected error while finalizing save file: {ex.Message}");
                success = false;
            }
            finally
            {
                // Always clean up temp if we failed to finalize the save
                if (!success)
                {
                    try
                    {
                        if (File.Exists(tempPath))
                            File.Delete(tempPath);
                    }
                    catch (Exception cleanupEx)
                    {
                        LogWarning($"Failed to cleanup temp file {tempPath}: {cleanupEx.Message}");
                    }
                }
            }

            if (success)
                Log($"Save completed to: {savePath}");

            return success;
        }

        // Utility methods
        private static string GetSavePath(string saveFileName)
        {
            string dataPath;

            // Alternative logic: MyDocuments/CompanyName_ProductName
            //string appName = Application.companyName + "_" + Application.productName;
            //dataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), appName);

            dataPath = Application.persistentDataPath;

            if (!Directory.Exists(dataPath))
            {
                Directory.CreateDirectory(dataPath);
            }

            return Path.Combine(dataPath, saveFileName);
        }

        public static bool SaveFileExists(string saveFileName, string customPath = null)
        {
            saveFileName = NormalizeSaveName(saveFileName);
            string finalSaveName = saveFileName + ".sua";  
            string savePath = customPath ?? GetSavePath(finalSaveName); 

            return File.Exists(savePath);
        }

        public static void DeleteSaveFile(string saveFileName, string customPath = null)
        {
            saveFileName = NormalizeSaveName(saveFileName);
            string finalSaveName = saveFileName + ".sua";   
            string savePath = customPath ?? GetSavePath(finalSaveName); 

            if (File.Exists(savePath))
            {
                File.Delete(savePath);
                Log($"Deleted save file: {savePath}");
            }
        }

        public static string GetSaveFilePath(string saveFileName)
        {
            saveFileName = NormalizeSaveName(saveFileName); 
            string finalSaveName = saveFileName + ".sua";  
            return GetSavePath(finalSaveName);
        }

        public static long GetSaveFileSize(string saveFileName, string customPath = null)
        {
            saveFileName = NormalizeSaveName(saveFileName);
            string finalSaveName = saveFileName + ".sua"; 
            string savePath = customPath ?? GetSavePath(finalSaveName); 

            if (File.Exists(savePath))
            {
                return new FileInfo(savePath).Length;
            }
            return 0;
        }

        public static DateTime GetSaveFileTimestamp(string saveFileName, string customPath = null)
        {
            saveFileName = NormalizeSaveName(saveFileName); 
            string finalSaveName = saveFileName + ".sua";  
            string savePath = customPath ?? GetSavePath(finalSaveName);

            if (File.Exists(savePath))
            {
                return File.GetLastWriteTime(savePath);
            }
            return DateTime.MinValue;
        }

        private static string NormalizeSaveName(string savename)
        {
            if (string.IsNullOrWhiteSpace(savename)) throw new ArgumentException(nameof(savename));
            const string ext = ".sua";
            return savename.EndsWith(ext, StringComparison.OrdinalIgnoreCase)
                ? savename.Substring(0, savename.Length - ext.Length)
                : savename;
        }
        // Logging methods
        private static void Log(string message)
        {
            if (EnableLogging)
                Debug.Log($"[SaveManager] {message}");
        }

        private static void LogWarning(string message)
        {
            if (EnableLogging)
                Debug.LogWarning($"[SaveManager] {message}");
        }

        private static void LogError(string message)
        {
            Debug.LogError($"[SaveManager] {message}");
        }
    }
}