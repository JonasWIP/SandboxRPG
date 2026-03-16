# WorldManager Refactor + Model Caching Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split WorldManager.cs into focused spawner classes and add a ModelRegistry for resource caching.

**Architecture:** A thin `WorldManager` coordinator wires GameManager signals to four plain-C# spawner classes (`PlayerSpawner`, `WorldItemSpawner`, `StructureSpawner`, `WorldObjectSpawner`). A static `ModelRegistry` class owns all GLB loading (cached by path) and the `ApplyMaterials` helper. `BuildSystem` is updated to reference static methods that moved from `WorldManager` to `StructureSpawner`.

**Tech Stack:** Godot 4.6.1 C#, .NET 8, SpacetimeDB ClientSDK 2.0.1

**Spec:** `docs/superpowers/specs/2026-03-16-world-manager-refactor-design.md`

---

## Chunk 1: ModelRegistry

### Task 1: Create ModelRegistry.cs

**Files:**
- Create: `client/scripts/world/ModelRegistry.cs`

- [ ] **Step 1: Create the file**

```csharp
using Godot;
using System.Collections.Generic;

namespace SandboxRPG;

/// <summary>
/// Caches loaded PackedScenes by path so each GLB is read from disk only once.
/// Also owns ApplyMaterials since it is the sole consumer of material overrides.
/// </summary>
public static class ModelRegistry
{
    private static readonly Dictionary<string, PackedScene> _cache = new();

    /// <summary>
    /// Returns the cached PackedScene for <paramref name="path"/>.
    /// Caller must check <c>ResourceLoader.Exists(path)</c> before calling —
    /// if the asset is missing ResourceLoader.Load returns null, which is cached.
    /// </summary>
    public static PackedScene? Get(string path)
    {
        if (!_cache.TryGetValue(path, out var scene))
            _cache[path] = scene = ResourceLoader.Load<PackedScene>(path);
        return scene;
    }

    /// <summary>
    /// Recursively processes every MeshInstance3D surface under <paramref name="root"/>:
    /// duplicates the material, zeroes Metallic, then either applies <paramref name="color"/>
    /// or dims AlbedoColor by 0.85.
    /// </summary>
    public static void ApplyMaterials(Node root, Color? color = null)
    {
        if (root is MeshInstance3D mi && mi.Mesh != null)
        {
            for (int surf = 0; surf < mi.Mesh.GetSurfaceCount(); surf++)
            {
                var mat = mi.GetActiveMaterial(surf);
                if (mat is not BaseMaterial3D bm) continue;
                var dup = (BaseMaterial3D)bm.Duplicate();
                dup.Metallic    = 0f;
                dup.AlbedoColor = color ?? dup.AlbedoColor * 0.85f;
                mi.SetSurfaceOverrideMaterial(surf, dup);
            }
        }
        foreach (Node child in root.GetChildren())
            ApplyMaterials(child, color);
    }
}
```

- [ ] **Step 2: Build to verify no errors**

```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: 0 errors, 0 warnings (WorldManager.cs still compiles with its own ApplyMaterials for now).

- [ ] **Step 3: Commit**

```bash
git add client/scripts/world/ModelRegistry.cs
git commit -m "feat: add ModelRegistry with PackedScene cache and ApplyMaterials"
```

---

## Chunk 2: Spawner Classes

### Task 2: Create WorldItemSpawner.cs

**Files:**
- Create: `client/scripts/world/WorldItemSpawner.cs`

- [ ] **Step 1: Create the file**

```csharp
using Godot;
using SpacetimeDB.Types;
using System.Collections.Generic;

namespace SandboxRPG;

public class WorldItemSpawner
{
    private readonly Node3D _parent;
    private readonly GameManager _gm;
    private readonly Dictionary<ulong, Node3D> _worldItems = new();

    public WorldItemSpawner(Node3D parent, GameManager gm)
    {
        _parent = parent;
        _gm     = gm;
    }

