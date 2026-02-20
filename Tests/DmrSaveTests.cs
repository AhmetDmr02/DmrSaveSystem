using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using DmrSaveScriber;
using System.Text.RegularExpressions;

public class DmrSaveTests
{
    private const string TEST_SAVE_NAME = "TestSave_UnitTests";

    private class MockInventory : IDmrSaveable
    {
        public string ID;
        public int Gold;
        public List<string> Items = new List<string>();

        public MockInventory(string id) { ID = id; }

        public string GetSaveId() => ID;

        public void Save(BinaryWriter writer)
        {
            writer.Write(Gold);
            writer.Write(Items.Count);
            foreach (var item in Items) writer.Write(item);
        }

        public void Load(BinaryReader reader, bool isSaveFound)
        {
            if (!isSaveFound) return;
            Gold = reader.ReadInt32();
            int count = reader.ReadInt32();
            Items.Clear();
            for (int i = 0; i < count; i++) Items.Add(reader.ReadString());
        }
    }

    private class MockPlayer : IDmrSaveable
    {
        public string ID;
        public Vector3 Position;

        public MockPlayer(string id) { ID = id; }
        public string GetSaveId() => ID;

        public void Save(BinaryWriter writer)
        {
            writer.Write(Position);
        }

        public void Load(BinaryReader reader, bool isSaveFound)
        {
            if (!isSaveFound) return;
            Position = reader.Read<Vector3>();
        }
    }

    [System.Serializable]
    public class NestedItem
    {
        public string Name;
        public int Durability;
    }

    public class NestedItemSurrogate : ISaveableSurrogate<NestedItem>
    {
        public void Save(NestedItem obj, BinaryWriter writer)
        {
            writer.Write(obj.Name);
            writer.Write(obj.Durability);
        }
        public NestedItem Load(BinaryReader reader)
        {
            return new NestedItem { Name = reader.ReadString(), Durability = reader.ReadInt32() };
        }
        public bool CanHandle(Type type) => type == typeof(NestedItem);
    }

    private class ComplexPlayer : IDmrSaveable
    {
        public string ID;
        public List<NestedItem> Inventory = new List<NestedItem>();

        public ComplexPlayer(string id) { ID = id; }
        public string GetSaveId() => ID;

        public void Save(BinaryWriter writer)
        {
            writer.Write(Inventory.Count);
            foreach (var item in Inventory)
            {
                writer.Write(item);
            }
        }

        public void Load(BinaryReader reader, bool isSaveFound)
        {
            if (!isSaveFound) return;
            int count = reader.ReadInt32();
            Inventory.Clear();
            for (int i = 0; i < count; i++)
            {
                Inventory.Add(reader.Read<NestedItem>());
            }
        }
    }

    private class BleedingTraitor : IDmrSaveable
    {
        public string ID;
        public BleedingTraitor(string id) { ID = id; }
        public string GetSaveId() => ID;

        public void Save(BinaryWriter writer)
        {
            writer.Write("I AM WRITING DATA I WILL NOT READ");
            writer.Write(123456789);
            for (int i = 0; i < 100; i++) writer.Write(i);
        }

        public void Load(BinaryReader reader, bool isSaveFound)
        {
        }
    }

    private class ReadTraitor : IDmrSaveable
    {
        public string ID;
        public ReadTraitor(string id) { ID = id; }
        public string GetSaveId() => ID;

        public void Save(BinaryWriter writer)
        {
            writer.Write(42);
        }

        public void Load(BinaryReader reader, bool isSaveFound)
        {
            int val = reader.ReadInt32();
            reader.ReadString();
            reader.ReadInt32();
            reader.ReadBoolean();
        }
    }

