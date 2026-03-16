# WorldManager Refactor + Model Caching

**Date:** 2026-03-16
**Status:** Approved

## Problem

`WorldManager.cs` (~493 lines) handles four distinct concerns in one file:
player management, world item spawning, structure spawning, and world object
spawning. This makes it hard to navigate and modify as features are added.
Additionally, `ResourceLoader.Load<PackedScene>()` is called per-instance with
no caching, meaning the same GLB is loaded from disk repeatedly.

## Goals

1. Split WorldManager into focused, single-responsibility classes.
2. Cache loaded PackedScenes in a central registry.
3. No behaviour changes — identical world appearance and logic.

## File Layout

All files live in `client/scripts/world/`:

| File | Approx lines | Responsibility |
|---|---|---|
| `WorldManager.cs` | ~50 | Signal wiring, spawner lifecycle |
| `ModelRegistry.cs` | ~60 | PackedScene cache + ApplyMaterials |
| `PlayerSpawner.cs` | ~80 | Local + remote player dict, spawn/update/remove |
| `WorldItemSpawner.cs` | ~70 | World item dict, CreateWorldItemVisual |
| `StructureSpawner.cs` | ~100 | Structure dict, CreateStructureVisual, static lookup tables |
| `WorldObjectSpawner.cs` | ~90 | World object dict, CreateWorldObjectVisual, convex shape builder |

## Architecture

### WorldManager.cs

Thin coordinator. Creates spawner instances in `_Ready()`, wires GameManager
signals to spawner methods. Holds no entity dictionaries itself.

The existing `_worldSpawned` guard (prevents double-fire when `SubscriptionApplied`
fires while already connected) stays in `WorldManager` as a `bool _worldSpawned`
field on the coordinator:

```csharp
public partial class WorldManager : Node3D
{
    private bool _worldSpawned;
    private PlayerSpawner _players;
    private WorldItemSpawner _items;
    private StructureSpawner _structures;
    private WorldObjectSpawner _worldObjects;

    public override void _Ready()
    {
        _players      = new PlayerSpawner(this, GameManager.Instance);
        _items        = new WorldItemSpawner(this, GameManager.Instance);
        _structures   = new StructureSpawner(this, GameManager.Instance);
        _worldObjects = new WorldObjectSpawner(this, GameManager.Instance);

        var gm = GameManager.Instance;
        gm.SubscriptionApplied  += OnSubscriptionApplied;
        gm.PlayerUpdated        += id => _players.OnUpdated(id);
        gm.PlayerRemoved        += id => _players.OnRemoved(id);
        gm.WorldItemChanged     += _items.Sync;
        gm.StructureChanged     += _structures.Sync;
        gm.WorldObjectUpdated   += _worldObjects.OnUpdated;

        if (gm.IsConnected && gm.GetLocalPlayer() != null)
            OnSubscriptionApplied();
    }

    private void OnSubscriptionApplied()
    {
        if (_worldSpawned) return;
        _worldSpawned = true;
        _players.SpawnAll();
        _items.Sync();
        _structures.Sync();
        _worldObjects.SyncAll();
    }
}
```

### ModelRegistry.cs

Static class. Owns the PackedScene cache and `ApplyMaterials`.

`Get()` returns `null` if the path does not exist — callers must guard with
`ResourceLoader.Exists(path)` before calling `Get()`, exactly as the current
code does. This preserves the existing fallback-mesh logic in each spawner.

```csharp
public static class ModelRegistry
{
    private static readonly Dictionary<string, PackedScene> _cache = new();

    // Returns null if path does not exist — caller must check ResourceLoader.Exists() first.
    public static PackedScene? Get(string path)
    {
        if (!_cache.TryGetValue(path, out var scene))
            _cache[path] = scene = ResourceLoader.Load<PackedScene>(path);
        return scene;
    }

    /// <summary>Recursively processes mesh materials: zeroes metallic, applies
    /// color override or dims albedo by 0.85.</summary>
    public static void ApplyMaterials(Node root, Color? color = null) { ... }
}
```