    public void Sync()
    {
        var currentIds = new HashSet<ulong>();

        foreach (var item in _gm.GetAllWorldItems())
        {
            currentIds.Add(item.Id);
            if (!_worldItems.ContainsKey(item.Id))
            {
                var visual = CreateWorldItemVisual(item);
                _parent.AddChild(visual);
                _worldItems[item.Id] = visual;
            }
        }

        var toRemove = new List<ulong>();
        foreach (var kvp in _worldItems)
            if (!currentIds.Contains(kvp.Key))
            {
                kvp.Value.QueueFree();
                toRemove.Add(kvp.Key);
            }
        foreach (var id in toRemove)
            _worldItems.Remove(id);
    }

    private static Node3D CreateWorldItemVisual(WorldItem item)
    {
        var body = new StaticBody3D { Name = $"WorldItem_{item.Id}", CollisionLayer = 2, CollisionMask = 0 };

        var modelPath = ModelPath(item.ItemType);
        if (modelPath != null && ResourceLoader.Exists(modelPath))
        {
            var model = ModelRegistry.Get(modelPath)!.Instantiate<Node3D>();
            model.Position = new Vector3(0, 0.1f, 0);
            ModelRegistry.ApplyMaterials(model);
            body.AddChild(model);
        }
        else
        {
            body.AddChild(CreateFallbackMesh(item.ItemType));
        }

        body.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = 0.2f } });
        body.AddChild(new Label3D
        {
            Text        = $"{item.ItemType} x{item.Quantity}",
            FontSize    = 32,
            Billboard   = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Position    = new Vector3(0, 0.5f, 0),
        });

        float groundY = Terrain.HeightAt(item.PosX, item.PosZ);
        body.Position = new Vector3(item.PosX, groundY + 0.1f, item.PosZ);
        body.SetMeta("world_item_id", (long)item.Id);
        body.SetMeta("item_type", item.ItemType);
        return body;
    }

    private static string? ModelPath(string itemType) => itemType switch
    {
        "wood"   => "res://assets/models/survival/resource-wood.glb",
        "stone"  => "res://assets/models/survival/resource-stone.glb",
        "planks" => "res://assets/models/survival/resource-planks.glb",
        _        => null,
    };

    private static MeshInstance3D CreateFallbackMesh(string itemType)
    {
        var mesh = new MeshInstance3D
        {
            Mesh     = new BoxMesh { Size = new Vector3(0.4f, 0.4f, 0.4f) },
            Position = new Vector3(0, 0.2f, 0),
        };
        mesh.MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = itemType switch
            {
                "wood"  => new Color(0.6f, 0.4f, 0.2f),
                "stone" => new Color(0.5f, 0.5f, 0.55f),
                "iron"  => new Color(0.7f, 0.7f, 0.75f),
                _       => new Color(0.8f, 0.8f, 0.2f),
            },
            Roughness = 0.9f,
        };
        return mesh;
    }
}
```

- [ ] **Step 2: Build**

```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add client/scripts/world/WorldItemSpawner.cs
git commit -m "feat: extract WorldItemSpawner from WorldManager"
```

---

### Task 3: Create StructureSpawner.cs

**Files:**
- Create: `client/scripts/world/StructureSpawner.cs`

Note: The three static methods (`StructureModelPath`, `StructureFallbackMesh`, `StructureYOffset`) move here from WorldManager. They remain `public static` so BuildSystem can call them after its reference is updated in Task 6.

- [ ] **Step 1: Create the file**

```csharp
using Godot;
using SpacetimeDB.Types;
using System.Collections.Generic;

namespace SandboxRPG;

public class StructureSpawner
{
    private readonly Node3D _parent;
    private readonly GameManager _gm;
    private readonly Dictionary<ulong, StaticBody3D> _structures = new();

    public StructureSpawner(Node3D parent, GameManager gm)
    {
        _parent = parent;
        _gm     = gm;
    }

    public void Sync()
    {
        var currentIds = new HashSet<ulong>();

        foreach (var structure in _gm.GetAllStructures())
        {
            currentIds.Add(structure.Id);
            if (!_structures.ContainsKey(structure.Id))
            {
                var visual = CreateStructureVisual(structure);
                _parent.AddChild(visual);
                _structures[structure.Id] = visual;
            }
        }

        var toRemove = new List<ulong>();
        foreach (var kvp in _structures)
            if (!currentIds.Contains(kvp.Key))
            {
                kvp.Value.QueueFree();
                toRemove.Add(kvp.Key);
            }
        foreach (var id in toRemove)
            _structures.Remove(id);
    }