    [SetUp]
    public void Setup()
    {
        DmrSaveManager.ClearAllSaveables();
        DmrSaveManager.DeleteSaveFile(TEST_SAVE_NAME);

        if (!DmrSaveManager.IsSurrogateRegistered<Vector3>())
        {
            DmrSaveManager.RegisterSurrogate(new Vector3Surrogate());
        }
    }
    [TearDown]
    public void Teardown()
    {
        //DmrSaveManager.DeleteSaveFile(TEST_SAVE_NAME);
        DmrSaveManager.ClearAllSaveables();
    }

    // Checks if simple data like integers and strings can be saved and loaded back correctly
    [Test]
    public void Test_SaveAndLoad_BasicData()
    {
        var inventory = new MockInventory("Inv_1");
        inventory.Gold = 500;
        inventory.Items.Add("Sword");
        inventory.Items.Add("Potion");
        DmrSaveManager.RegisterSaveable(inventory);

        bool saveSuccess = DmrSaveManager.SaveToFile(TEST_SAVE_NAME);
        Assert.IsTrue(saveSuccess, "SaveToFile failed");

        inventory.Gold = 0;
        inventory.Items.Clear();

        bool loadSuccess = DmrSaveManager.LoadFromFile(TEST_SAVE_NAME);
        Assert.IsTrue(loadSuccess, "LoadFromFile failed");

        Assert.AreEqual(500, inventory.Gold);
        Assert.AreEqual(2, inventory.Items.Count);
        Assert.AreEqual("Sword", inventory.Items[0]);
    }

    // Checks if the surrogate system works for structs like Vector3 using the extension methods
    [Test]
    public void Test_Surrogate_Vector3()
    {
        var player = new MockPlayer("Player_Main");
        player.Position = new Vector3(10.5f, 20.0f, -5.5f);
        DmrSaveManager.RegisterSaveable(player);

        DmrSaveManager.SaveToFile(TEST_SAVE_NAME);

        player.Position = Vector3.zero;

        DmrSaveManager.LoadFromFile(TEST_SAVE_NAME);

        Assert.AreEqual(10.5f, player.Position.x);
        Assert.AreEqual(20.0f, player.Position.y);
        Assert.AreEqual(-5.5f, player.Position.z);
    }

    // Ensures the game does not crash or fail if the save file contains objects that are no longer in the scene
    [Test]
    public void Test_MissingObject_DoesNotCrash()
    {
        var obj1 = new MockInventory("Obj_1");
        var obj2 = new MockInventory("Obj_2");
        DmrSaveManager.RegisterSaveable(obj1);
        DmrSaveManager.RegisterSaveable(obj2);
        DmrSaveManager.SaveToFile(TEST_SAVE_NAME);

        DmrSaveManager.ClearAllSaveables();

        var newObj1 = new MockInventory("Obj_1");
        DmrSaveManager.RegisterSaveable(newObj1);

        bool success = DmrSaveManager.LoadFromFile(TEST_SAVE_NAME);

        Assert.IsTrue(success, "Load should succeed even if objects are missing");
    }

    // Ensures that new objects added to the game after a save file was created are handled gracefully
    [Test]
    public void Test_NewObject_ReceivesNullLoad()
    {
        DmrSaveManager.SaveToFile(TEST_SAVE_NAME);

        var newObj = new MockInventory("New_Obj");
        newObj.Gold = 100;
        DmrSaveManager.RegisterSaveable(newObj);

        DmrSaveManager.LoadFromFile(TEST_SAVE_NAME);

        Assert.AreEqual(100, newObj.Gold);
    }

    // Verifies that files are actually created and deleted on the disk
    [Test]
    public void Test_FileOperations()
    {
        var dummy = new MockInventory("Dummy_Obj");
        DmrSaveManager.RegisterSaveable(dummy);

        bool saveResult = DmrSaveManager.SaveToFile(TEST_SAVE_NAME);
        Assert.IsTrue(saveResult, "Save should succeed when object is registered");

        Assert.IsTrue(DmrSaveManager.SaveFileExists(TEST_SAVE_NAME));

        DmrSaveManager.DeleteSaveFile(TEST_SAVE_NAME);
        Assert.IsFalse(DmrSaveManager.SaveFileExists(TEST_SAVE_NAME));
    }

