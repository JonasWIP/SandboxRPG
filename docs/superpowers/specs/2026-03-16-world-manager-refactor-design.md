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

```csharp
public partial class WorldManager : Node3D
{
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
        _players.SpawnAll();
        _items.Sync();
        _structures.Sync();
        _worldObjects.SyncAll();
    }
}
```

### ModelRegistry.cs

Static class. Owns the PackedScene cache and `ApplyMaterials`.

```csharp
public static class ModelRegistry
{
    private static readonly Dictionary<string, PackedScene> _cache = new();

    public static PackedScene Get(string path)
    {
        if (!_cache.TryGetValue(path, out var scene))
            _cache[path] = scene = ResourceLoader.Load<PackedScene>(path);
        return scene;
    }

    public static void ApplyMaterials(Node root, Color? color = null) { ... }
}
```

All spawners call `ModelRegistry.Get(path).Instantiate<Node3D>()` instead of
`ResourceLoader.Load<PackedScene>(path).Instantiate<Node3D>()`.

### Spawner Classes

Plain C# classes (not Nodes). Constructor signature:

```csharp
public XxxSpawner(Node3D parent, GameManager gm)
```

- `parent` — used for `parent.AddChild(node)`
- `gm` — used for `gm.GetAll*()`, `gm.GetLocalPlayer()`, etc.

Each spawner owns its own entity dictionary (`Dictionary<ulong, Node3D>` or
equivalent).

### BuildSystem.cs compatibility

`BuildSystem` currently calls `WorldManager.StructureFallbackMesh`,
`WorldManager.StructureModelPath`, and `WorldManager.StructureYOffset`.
These move to `StructureSpawner` as `public static` methods. Call sites in
`BuildSystem.cs` are updated accordingly (3 references).

## Data Flow

```
GameManager signals
    SubscriptionApplied  → _players.SpawnAll()
                         → _items.Sync()
                         → _structures.Sync()
                         → _worldObjects.SyncAll()
    PlayerUpdated(id)    → _players.OnUpdated(id)
    PlayerRemoved(id)    → _players.OnRemoved(id)
    WorldItemChanged     → _items.Sync()
    StructureChanged     → _structures.Sync()
    WorldObjectUpdated   → _worldObjects.OnUpdated(id, removed)
```

## Optimisation

`ModelRegistry.Get()` ensures each unique GLB path is loaded once per session
and reused for all instances. This eliminates repeated disk reads when many
world objects of the same type are spawned (e.g. hundreds of pine trees).

## Out of Scope

- No logic changes to spawning, collision, or material processing.
- No changes to server module or GameManager signals.
- No UI or gameplay changes.