    private static StaticBody3D CreateStructureVisual(PlacedStructure structure)
    {
        var body = new StaticBody3D { Name = $"Structure_{structure.Id}", CollisionLayer = 1, CollisionMask = 1 };

        string? modelPath = StructureModelPath(structure.StructureType);
        if (modelPath != null && ResourceLoader.Exists(modelPath))
        {
            var visual = ModelRegistry.Get(modelPath)!.Instantiate<Node3D>();
            Color? tint = structure.StructureType switch
            {
                "wood_wall" or "wood_floor" or "wood_door" => new Color(1.0f, 0.78f, 0.55f),
                "stone_wall" or "stone_floor"              => new Color(0.82f, 0.82f, 0.88f),
                _                                          => null,
            };
            ModelRegistry.ApplyMaterials(visual, tint);
            body.AddChild(visual);
        }
        else
        {
            bool isStone = structure.StructureType.Contains("stone");
            var mesh = new MeshInstance3D { Mesh = StructureFallbackMesh(structure.StructureType) };
            mesh.MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = structure.StructureType switch
                {
                    "campfire"     => new Color(0.8f, 0.3f,  0.1f),
                    "workbench"    => new Color(0.5f, 0.35f, 0.2f),
                    "chest"        => new Color(0.55f, 0.4f, 0.25f),
                    _ when isStone => new Color(0.55f, 0.55f, 0.6f),
                    _              => new Color(0.6f, 0.45f, 0.25f),
                },
                Roughness = 0.85f,
            };
            mesh.Position = new Vector3(0, StructureYOffset(structure.StructureType), 0);
            body.AddChild(mesh);
        }

        var (sz, sc) = GetBoxShape(structure.StructureType);
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = sz }, Position = sc });
        body.Position = new Vector3(structure.PosX, structure.PosY, structure.PosZ);
        body.Rotation = new Vector3(0, structure.RotY, 0);
        body.SetMeta("structure_id",   (long)structure.Id);
        body.SetMeta("structure_type", structure.StructureType);
        body.SetMeta("owner_id",       structure.OwnerId.ToString());

        return body;
    }

    // ── Public static lookup tables (also used by BuildSystem) ────────────────

    public static string? StructureModelPath(string t) => t switch
    {
        "wood_wall"  or "stone_wall"  => "res://assets/models/building/wall.glb",
        "wood_floor" or "stone_floor" => "res://assets/models/building/floor.glb",
        "wood_door"                   => "res://assets/models/building/wall-doorway-square.glb",
        "campfire"                    => "res://assets/models/survival/campfire-pit.glb",
        "workbench"                   => "res://assets/models/survival/workbench.glb",
        "chest"                       => "res://assets/models/survival/chest.glb",
        _                             => null,
    };

    public static Mesh StructureFallbackMesh(string t) => t switch
    {
        "wood_wall" or "stone_wall"   => new BoxMesh { Size = new Vector3(2f, 2.5f, 0.2f) },
        "wood_floor" or "stone_floor" => new BoxMesh { Size = new Vector3(2f, 0.1f, 2f) },
        "wood_door"                   => new BoxMesh { Size = new Vector3(1f, 2.2f, 0.15f) },
        "campfire"                    => new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.5f, Height = 0.3f },
        "workbench"                   => new BoxMesh { Size = new Vector3(1.2f, 0.8f, 0.8f) },
        "chest"                       => new BoxMesh { Size = new Vector3(0.8f, 0.6f, 0.5f) },
        _                             => new BoxMesh { Size = new Vector3(1f, 1f, 1f) },
    };

    public static float StructureYOffset(string t) => t switch
    {
        "wood_wall" or "stone_wall"   => 1.25f,
        "wood_floor" or "stone_floor" => 0.05f,
        "wood_door"                   => 1.1f,
        "campfire"                    => 0.15f,
        "workbench"                   => 0.4f,
        "chest"                       => 0.3f,
        _                             => 0.5f,
    };

    private static (Vector3 size, Vector3 center) GetBoxShape(string t) => t switch
    {
        "wood_wall"  or "stone_wall"  => (new Vector3(0.25f, 2.4f, 2.0f), new Vector3(0, 1.2f, 0)),
        "wood_floor" or "stone_floor" => (new Vector3(2.0f,  0.1f, 2.0f), new Vector3(0, 0.05f, 0)),
        "wood_door"                   => (new Vector3(0.25f, 2.4f, 2.0f), new Vector3(0, 1.2f, 0)),
        "campfire"                    => (new Vector3(0.8f,  0.4f, 0.8f),  new Vector3(0, 0.2f,  0)),
        "workbench"                   => (new Vector3(1.2f,  0.8f, 0.6f),  new Vector3(0, 0.4f,  0)),
        "chest"                       => (new Vector3(0.8f,  0.6f, 0.6f),  new Vector3(0, 0.3f,  0)),
        _                             => (new Vector3(1.0f,  1.0f, 1.0f),  new Vector3(0, 0.5f,  0)),
    };
}
```

- [ ] **Step 2: Build**

```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add client/scripts/world/StructureSpawner.cs
git commit -m "feat: extract StructureSpawner from WorldManager"
```

---

### Task 4: Create WorldObjectSpawner.cs

**Files:**
- Create: `client/scripts/world/WorldObjectSpawner.cs`

- [ ] **Step 1: Create the file**

```csharp
using Godot;
using SpacetimeDB.Types;
using System.Collections.Generic;
using System.Linq;