    // Checks if the system handles null values inside lists correctly without throwing errors
    [Test]
    public void Test_NestedReferenceTypes_WithNulls()
    {
        if (!DmrSaveManager.IsSurrogateRegistered<NestedItem>())
            DmrSaveManager.RegisterSurrogate(new NestedItemSurrogate());

        var player = new ComplexPlayer("Complex_P1");
        player.Inventory.Add(new NestedItem { Name = "Axe", Durability = 100 });
        player.Inventory.Add(null);
        player.Inventory.Add(new NestedItem { Name = "Bow", Durability = 50 });

        DmrSaveManager.RegisterSaveable(player);

        DmrSaveManager.SaveToFile(TEST_SAVE_NAME);

        player.Inventory.Clear();

        DmrSaveManager.LoadFromFile(TEST_SAVE_NAME);

        Assert.AreEqual(3, player.Inventory.Count);
        Assert.AreEqual("Axe", player.Inventory[0].Name);
        Assert.IsNull(player.Inventory[1], "Reader failed to read 'null' from the stream correctly");
        Assert.AreEqual("Bow", player.Inventory[2].Name);
    }

    // Stresses the internal memory buffer to ensure it resizes correctly for large amounts of data
    [Test]
    public void Test_LargeDataBufferResizing()
    {
        var heavyObj = new MockInventory("Heavy_Obj");
        for (int i = 0; i < 10000; i++)
        {
            heavyObj.Items.Add($"Item_{i}");
        }
        DmrSaveManager.RegisterSaveable(heavyObj);

        bool saveSuccess = DmrSaveManager.SaveToFile(TEST_SAVE_NAME);
        Assert.IsTrue(saveSuccess);

        heavyObj.Items.Clear();

        bool loadSuccess = DmrSaveManager.LoadFromFile(TEST_SAVE_NAME);
        Assert.IsTrue(loadSuccess);

        Assert.AreEqual(10000, heavyObj.Items.Count);
        Assert.AreEqual("Item_9999", heavyObj.Items[9999]);
    }

    // Verifies that the order in which objects are registered does not affect their ability to load
    [Test]
    public void Test_RegistrationOrder_DoesNotMatter()
    {
        var a = new MockInventory("Obj_A");
        var b = new MockInventory("Obj_B");
        var c = new MockInventory("Obj_C");

        a.Gold = 10; b.Gold = 20; c.Gold = 30;

        DmrSaveManager.RegisterSaveable(a);
        DmrSaveManager.RegisterSaveable(b);
        DmrSaveManager.RegisterSaveable(c);
        DmrSaveManager.SaveToFile(TEST_SAVE_NAME);

        DmrSaveManager.ClearAllSaveables();

        var newC = new MockInventory("Obj_C");
        var newA = new MockInventory("Obj_A");

        DmrSaveManager.RegisterSaveable(newC);
        DmrSaveManager.RegisterSaveable(newA);

        DmrSaveManager.LoadFromFile(TEST_SAVE_NAME);

        Assert.AreEqual(30, newC.Gold, "Obj_C failed to load because order changed");
        Assert.AreEqual(10, newA.Gold, "Obj_A failed to load because order changed");
    }

    // Ensures the loader gracefully fails and returns false if the save file is corrupted
    [Test]
    public void Test_CorruptFile_ReturnsFalse()
    {
        string path = DmrSaveManager.GetSaveFilePath(TEST_SAVE_NAME);
        File.WriteAllText(path, "This is not a binary save file, just text.");

        var obj = new MockInventory("Obj_1");
        DmrSaveManager.RegisterSaveable(obj);

        UnityEngine.TestTools.LogAssert.Expect(LogType.Error, "[SaveManager] Invalid save file format");

        bool success = DmrSaveManager.LoadFromFile(TEST_SAVE_NAME);

        Assert.IsFalse(success, "LoadFromFile should return false for corrupt data");
    }