All spawners and `BuildSystem` call `ModelRegistry.Get(path)!.Instantiate<Node3D>()`
after an `ResourceLoader.Exists()` guard, replacing direct `ResourceLoader.Load` calls.
`BuildSystem.CreateGhostPreview` also currently calls `ResourceLoader.Load` directly
(line ~107) and must be updated to use `ModelRegistry.Get` for consistency.

### Spawner Classes

Plain C# classes (not Nodes). Constructor signature:

```csharp
public XxxSpawner(Node3D parent, GameManager gm)
```

- `parent` — used for `parent.AddChild(node)`
- `gm` — used for `gm.GetAll*()`, `gm.GetLocalPlayer()`, etc.

**PlayerSpawner** specific notes:
- Entity dict is `Dictionary<string, RemotePlayer>` (keyed by identity hex string),
  not `Dictionary<ulong, Node3D>`, because remote players are identified by string identity.
- Creates `new PlayerController { Name = "LocalPlayer" }` then calls `parent.AddChild()`.
  `PlayerController` is a `CharacterBody3D` that self-initialises in `_Ready`/`_EnterTree`
  — no further setup needed from the spawner beyond setting position, rotation, and color.
- Uses `PlayerPrefs.LoadColorHex()` as a fallback color in two places (local player spawn
  and local player color update). `PlayerPrefs` is a static class — no wiring needed.

**StructureSpawner** entity dict is `Dictionary<ulong, StaticBody3D>` (not `Node3D`)
because `CreateStructureVisual` returns `StaticBody3D` and callers rely on this type.

**WorldObjectSpawner** entity dict is `Dictionary<ulong, Node3D>`.

**WorldItemSpawner** entity dict is `Dictionary<ulong, Node3D>`.

### BuildSystem.cs compatibility

`BuildSystem` currently calls three static methods on `WorldManager`:
- `WorldManager.StructureFallbackMesh` (line ~104)
- `WorldManager.StructureModelPath` (line ~115)
- `WorldManager.StructureYOffset` (line ~122)

These move to `StructureSpawner` as `public static` methods. All three call sites
in `BuildSystem.cs` are updated to `StructureSpawner.*`. No other files reference
these methods.

`BuildSystem` also calls `ResourceLoader.Load<PackedScene>(modelPath)` directly
(line ~107). This is updated to use `ModelRegistry.Get(modelPath)` for consistency
with the rest of the codebase.

## Data Flow

```
GameManager signals
    SubscriptionApplied  → _players.SpawnAll()    (spawns local + all online remote players)
                         → _items.Sync()
                         → _structures.Sync()
                         → _worldObjects.SyncAll() (iterates GetAllWorldObjects(), adds missing)
    PlayerUpdated(id)    → _players.OnUpdated(id)
    PlayerRemoved(id)    → _players.OnRemoved(id)
    WorldItemChanged     → _items.Sync()
    StructureChanged     → _structures.Sync()
    WorldObjectUpdated   → _worldObjects.OnUpdated(id, removed)
                           (delta: removes node if removed=true, adds new if removed=false)
```

`SyncAll()` on `WorldObjectSpawner` mirrors the existing `OnWorldObjectsChanged()`:
iterates `gm.GetAllWorldObjects()`, creates visuals for any IDs not yet in the dict.
It does NOT remove objects (world objects are only removed via `OnWorldObjectUpdated`
with `removed=true`).

## Optimisation

`ModelRegistry.Get()` ensures each unique GLB path is loaded once per session
and reused for all instances. This eliminates repeated disk reads when many
world objects of the same type are spawned (e.g. hundreds of pine trees).

## Out of Scope

- No logic changes to spawning, collision, or material processing.
- No changes to server module or GameManager signals.
- No UI or gameplay changes.