namespace SandboxRPG;

public class WorldObjectSpawner
{
    private readonly Node3D _parent;
    private readonly GameManager _gm;
    private readonly Dictionary<ulong, Node3D> _worldObjects = new();

    public WorldObjectSpawner(Node3D parent, GameManager gm)
    {
        _parent = parent;
        _gm     = gm;
    }

    /// <summary>Initial load — adds any objects not yet in the dict. Does not remove.</summary>
    public void SyncAll()
    {
        foreach (var obj in _gm.GetAllWorldObjects())
        {
            if (!_worldObjects.ContainsKey(obj.Id))
            {
                var visual = CreateWorldObjectVisual(obj);
                _parent.AddChild(visual);
                _worldObjects[obj.Id] = visual;
            }
        }
    }

    /// <summary>Delta update from WorldObjectUpdated signal.</summary>
    public void OnUpdated(long id, bool removed)
    {
        ulong uid = (ulong)id;
        if (removed)
        {
            if (_worldObjects.TryGetValue(uid, out var existing))
            {
                existing.QueueFree();
                _worldObjects.Remove(uid);
            }
            return;
        }

        var obj = _gm.GetWorldObject(uid);
        if (obj is null) return;

        if (!_worldObjects.ContainsKey(uid))
        {
            var visual = CreateWorldObjectVisual(obj);
            _parent.AddChild(visual);
            _worldObjects[uid] = visual;
        }
    }