    // Checks that we cannot register two objects with the same ID to prevent save corruption
    [Test]
    public void Test_DuplicateRegistration_Ignored()
    {
        var obj1 = new MockInventory("SAME_ID");
        obj1.Gold = 100;
        DmrSaveManager.RegisterSaveable(obj1);

        var obj2 = new MockInventory("SAME_ID");
        obj2.Gold = 999;
        DmrSaveManager.RegisterSaveable(obj2);

        Assert.AreEqual(1, DmrSaveManager.GetRegisteredCount(), "Should only have 1 object registered");

        DmrSaveManager.SaveToFile(TEST_SAVE_NAME);

        DmrSaveManager.ClearAllSaveables();
        var freshObj = new MockInventory("SAME_ID");
        DmrSaveManager.RegisterSaveable(freshObj);
        DmrSaveManager.LoadFromFile(TEST_SAVE_NAME);

        Assert.AreEqual(100, freshObj.Gold, "First registered object should take priority");
    }

    // Stress test that saves 1000 objects where one object writes garbage data to try and break the file
    [Test]
    public void Test_1000_Objects_With_Bleeding()
    {
        int count = 1000;
        int traitorIndex = 500;

        System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();

        for (int i = 0; i < count; i++)
        {
            if (i == traitorIndex)
            {
                DmrSaveManager.RegisterSaveable(new BleedingTraitor($"Obj_{i}"));
            }
            else
            {
                var inv = new MockInventory($"Obj_{i}");
                inv.Gold = i;
                DmrSaveManager.RegisterSaveable(inv);
            }
        }

        sw.Start();
        bool saveSuccess = DmrSaveManager.SaveToFile(TEST_SAVE_NAME);
        sw.Stop();
        Assert.IsTrue(saveSuccess);
        Debug.Log($"[StressTest] Saved 1000 objects in {sw.ElapsedMilliseconds}ms");

        DmrSaveManager.ClearAllSaveables();

        var loadedObjects = new List<IDmrSaveable>();

        for (int i = 0; i < count; i++)
        {
            if (i == traitorIndex)
            {
                var traitor = new BleedingTraitor($"Obj_{i}");
                DmrSaveManager.RegisterSaveable(traitor);
                loadedObjects.Add(traitor);
            }
            else
            {
                var inv = new MockInventory($"Obj_{i}");
                DmrSaveManager.RegisterSaveable(inv);
                loadedObjects.Add(inv);
            }
        }

        sw.Reset();
        sw.Start();
        bool loadSuccess = DmrSaveManager.LoadFromFile(TEST_SAVE_NAME);
        sw.Stop();
        Assert.IsTrue(loadSuccess);
        Debug.Log($"[StressTest] Loaded 1000 objects in {sw.ElapsedMilliseconds}ms");

        var survivor = loadedObjects[traitorIndex + 1] as MockInventory;

        Assert.IsNotNull(survivor, "The object after the traitor should exist");
        Assert.AreEqual(501, survivor.Gold, "Data Bleeding Detected! The traitor's garbage data corrupted Object 501.");

        var last = loadedObjects[999] as MockInventory;
        Assert.AreEqual(999, last.Gold);
    }

    // Ensures that if an object tries to read more data than it wrote, it crashes safely without breaking the rest of the file
    [Test]
    public void Test_ReadTraitor_Sandboxing()
    {
        var objA = new MockInventory("Obj_A"); objA.Gold = 100;
        var traitor = new ReadTraitor("Traitor_B");
        var objC = new MockInventory("Obj_C"); objC.Gold = 300;

        DmrSaveManager.RegisterSaveable(objA);
        DmrSaveManager.RegisterSaveable(traitor);
        DmrSaveManager.RegisterSaveable(objC);

        DmrSaveManager.SaveToFile(TEST_SAVE_NAME);

        DmrSaveManager.ClearAllSaveables();
    var newA = new MockInventory("Obj_A");
        var newTraitor = new ReadTraitor("Traitor_B");
        var newC = new MockInventory("Obj_C");

        DmrSaveManager.RegisterSaveable(newA);
        DmrSaveManager.RegisterSaveable(newTraitor);
        DmrSaveManager.RegisterSaveable(newC);

        UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new Regex("Failed to load object Traitor_B.*"));

