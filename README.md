# DmrSaveSystem

A high-performance binary serialization framework for Unity. The architecture is inspired by the low-level data packing strategies used in Unity Netcode for GameObjects (NGO), adapting network-efficient binary streams for local persistence.

This system is designed for games requiring deterministic state management, minimal memory allocation, and corruption-proof I/O.

---

## Core Features

**NGO-Inspired Binary Packing:** Adapts the philosophy of "bit-packing" used in multiplayer networking. Data is written directly to a binary stream without the overhead of JSON or reflection-heavy serializers.

**Sandboxed Serialization:** Each object is serialized into a shared intermediate memory buffer before writing to disk. If a specific object throws an exception during the save process, it is skipped safely without corrupting the rest of the file.

**Atomic Writes:** Saves are written to a temporary file (`.tmp`) and atomically swapped only upon a successful write, preventing data loss if the game crashes or loses power during a save.

**Surrogate Pattern:** Extensible support for complex Unity types (like `Vector3`, `Quaternion`) via a surrogate dictionary, allowing full control over how specific types are packed.

**Reduced-Garbage Design:** Uses a reusable 4KB `MemoryStream` buffer for object serialization to minimize GC spikes on the hot path. Note that the save pass itself still allocates temporary lists for validation. This is a known trade-off, not a zero-allocation guarantee.

---

## Architecture: The "Network Object" Approach

Just as a `NetworkObject` requires a `NetworkObjectId` to sync across clients, the `DmrSaveSystem` requires a **Deterministic Save ID** to sync data from disk to the scene.

### 1. Deterministic Identification

Every saveable object must provide a stable, hand-authored, or otherwise deterministic ID.

> ⚠️ **The ID must remain identical across game sessions and scene loads.**

Dynamic IDs like `GetInstanceID()` or `Guid.NewGuid()` are useless here because they change every time the game runs. The save system needs to know that `"Chest_01"` in the file corresponds to `"Chest_01"` in the scene. If the ID changes, the data is silently orphaned.

### 2. The Surrogate System

To keep the core system lightweight, type-specific packing logic is decoupled using `ISaveableSurrogate<T>`.

- **Core:** `BinaryWriterExtensions` handles the stream.
- **Extension:** When the writer encounters a complex type (e.g., `Vector3`), it looks up the registered surrogate to handle the packing.
- `Vector3Surrogate` is registered automatically at startup. Add your own for any other complex types.

---

## Usage

### Implementing the Interface

Implement `IDmrSaveable` on any `MonoBehaviour`.

```csharp
using DmrSaveScriber;
using UnityEngine;
using System.IO;

public class Inventory : MonoBehaviour, IDmrSaveable
{
    // Must be unique across ALL saveables in your project.
    [Tooltip("Must be unique and constant. Do not use random generation.")]
    [SerializeField] private string deterministicId = "Inv_Player_Main";

    public int goldAmount;
    public Vector3 lastPosition;

    public string GetSaveId() => deterministicId;

    public void Save(BinaryWriter writer)
    {
        writer.Write(goldAmount);         // Written first
        writer.Write(lastPosition);       // Written second — handled by Vector3Surrogate
    }

    public void Load(BinaryReader reader, bool isSaveFound)
    {
        // isSaveFound == false means this object exists in the scene
        // but has no entry in the save file (e.g. a newly added object).
        // Use this branch to apply your default / reset state.
        if (!isSaveFound)
        {
            goldAmount = 0;
            lastPosition = Vector3.zero;
            return;
        }

        goldAmount = reader.ReadInt32();  // Must match Save() — read first
        lastPosition = reader.Read<Vector3>(); // Must match Save() — read second
    }
}
```

> ⚠️ **`Save()` and `Load()` must read and write in the exact same order and with the exact same types.**
>
> The system frames data per-object (not per-field). If your `Save()` writes an `int` then a `Vector3`, your `Load()` must read an `int` then a `Vector3`. Any mismatch silently corrupts the read cursor for that object, and potentially all objects that follow it in the file. There is no runtime validation to catch this. Maintaining order is entirely your responsibility, just like in **NGO**.

---

### Registration

Objects must register with the manager before any save or load occurs, similar to how `NetworkObjects` register with the `NetworkManager`.

```csharp
void Awake() => DmrSaveManager.RegisterSaveable(this);
```

> ⚠️ **Registration order and timing matters.**
>
> LoadFromFile() is a single linear pass over the file it reads each object's payload and immediately dispatches it to the matching **registered** object, then discards it. No data is held in memory after the pass completes, and no reflection or looping over all objects is used to discover objects at runtime. This is intentional. Retaining the file contents or scanning the scene for potential recipients would reintroduce the allocation and coupling overhead the system is designed to avoid.
The consequence is that any object registering after LoadFromFile() runs will never receive a Load() call at all — not even the isSaveFound = false branch. In practice, call LoadFromFile() after all relevant Start() methods have completed.

### Saving & Loading

```csharp
// Saves to Application.persistentDataPath/SaveName.sua
DmrSaveManager.SaveToFile("MyGameSave");

// Loads data and distributes it to all currently registered objects
DmrSaveManager.LoadFromFile("MyGameSave");
```

---

### Registering a Custom Surrogate

```csharp
// Register before any Save/Load calls that use this type
DmrSaveManager.RegisterSurrogate<Quaternion>(new QuaternionSurrogate());
```

Implement `ISaveableSurrogate<T>`:

```csharp
public class QuaternionSurrogate : ISaveableSurrogate<Quaternion>
{
    public void Save(Quaternion obj, BinaryWriter writer)
    {
        writer.Write(obj.x);
        writer.Write(obj.y);
        writer.Write(obj.z);
        writer.Write(obj.w);
    }

    public Quaternion Load(BinaryReader reader)
    {
        return new Quaternion(
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle()
        );
    }

    public bool CanHandle(Type type) => type == typeof(Quaternion);
}
```

---

## Technical Specs & Constraints

### Thread Safety

> 🚫 **The DmrSaveSystem is NOT thread-safe.**

**All calls to `SaveToFile`, `LoadFromFile`, `RegisterSaveable`, and `UnregisterSaveable` must be made from the Unity Main Thread.**

### The `isSaveFound` Contract

Every registered object receives a `Load()` call — even objects with no matching entry in the save file. When `isSaveFound` is `false`, the `reader` argument is `null`. Your `Load()` implementation **must** handle this branch and set a sensible default state. Failing to guard against it will throw a `NullReferenceException`.

```csharp
public void Load(BinaryReader reader, bool isSaveFound)
{
    if (!isSaveFound)
    {
        // Apply defaults here do NOT touch reader
        return;
    }
    // Safe to read from reader below this point
}
```

### Duplicate IDs

Registering two objects with the same Save ID is silently rejected. The second object will log a warning and will **never** receive a `Load()` call. Always ensure IDs are globally unique across your entire project.