    private static Node3D CreateWorldObjectVisual(WorldObject obj)
    {
        var body = new StaticBody3D { Name = $"WorldObject_{obj.Id}" };

        string? modelPath = obj.ObjectType switch
        {
            "tree_pine"  => "res://assets/models/nature/tree_pineRoundA.glb",
            "tree_dead"  => "res://assets/models/nature/tree_thin_dark.glb",
            "tree_palm"  => "res://assets/models/nature/tree_palmTall.glb",
            "rock_large" => "res://assets/models/nature/rock_largeA.glb",
            "rock_small" => "res://assets/models/nature/rock_smallA.glb",
            "bush"       => "res://assets/models/nature/plant_bush.glb",
            _            => null,
        };

        float modelScale = obj.ObjectType switch
        {
            "tree_pine" or "tree_palm" => 2.5f,
            "tree_dead"                => 2.0f,
            "rock_large"               => 2.0f,
            "rock_small"               => 1.8f,
            "bush"                     => 1.5f,
            _                          => 1.0f,
        };

        if (modelPath != null && ResourceLoader.Exists(modelPath))
        {
            var model = ModelRegistry.Get(modelPath)!.Instantiate<Node3D>();
            model.Scale = Vector3.One * modelScale;
            Color? tint = obj.ObjectType is "rock_large" or "rock_small" ? new Color(0.6f, 0.6f, 0.6f) : null;
            ModelRegistry.ApplyMaterials(model, tint);
            body.AddChild(model);
            body.AddChild(new CollisionShape3D { Shape = BuildConvexShape(model, modelScale) });
        }
        else
        {
            body.AddChild(new MeshInstance3D
            {
                Mesh     = new BoxMesh { Size = new Vector3(0.8f, 1.5f, 0.8f) * modelScale },
                Position = new Vector3(0, 0.75f * modelScale, 0),
            });
            var shapeSize = obj.ObjectType switch
            {
                "tree_pine" or "tree_palm" => new Vector3(1.2f, 6.0f, 1.2f),
                "tree_dead"                => new Vector3(1.0f, 5.0f, 1.0f),
                "rock_large"               => new Vector3(2.4f, 1.6f, 2.4f),
                "rock_small"               => new Vector3(1.1f, 0.7f, 1.1f),
                "bush"                     => new Vector3(1.5f, 1.0f, 1.5f),
                _                          => new Vector3(0.8f, 1.0f, 0.8f),
            };
            body.AddChild(new CollisionShape3D
            {
                Shape    = new BoxShape3D { Size = shapeSize },
                Position = new Vector3(0, shapeSize.Y / 2f, 0),
            });
        }

        float groundY = Terrain.HeightAt(obj.PosX, obj.PosZ);
        body.Position = new Vector3(obj.PosX, groundY, obj.PosZ);
        body.Rotation = new Vector3(0, obj.RotY, 0);
        body.AddToGroup("world_object");
        body.SetMeta("world_object_id", (long)obj.Id);
        body.SetMeta("object_type", obj.ObjectType);

        return body;
    }

    private static ConvexPolygonShape3D BuildConvexShape(Node3D model, float scale)
    {
        var pts = new List<Vector3>();
        foreach (var mi in model.FindChildren("*", "MeshInstance3D", owned: false).OfType<MeshInstance3D>())
        {
            if (mi.Mesh is not ArrayMesh arrayMesh) continue;
            var shape = arrayMesh.CreateConvexShape(clean: true, simplify: false);
            pts.AddRange(shape.Points);
        }
        for (int i = 0; i < pts.Count; i++)
            pts[i] *= scale;
        return new ConvexPolygonShape3D { Points = pts.ToArray() };
    }
}
```

- [ ] **Step 2: Build**

```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add client/scripts/world/WorldObjectSpawner.cs
git commit -m "feat: extract WorldObjectSpawner from WorldManager"
```

---

### Task 5: Create PlayerSpawner.cs

**Files:**
- Create: `client/scripts/world/PlayerSpawner.cs`

Notes:
- Dict is `Dictionary<string, RemotePlayer>` (keyed by identity hex string).
- `PlayerController` is a `CharacterBody3D` that self-initialises — spawner just news it, sets transform/color, and calls `parent.AddChild`.
- `PlayerPrefs.LoadColorHex()` is the fallback color (static, no wiring needed).

- [ ] **Step 1: Create the file**

```csharp
using Godot;
using SpacetimeDB.Types;
using System.Collections.Generic;

namespace SandboxRPG;

public class PlayerSpawner
{
    private readonly Node3D _parent;
    private readonly GameManager _gm;
    private readonly Dictionary<string, RemotePlayer> _remotePlayers = new();

    private PlayerController? _localPlayer;

    public PlayerSpawner(Node3D parent, GameManager gm)
    {
        _parent = parent;
        _gm     = gm;
    }

    public void SpawnAll()
    {
        SpawnLocalPlayer();

        foreach (var player in _gm.GetAllPlayers())
        {
            if (player.Identity != _gm.LocalIdentity && player.IsOnline)
                SpawnOrUpdateRemotePlayer(player);
        }
    }