        bool loadResult = DmrSaveManager.LoadFromFile(TEST_SAVE_NAME);

        Assert.IsTrue(loadResult, "The load process should finish successfully despite the traitor.");

        Assert.AreEqual(300, newC.Gold, "Sandboxing Worked! Object C loaded correctly even though Traitor B crashed.");

        Assert.AreEqual(100, newA.Gold);
    }
    // A saveable specifically designed to hold variable amounts of raw byte data
    private class BlobObject : IDmrSaveable
    {
        public string ID;
        public byte[] Data;

        public BlobObject(string id, int sizeInBytes)
        {
            ID = id;
            Data = new byte[sizeInBytes];
            // Fill with a pattern so we can verify integrity later
            for (int i = 0; i < sizeInBytes; i++) Data[i] = (byte)(i % 255);
        }

        public string GetSaveId() => ID;

        public void Save(BinaryWriter writer)
        {
            writer.Write(Data.Length);
            writer.Write(Data);
        }

        public void Load(BinaryReader reader, bool isSaveFound)
        {
            if (!isSaveFound) return;
            int count = reader.ReadInt32();
            Data = reader.ReadBytes(count);
        }
    }

    [Test]
    public void Test_Buffer_Elasticity_SmallHugeSmall()
    {
        var smallObj = new BlobObject("Small_Start", 100);

        var hugeObj = new BlobObject("Huge_Middle", 1024 * 1024);

        var tinyObj = new BlobObject("Tiny_End", 10);

        DmrSaveManager.RegisterSaveable(smallObj);
        DmrSaveManager.RegisterSaveable(hugeObj);
        DmrSaveManager.RegisterSaveable(tinyObj);

        bool saveSuccess = DmrSaveManager.SaveToFile(TEST_SAVE_NAME);
        Assert.IsTrue(saveSuccess, "Save should succeed");

        DmrSaveManager.ClearAllSaveables();
        var loadSmall = new BlobObject("Small_Start", 0);
        var loadHuge = new BlobObject("Huge_Middle", 0);
        var loadTiny = new BlobObject("Tiny_End", 0);

        DmrSaveManager.RegisterSaveable(loadSmall);
        DmrSaveManager.RegisterSaveable(loadHuge);
        DmrSaveManager.RegisterSaveable(loadTiny);

        bool loadSuccess = DmrSaveManager.LoadFromFile(TEST_SAVE_NAME);
        Assert.IsTrue(loadSuccess, "Load should succeed");

        Assert.AreEqual(100, loadSmall.Data.Length);
        Assert.AreEqual(1024 * 1024, loadHuge.Data.Length);
        Assert.AreEqual(10, loadTiny.Data.Length, "Tiny object should allow reading exactly 10 bytes, not the Huge buffer size.");

        Assert.AreEqual((byte)0, loadTiny.Data[0]);
        Assert.AreEqual((byte)9, loadTiny.Data[9]);
    }
	
	// Tests if an unloaded object's data survives a save-load cycle where it was completely absent
    [Test]
    public void Test_DeadData_IsRetainedAcrossSaves()
    {
        var objA = new MockInventory("Retain_A"); objA.Gold = 10;
        var objB = new MockInventory("Retain_B"); objB.Gold = 20;
        
        DmrSaveManager.RegisterSaveable(objA);
        DmrSaveManager.RegisterSaveable(objB);
        DmrSaveManager.SaveToFile(TEST_SAVE_NAME);
        
        DmrSaveManager.ClearAllSaveables();

        var liveObjA = new MockInventory("Retain_A");
        DmrSaveManager.RegisterSaveable(liveObjA);
        
        DmrSaveManager.LoadFromFile(TEST_SAVE_NAME);
        DmrSaveManager.SaveToFile(TEST_SAVE_NAME);
        
        DmrSaveManager.ClearAllSaveables();
        
        var recoveredA = new MockInventory("Retain_A");
        var recoveredB = new MockInventory("Retain_B");
        DmrSaveManager.RegisterSaveable(recoveredA);
        DmrSaveManager.RegisterSaveable(recoveredB);
        
        DmrSaveManager.LoadFromFile(TEST_SAVE_NAME);
        
        Assert.AreEqual(20, recoveredB.Gold, "Object B data was lost while it was dead.");
    }

    // Tests if an object spawned after the initial load can manually pull its dead data from the cache
    [Test]
    public void Test_DeadData_LateInstantiationRecovery()
    {
        var lateObj = new MockInventory("Late_Enemy");
        lateObj.Gold = 999;
        DmrSaveManager.RegisterSaveable(lateObj);
        DmrSaveManager.SaveToFile(TEST_SAVE_NAME);
        
        DmrSaveManager.ClearAllSaveables();
        DmrSaveManager.LoadFromFile(TEST_SAVE_NAME);
        
        var spawnedLater = new MockInventory("Late_Enemy");
        
        DmrSaveManager.LoadDeadObjectFromPreviousLoadedData(spawnedLater);
        
        Assert.AreEqual(999, spawnedLater.Gold, "Late instantiated object failed to recover its dead data.");
    }

    // Ensures that recovering dead data removes it from the stale pool so it isn't duplicated internally
    [Test]
    public void Test_DeadData_RemovedFromStaleWhenRecovered()
    {
        var obj = new MockInventory("Stale_Test");
        obj.Gold = 50;
        DmrSaveManager.RegisterSaveable(obj);
        DmrSaveManager.SaveToFile(TEST_SAVE_NAME);
        
        DmrSaveManager.ClearAllSaveables();
        DmrSaveManager.LoadFromFile(TEST_SAVE_NAME);
        
        var recoveredObj = new MockInventory("Stale_Test");
        DmrSaveManager.RegisterSaveable(recoveredObj);
        DmrSaveManager.LoadDeadObjectFromPreviousLoadedData(recoveredObj);
        
        recoveredObj.Gold = 100;
        
        DmrSaveManager.SaveToFile(TEST_SAVE_NAME);
        DmrSaveManager.ClearAllSaveables();
        
        var finalObj = new MockInventory("Stale_Test");
        DmrSaveManager.RegisterSaveable(finalObj);
        DmrSaveManager.LoadFromFile(TEST_SAVE_NAME);
        
        Assert.AreEqual(100, finalObj.Gold, "Data reverted to stale version because it wasn't removed from the dead pool.");
    }

    // Tests if dead data can survive being passed through multiple consecutive saves without ever being loaded
    [Test]
    public void Test_DeadData_MultipleSavesWhileDead()
    {
        var survivor = new MockInventory("Survivor");
        survivor.Gold = 777;
        DmrSaveManager.RegisterSaveable(survivor);
        DmrSaveManager.SaveToFile(TEST_SAVE_NAME);
        
        for (int i = 0; i < 5; i++)
        {
            DmrSaveManager.ClearAllSaveables();
            DmrSaveManager.LoadFromFile(TEST_SAVE_NAME);
            DmrSaveManager.SaveToFile(TEST_SAVE_NAME);
        }
        
        DmrSaveManager.ClearAllSaveables();
        var revived = new MockInventory("Survivor");
        DmrSaveManager.RegisterSaveable(revived);
        DmrSaveManager.LoadFromFile(TEST_SAVE_NAME);
        
        Assert.AreEqual(777, revived.Gold, "Dead data decayed or was lost after multiple generations of saving.");
    }

    // Tests calling the late instantiation load method on an object that has no previous save data
    [Test]
    public void Test_DeadData_LateInstantiate_WithNoData()
    {
        DmrSaveManager.SaveToFile(TEST_SAVE_NAME);
        DmrSaveManager.LoadFromFile(TEST_SAVE_NAME);
        
        var brandNew = new MockInventory("Brand_New_Item");
        brandNew.Gold = 15;
        
        DmrSaveManager.LoadDeadObjectFromPreviousLoadedData(brandNew);
        
        Assert.AreEqual(15, brandNew.Gold, "Brand new object was corrupted by empty dead data pull.");
    }

    // Ensures loading a new file completely clears the dead data from the previous file
    [Test]
    public void Test_DeadData_DifferentFilesDoNotMix()
    {
        string file1 = TEST_SAVE_NAME + "_1";
        string file2 = TEST_SAVE_NAME + "_2";
        
        var objA = new MockInventory("Cross_A"); objA.Gold = 1;
        DmrSaveManager.RegisterSaveable(objA);
        DmrSaveManager.SaveToFile(file1);
        
        DmrSaveManager.ClearAllSaveables();
        var objB = new MockInventory("Cross_B"); objB.Gold = 2;
        DmrSaveManager.RegisterSaveable(objB);
        DmrSaveManager.SaveToFile(file2);
        
        DmrSaveManager.ClearAllSaveables();
        DmrSaveManager.LoadFromFile(file1);
        DmrSaveManager.LoadFromFile(file2); 
        
        var testObj = new MockInventory("Cross_A");
        testObj.Gold = 0;
        DmrSaveManager.LoadDeadObjectFromPreviousLoadedData(testObj);
        
        Assert.AreEqual(0, testObj.Gold, "Ghost data from File 1 bled into File 2's dead data pool.");
        
        DmrSaveManager.DeleteSaveFile(file1);
        DmrSaveManager.DeleteSaveFile(file2);
    }

    // Tests if an extremely large object can safely sit in the dead data dictionary without breaking writes
    [Test]
    public void Test_DeadData_HeavyObjectRetention()
    {
        var heavy = new BlobObject("Heavy_Dead", 1024 * 512); 
        heavy.Data[0] = 42;
        DmrSaveManager.RegisterSaveable(heavy);
        DmrSaveManager.SaveToFile(TEST_SAVE_NAME);
        
        DmrSaveManager.ClearAllSaveables();
        
        DmrSaveManager.LoadFromFile(TEST_SAVE_NAME);
        DmrSaveManager.SaveToFile(TEST_SAVE_NAME);
        
        var recovered = new BlobObject("Heavy_Dead", 0);
        DmrSaveManager.RegisterSaveable(recovered);
        DmrSaveManager.LoadFromFile(TEST_SAVE_NAME);
        
        Assert.AreEqual(1024 * 512, recovered.Data.Length);
        Assert.AreEqual(42, recovered.Data[0]);
    }

    // Tests if interleaving dead data and live data corrupts the file stream offsets
    [Test]
    public void Test_DeadData_DoesNotBleedIntoLive()
    {
        var dead1 = new MockInventory("Dead_1"); dead1.Gold = 10;
        var live1 = new MockInventory("Live_1"); live1.Gold = 20;
        var dead2 = new MockInventory("Dead_2"); dead2.Gold = 30;
        
        DmrSaveManager.RegisterSaveable(dead1);
        DmrSaveManager.RegisterSaveable(live1);
        DmrSaveManager.RegisterSaveable(dead2);
        DmrSaveManager.SaveToFile(TEST_SAVE_NAME);
        
        DmrSaveManager.ClearAllSaveables();
        var newLive = new MockInventory("Live_1");
        DmrSaveManager.RegisterSaveable(newLive);
        
        DmrSaveManager.LoadFromFile(TEST_SAVE_NAME);
        
        Assert.AreEqual(20, newLive.Gold, "Live object read the wrong data because of adjacent dead objects.");
    }

    // Tests recovering one dead object but leaving another dead, ensuring both are handled correctly on the next save
    [Test]
    public void Test_DeadData_PartialRecovery()
    {
        var a = new MockInventory("A"); a.Gold = 1;
        var b = new MockInventory("B"); b.Gold = 2;
        var c = new MockInventory("C"); c.Gold = 3;
        
        DmrSaveManager.RegisterSaveable(a);
        DmrSaveManager.RegisterSaveable(b);
        DmrSaveManager.RegisterSaveable(c);
        DmrSaveManager.SaveToFile(TEST_SAVE_NAME);
        
        DmrSaveManager.ClearAllSaveables();
        DmrSaveManager.LoadFromFile(TEST_SAVE_NAME);
        
        var recB = new MockInventory("B");
        DmrSaveManager.RegisterSaveable(recB);
        DmrSaveManager.LoadDeadObjectFromPreviousLoadedData(recB);
        recB.Gold = 200;
        
        DmrSaveManager.SaveToFile(TEST_SAVE_NAME);
        
        DmrSaveManager.ClearAllSaveables();
        var finalA = new MockInventory("A");
        var finalB = new MockInventory("B");
        var finalC = new MockInventory("C");
        DmrSaveManager.RegisterSaveable(finalA);
        DmrSaveManager.RegisterSaveable(finalB);
        DmrSaveManager.RegisterSaveable(finalC);
        
        DmrSaveManager.LoadFromFile(TEST_SAVE_NAME);
        
        Assert.AreEqual(1, finalA.Gold);
        Assert.AreEqual(200, finalB.Gold);
        Assert.AreEqual(3, finalC.Gold);
    }

    // Ensures that the Traitor class data is perfectly preserved in dead data even if it contains stream-breaking garbage
    [Test]
    public void Test_DeadData_TraitorPreservation()
    {
        var traitor = new BleedingTraitor("Dead_Traitor");
        DmrSaveManager.RegisterSaveable(traitor);
        DmrSaveManager.SaveToFile(TEST_SAVE_NAME);
        
        DmrSaveManager.ClearAllSaveables();
        DmrSaveManager.LoadFromFile(TEST_SAVE_NAME);
        DmrSaveManager.SaveToFile(TEST_SAVE_NAME);
        
        long size = DmrSaveManager.GetSaveFileSize(TEST_SAVE_NAME);
        
        Assert.IsTrue(size > 400, "Traitor data was truncated or lost while acting as dead data.");
    }
    private class MockUnitySaveable : MonoBehaviour, IDmrSaveable
    {
        public string ID = "Unity_Mock_1";
        public string GetSaveId() => ID;

        public void Save(BinaryWriter writer) { writer.Write(42); }
        public void Load(BinaryReader reader, bool isSaveFound)
        {
            if (isSaveFound) reader.ReadInt32();
        }
    }
    // Verifies that the internal cleanup method successfully identifies and purges destroyed Unity objects before saving
    [Test]
    public void Test_Cleanup_RemovesDestroyedUnityObjects()
    {
        var survivor = new MockInventory("Standard_Survivor");
        DmrSaveManager.RegisterSaveable(survivor);

        var go = new GameObject("TempSaveableObject");
        var unitySaveable = go.AddComponent<MockUnitySaveable>();
        DmrSaveManager.RegisterSaveable(unitySaveable);

        Assert.AreEqual(2, DmrSaveManager.GetRegisteredCount(), "Both objects should be registered.");

        UnityEngine.Object.DestroyImmediate(go);

        bool saveSuccess = DmrSaveManager.SaveToFile(TEST_SAVE_NAME);
        Assert.IsTrue(saveSuccess, "Save should succeed despite the destroyed object.");

        Assert.AreEqual(1, DmrSaveManager.GetRegisteredCount(), "Registry failed to purge the destroyed Unity object!");
        Assert.AreEqual("Standard_Survivor", DmrSaveManager.GetRegisteredIds()[0], "The wrong object was purged!");
    }
}