    public void OnUpdated(string identityHex)
    {
        foreach (var player in _gm.GetAllPlayers())
        {
            if (player.Identity.ToString() != identityHex) continue;

            if (player.Identity == _gm.LocalIdentity)
            {
                _localPlayer?.ApplyColor(player.ColorHex ?? PlayerPrefs.LoadColorHex());
                return;
            }

            if (player.IsOnline)
                SpawnOrUpdateRemotePlayer(player);
            else
                RemoveRemotePlayer(identityHex);
            break;
        }
    }

    public void OnRemoved(string identityHex) => RemoveRemotePlayer(identityHex);

    private void SpawnLocalPlayer()
    {
        var p = _gm.GetLocalPlayer();
        if (p == null) return;

        _localPlayer = new PlayerController { Name = "LocalPlayer" };
        _parent.AddChild(_localPlayer);
        _localPlayer.GlobalPosition = new Vector3(p.PosX, p.PosY, p.PosZ);
        _localPlayer.Rotation = new Vector3(0, p.RotY, 0);
        _localPlayer.ApplyColor(p.ColorHex ?? PlayerPrefs.LoadColorHex());

        GD.Print($"[PlayerSpawner] Local player spawned at ({p.PosX}, {p.PosY}, {p.PosZ})");
    }

    private void SpawnOrUpdateRemotePlayer(Player player)
    {
        string id       = player.Identity.ToString();
        string colorHex = player.ColorHex ?? "#E6804D";

        if (_remotePlayers.TryGetValue(id, out var existing))
        {
            existing.UpdateFromServer(player.PosX, player.PosY, player.PosZ, player.RotY, player.Name, colorHex);
        }
        else
        {
            var remote = new RemotePlayer
            {
                Name       = $"Remote_{id[..8]}",
                IdentityHex = id,
                PlayerName = player.Name,
                ColorHex   = colorHex,
            };
            _parent.AddChild(remote);
            remote.GlobalPosition = new Vector3(player.PosX, player.PosY, player.PosZ);
            remote.Rotation = new Vector3(0, player.RotY, 0);
            _remotePlayers[id] = remote;
            GD.Print($"[PlayerSpawner] Remote player spawned: {player.Name}");
        }
    }

    private void RemoveRemotePlayer(string identityHex)
    {
        if (_remotePlayers.TryGetValue(identityHex, out var remote))
        {
            remote.QueueFree();
            _remotePlayers.Remove(identityHex);
        }
    }
}
```

- [ ] **Step 2: Build**

```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add client/scripts/world/PlayerSpawner.cs
git commit -m "feat: extract PlayerSpawner from WorldManager"
```

---

## Chunk 3: Wire Up + Cleanup

### Task 6: Rewrite WorldManager.cs as thin coordinator

**Files:**
- Modify: `client/scripts/world/WorldManager.cs`

Replace the entire file contents. The old `ApplyMaterials`, `StructureModelPath`, `StructureFallbackMesh`, `StructureYOffset`, `StructureFallbackMesh`, `GetStructureBoxShape`, `BuildConvexShape`, and all entity dicts are now in the spawner classes.

- [ ] **Step 1: Replace WorldManager.cs**

```csharp
using Godot;

namespace SandboxRPG;

/// <summary>
/// Thin coordinator — wires GameManager signals to the spawner classes.
/// </summary>
public partial class WorldManager : Node3D
{
    private bool _worldSpawned;

    private PlayerSpawner _players = null!;
    private WorldItemSpawner _items = null!;
    private StructureSpawner _structures = null!;
    private WorldObjectSpawner _worldObjects = null!;

    public override void _Ready()
    {
        var gm = GameManager.Instance;
        _players      = new PlayerSpawner(this, gm);
        _items        = new WorldItemSpawner(this, gm);
        _structures   = new StructureSpawner(this, gm);
        _worldObjects = new WorldObjectSpawner(this, gm);

        gm.SubscriptionApplied += OnSubscriptionApplied;
        gm.PlayerUpdated       += id => _players.OnUpdated(id);
        gm.PlayerRemoved       += id => _players.OnRemoved(id);
        gm.WorldItemChanged    += _items.Sync;
        gm.StructureChanged    += _structures.Sync;
        gm.WorldObjectUpdated  += _worldObjects.OnUpdated;

        if (gm.IsConnected && gm.GetLocalPlayer() != null)
            OnSubscriptionApplied();
    }

    private void OnSubscriptionApplied()
    {
        if (_worldSpawned) return;
        _worldSpawned = true;
        GD.Print("[WorldManager] Initial data received, spawning world...");
        _players.SpawnAll();
        _items.Sync();
        _structures.Sync();
        _worldObjects.SyncAll();
    }
}
```

- [ ] **Step 2: Build**

```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: 0 errors. If errors appear about missing static methods (e.g. `WorldManager.StructureModelPath`), that is expected — Task 7 fixes BuildSystem.

- [ ] **Step 3: Commit**

```bash
git add client/scripts/world/WorldManager.cs
git commit -m "refactor: WorldManager reduced to thin signal coordinator"
```

---

### Task 7: Update BuildSystem.cs

**Files:**
- Modify: `client/scripts/building/BuildSystem.cs`

Three references to `WorldManager.*` statics change to `StructureSpawner.*`.
The `ResourceLoader.Load` call at line ~107 changes to `ModelRegistry.Get`.

- [ ] **Step 1: Update the three static method references and the Load call**

In `CreateGhostPreview` (around line 100–127), change:

```csharp
// BEFORE (BuildSystem.cs CreateGhostPreview, lines 104-122)
var modelPath = WorldManager.StructureModelPath(structureType);
if (modelPath != null && ResourceLoader.Exists(modelPath))
{
    var model = ResourceLoader.Load<PackedScene>(modelPath).Instantiate<Node3D>();
    ApplyGhostMaterial(model);                          // keep this line unchanged
    _ghostPreview.AddChild(model);                      // keep this line unchanged
}
else
{
    var mesh = new MeshInstance3D
    {
        Mesh             = WorldManager.StructureFallbackMesh(structureType),
        MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor  = new Color(0.3f, 0.8f, 0.3f, 0.4f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        },
    };
    mesh.Position = new Vector3(0, WorldManager.StructureYOffset(structureType), 0);
    _ghostPreview.AddChild(mesh);                       // keep this line unchanged
}
```

```csharp
// AFTER — only the 4 highlighted lines change; everything else is identical
var modelPath = StructureSpawner.StructureModelPath(structureType);      // changed
if (modelPath != null && ResourceLoader.Exists(modelPath))
{
    var model = ModelRegistry.Get(modelPath)!.Instantiate<Node3D>();     // changed
    ApplyGhostMaterial(model);
    _ghostPreview.AddChild(model);
}
else
{
    var mesh = new MeshInstance3D
    {
        Mesh             = StructureSpawner.StructureFallbackMesh(structureType),  // changed
        MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor  = new Color(0.3f, 0.8f, 0.3f, 0.4f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        },
    };
    mesh.Position = new Vector3(0, StructureSpawner.StructureYOffset(structureType), 0);  // changed
    _ghostPreview.AddChild(mesh);
}
```

- [ ] **Step 2: Build**

```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: 0 errors, 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add client/scripts/building/BuildSystem.cs
git commit -m "refactor: BuildSystem uses StructureSpawner statics and ModelRegistry"
```

---

### Task 8: Final verification

- [ ] **Step 1: Clean build**

```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: 0 errors, 0 warnings.

- [ ] **Step 2: Confirm old static methods are gone from WorldManager**

```bash
grep -n "StructureModelPath\|StructureFallbackMesh\|StructureYOffset\|ApplyMaterials\|BuildConvexShape" client/scripts/world/WorldManager.cs
```
Expected: no output (all moved to spawner classes / ModelRegistry).

- [ ] **Step 3: Confirm no stale WorldManager.* references in BuildSystem**

```bash
grep -n "WorldManager\." client/scripts/building/BuildSystem.cs
```
Expected: no output.

- [ ] **Step 4: Commit final state**

```bash
git add -A
git commit -m "chore: WorldManager refactor complete — 6 focused files, ModelRegistry caching"
```
