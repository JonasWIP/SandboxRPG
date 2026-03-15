# Starting Area Redesign Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the flat green placeholder world with a low-poly coastal starting area — beach shoreline, gentle terrain slope, Kenney 3D assets, and fully server-authoritative world objects.

**Architecture:** Three independent phases — (1) server WorldObject table + Kenney model visuals, (2) terrain mesh + water plane + textures, (3) BuildSystem slope snap. Each phase can be committed and tested individually without breaking the others.

**Tech Stack:** SpacetimeDB 2.0 (C# WASM server), Godot 4.6.1 C# client, Kenney.nl free asset packs (.glb), StandardMaterial3D, GLSL terrain blend shader.

---

## Chunk 1: Server World Objects

### Task 1: Add WorldObject table

**Files:**
- Modify: `server/Tables.cs`

- [ ] **Step 1: Add WorldObject struct** at the end of `Tables.cs`, before the closing `}`:

```csharp
/// <summary>Harvestable and decorative world objects (trees, rocks, bushes).
/// Seeded on Init, deleted when health reaches 0.</summary>
[Table(Name = "world_object", Public = true)]
public partial struct WorldObject
{
    [AutoInc][PrimaryKey]
    public ulong Id;
    public string ObjectType;   // "tree_pine", "rock_large", "rock_small", "tree_dead", "bush"
    public float PosX;
    public float PosY;
    public float PosZ;
    public float RotY;          // Y-axis rotation in radians
    public uint Health;
    public uint MaxHealth;
}
```

- [ ] **Step 2: Build server to verify no compile errors**

```bash
cd server && spacetime build
```
Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 3: Commit**

```bash
git add server/Tables.cs
git commit -m "feat: add WorldObject table for harvestable world objects"
```

---

### Task 2: Add WorldReducers.cs

**Files:**
- Create: `server/WorldReducers.cs`

- [ ] **Step 1: Create the file**

```csharp
using SpacetimeDB;

namespace SandboxRPG.Server;

public static partial class Module
{
    [Reducer]
    public static void DamageWorldObject(ReducerContext ctx, ulong id, uint damage)
    {
        var obj = ctx.Db.WorldObject.Id.Find(id);
        if (obj is null) return;
        var o = obj.Value;

        uint newHealth = o.Health <= damage ? 0 : o.Health - damage;

        // STDB 2.0: no UpdateByField — delete then reinsert
        ctx.Db.WorldObject.Delete(o);

        if (newHealth == 0)
        {
            // Drop items at the object's position
            ctx.Db.WorldItem.Insert(new WorldItem
            {
                ItemType = DropTypeFor(o.ObjectType),
                Quantity = 1,
                PosX = o.PosX,
                PosY = o.PosY,
                PosZ = o.PosZ,
            });
        }
        else
        {
            // Reinsert with updated health. Note: AutoInc assigns a new Id on
            // each insert — the client will see a delete+insert pair per hit.
            ctx.Db.WorldObject.Insert(new WorldObject
            {
                ObjectType = o.ObjectType,
                PosX = o.PosX, PosY = o.PosY, PosZ = o.PosZ,
                RotY = o.RotY,
                Health = newHealth,
                MaxHealth = o.MaxHealth,
            });
        }
    }

    private static string DropTypeFor(string objectType) => objectType switch
    {
        "rock_large" or "rock_small" => "stone",
        _ => "wood",    // trees, stumps, bushes
    };
}
```

- [ ] **Step 2: Build to verify**

```bash
cd server && spacetime build
```
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 3: Commit**

```bash
git add server/WorldReducers.cs
git commit -m "feat: add DamageWorldObject reducer with item drop"
```

---

### Task 3: Update Lifecycle.cs — seed world objects + fix spawn Y + fix item positions

**Files:**
- Modify: `server/Lifecycle.cs`

- [ ] **Step 1: Add TerrainHeightAt helper** — add this private static method after `SeedWorldItems` (line 100):

```csharp
/// <summary>Mirrors the client Terrain.HeightAt formula (no Mathf on server).</summary>
private static float TerrainHeightAt(float x, float z)
{
    float t = Math.Clamp((z - 5f) / 20f, 0f, 1f);
    return t * t * (3f - 2f * t) * 4f;   // SmoothStep(0, 4, t)
}
```

- [ ] **Step 2: Add SeedWorldObjects helper** — add after `TerrainHeightAt`:

```csharp
private static void SeedWorldObjects(ReducerContext ctx)
{
    var rng = new Random(42);   // fixed seed — same world every restart

    // Helper: random position in range with terrain-height Y
    WorldObject MakeObject(string type, float xMin, float xMax, float zMin, float zMax, uint hp)
    {
        float x = (float)(rng.NextDouble() * (xMax - xMin) + xMin);
        float z = (float)(rng.NextDouble() * (zMax - zMin) + zMin);
        return new WorldObject
        {
            ObjectType = type,
            PosX = x,
            PosY = TerrainHeightAt(x, z),
            PosZ = z,
            RotY = (float)(rng.NextDouble() * Math.PI * 2),
            Health = hp,
            MaxHealth = hp,
        };
    }

    // Pine trees — inland plateau
    for (int i = 0; i < 50; i++)
        ctx.Db.WorldObject.Insert(MakeObject("tree_pine", -40f, 40f, 20f, 48f, 100));

    // Dead trees / stumps — at treeline
    for (int i = 0; i < 12; i++)
        ctx.Db.WorldObject.Insert(MakeObject("tree_dead", -30f, 30f, 15f, 25f, 60));

    // Large rocks — scattered beach and hillside
    for (int i = 0; i < 18; i++)
        ctx.Db.WorldObject.Insert(MakeObject("rock_large", -40f, 40f, 0f, 30f, 150));

    // Small rocks — scattered everywhere
    for (int i = 0; i < 15; i++)
        ctx.Db.WorldObject.Insert(MakeObject("rock_small", -45f, 45f, -5f, 35f, 80));

    // Bushes — near spawn clearing
    for (int i = 0; i < 10; i++)
        ctx.Db.WorldObject.Insert(MakeObject("bush", -20f, 20f, 5f, 18f, 30));

    Log.Info("Seeded world objects.");
}
```

- [ ] **Step 3: Call SeedWorldObjects from Init** — update the `Init` reducer body (line 13–18):

```csharp
[Reducer(ReducerKind.Init)]
public static void Init(ReducerContext ctx)
{
    Log.Info("SandboxRPG server module initialized!");
    SeedRecipes(ctx);
    SeedWorldItems(ctx);
    SeedWorldObjects(ctx);
}
```

- [ ] **Step 4: Update SeedWorldItems Y positions** — replace the hardcoded `0.5f` values so items sit on terrain surface. Replace the full `SeedWorldItems` method body:

```csharp
private static void SeedWorldItems(ReducerContext ctx)
{
    ctx.Db.WorldItem.Insert(new WorldItem { ItemType = "wood",  Quantity = 5, PosX =  3f, PosY = TerrainHeightAt( 3f,  3f) + 0.2f, PosZ =  3f });
    ctx.Db.WorldItem.Insert(new WorldItem { ItemType = "stone", Quantity = 3, PosX = -4f, PosY = TerrainHeightAt(-4f,  2f) + 0.2f, PosZ =  2f });
    ctx.Db.WorldItem.Insert(new WorldItem { ItemType = "wood",  Quantity = 8, PosX =  7f, PosY = TerrainHeightAt( 7f, -5f) + 0.2f, PosZ = -5f });
    ctx.Db.WorldItem.Insert(new WorldItem { ItemType = "iron",  Quantity = 2, PosX = -8f, PosY = TerrainHeightAt(-8f, -6f) + 0.2f, PosZ = -6f });
    ctx.Db.WorldItem.Insert(new WorldItem { ItemType = "stone", Quantity = 5, PosX = 10f, PosY = TerrainHeightAt(10f,  8f) + 0.2f, PosZ =  8f });

    Log.Info("Seeded starter world items.");
}
```

Note: Z values -5 and -6 are ocean-side (beach/water edge) — items there will be at Y≈0 (beach level). That is fine for starter items.

- [ ] **Step 5: Fix player spawn Y** — in `ClientConnected`, change `PosY = 1f` to `PosY = 0.3f`:

Find this block:
```csharp
ctx.Db.Player.Insert(new Player
{
    ...
    PosY = 1f,
```
Change to:
```csharp
    PosY = 0.3f,
```

Note: During Phase 1 (before terrain is added), players will float 0.3 units above the old flat ground. This is expected and harmless.

- [ ] **Step 6: Add `using System;` to Lifecycle.cs** — `Lifecycle.cs` currently starts with only `using SpacetimeDB;`. Add `using System;` on line 2 (needed for `Math.Clamp` and `Random`).

- [ ] **Step 7: Build to verify**

```bash
cd server && spacetime build
```
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 8: Commit**

```bash
git add server/Lifecycle.cs
git commit -m "feat: seed world objects on init, fix item/player spawn Y positions"
```

---

### Task 4: Publish server and regenerate client bindings

**Files:**
- Modify: `client/scripts/networking/SpacetimeDB/` (auto-generated — do not hand-edit)

- [ ] **Step 1: Ensure SpacetimeDB server is running**

```bash
spacetime start --in-memory
```
If it's already running, skip. If you need to re-login:
```bash
spacetime logout && spacetime login --server-issued-login local --no-browser
```

- [ ] **Step 2: Publish the server module**

```bash
cd server && spacetime publish -b bin/Release/net8.0/wasi-wasm/AppBundle/StdbModule.wasm
```
Expected: `Published successfully.`

- [ ] **Step 3: Regenerate client bindings**

```bash
cd server && spacetime generate --lang csharp \
  --out-dir ../client/scripts/networking/SpacetimeDB \
  --bin-path bin/Release/net8.0/wasi-wasm/AppBundle/StdbModule.wasm
```
Expected: Files written to `client/scripts/networking/SpacetimeDB/`. Verify that `WorldObject.cs` and updated `Reducers.cs` (with `DamageWorldObject`) appear in that directory.

- [ ] **Step 4: Build client to confirm generated bindings compile**

```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 5: Commit**

```bash
git add client/scripts/networking/SpacetimeDB/
git commit -m "chore: regenerate SpacetimeDB bindings for WorldObject + DamageWorldObject"
```

---

## Chunk 2: Client World Objects + Kenney Visuals

### Task 5: Download and set up Kenney assets

**Files:**
- Create: `client/assets/models/nature-kit/gltf/` (and contents)
- Create: `client/assets/models/survival-kit/gltf/` (and contents)
- Create: `client/assets/models/building-kit/gltf/` (and contents)

- [ ] **Step 1: Download the three free packs** from kenney.nl:
  - Nature Kit: `https://kenney.nl/assets/nature-kit`
  - Survival Kit: `https://kenney.nl/assets/survival-kit`
  - Building Kit: `https://kenney.nl/assets/building-kit`

- [ ] **Step 2: For each pack**, locate the folder named `GLTF format` inside the zip and rename it to `gltf`.

- [ ] **Step 3: Copy into project**:
  - Nature Kit `gltf/` → `client/assets/models/nature-kit/gltf/`
  - Survival Kit `gltf/` → `client/assets/models/survival-kit/gltf/`
  - Building Kit `gltf/` → `client/assets/models/building-kit/gltf/`

- [ ] **Step 4: Verify exact filenames** — Kenney sometimes uses hyphens or different capitalisation. Check that these files exist (or note actual filename if different):
  - `nature-kit/gltf/tree_pine.glb`
  - `nature-kit/gltf/tree_dead.glb`
  - `nature-kit/gltf/rock_large.glb`
  - `nature-kit/gltf/rock_small.glb`
  - `nature-kit/gltf/bush.glb`
  - `survival-kit/gltf/campfire.glb`
  - `survival-kit/gltf/chest.glb`
  - `survival-kit/gltf/workbench.glb`
  - `building-kit/gltf/wall_wood.glb`
  - `building-kit/gltf/floor_wood.glb`
  - `building-kit/gltf/wall_stone.glb`
  - `building-kit/gltf/floor_stone.glb`
  - `building-kit/gltf/door_wood.glb`

  If a file has a different name (e.g., `treeConifer.glb`), note it — the `ModelPath` method in Task 7 must match exact disk names.

- [ ] **Step 5: Commit**

```bash
git add client/assets/models/
git commit -m "assets: add Kenney Nature Kit, Survival Kit, Building Kit (.glb models)"
```

---

### Task 6: Wire WorldObject in GameManager.cs

**Files:**
- Modify: `client/scripts/networking/GameManager.cs`

- [ ] **Step 1: Add signal** — in the `=== Signals ===` block (after line 55 `StructureChangedEventHandler`), add:

```csharp
[Signal] public delegate void WorldObjectUpdatedEventHandler(long id, bool removed);
```

Note: `long` not `ulong` — Godot's Variant system does not support `ulong`.

- [ ] **Step 2: Add reducer wrapper** — in the `REDUCER CALLS` block (after line 113 `RemoveBuildStructure`), add:

```csharp
public void DamageWorldObject(ulong id, uint damage) => Conn?.Reducers.DamageWorldObject(id, damage);
```

- [ ] **Step 3: Add data accessor** — in the `DATA ACCESS` block (after line 141 `GetAllRecipes`), add:

```csharp
public IEnumerable<WorldObject> GetAllWorldObjects() { if (Conn != null) foreach (var o in Conn.Db.WorldObject.Iter()) yield return o; }
```

- [ ] **Step 4: Register table callbacks** — in `RegisterCallbacks`, after the `PlacedStructure` OnDelete line (line 254), add:

```csharp
conn.Db.WorldObject.OnInsert += (ctx, obj) =>
    CallDeferred(nameof(EmitWorldObjectUpdated), (long)obj.Id, false);
conn.Db.WorldObject.OnDelete += (ctx, obj) =>
    CallDeferred(nameof(EmitWorldObjectUpdated), (long)obj.Id, true);
```

- [ ] **Step 5: Add deferred emitter** — after `EmitStructureChanged` (line 266), add:

```csharp
private void EmitWorldObjectUpdated(long id, bool removed) => EmitSignal(SignalName.WorldObjectUpdated, id, removed);
```

- [ ] **Step 6: Build to verify**

```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 7: Commit**

```bash
git add client/scripts/networking/GameManager.cs
git commit -m "feat: add WorldObject signal, reducer wrapper, and table callbacks"
```

---

### Task 7: Add WorldObject visuals to WorldManager.cs

**Files:**
- Modify: `client/scripts/world/WorldManager.cs`

- [ ] **Step 1: Add `_worldObjects` dictionary** — in the `=== Tracked entities ===` block (after line 16 `_structures`), add:

```csharp
private readonly Dictionary<ulong, Node3D> _worldObjects = new();
```

- [ ] **Step 2: Subscribe to WorldObjectUpdated signal** — in `_Ready()`, after line 29 (`gm.StructureChanged += OnStructuresChanged`), add:

```csharp
gm.WorldObjectUpdated += OnWorldObjectUpdated;
```

- [ ] **Step 3: Spawn existing objects on subscription** — in `OnSubscriptionApplied()`, after the `OnStructuresChanged()` call (line 57), add:

```csharp
// Spawn all world objects already in the database
foreach (var obj in GameManager.Instance.GetAllWorldObjects())
    CreateWorldObjectVisual(obj);
```

- [ ] **Step 4: Add OnWorldObjectUpdated handler** — add this method after `OnStructuresChanged`:

```csharp
private void OnWorldObjectUpdated(long id, bool removed)
{
    var uid = (ulong)id;
    if (removed)
    {
        if (_worldObjects.TryGetValue(uid, out var node))
        {
            node.QueueFree();
            _worldObjects.Remove(uid);
        }
        return;
    }

    // Guard against duplicate spawns during initial subscription apply
    if (_worldObjects.ContainsKey(uid)) return;

    foreach (var obj in GameManager.Instance.GetAllWorldObjects())
    {
        if (obj.Id == uid)
        {
            CreateWorldObjectVisual(obj);
            return;
        }
    }
}
```

- [ ] **Step 5: Add CreateWorldObjectVisual** — add this method after `OnWorldObjectUpdated`:

```csharp
private void CreateWorldObjectVisual(WorldObject obj)
{
    if (_worldObjects.ContainsKey(obj.Id)) return;

    var path = WorldObjectModelPath(obj.ObjectType);
    if (path == null)
    {
        GD.PrintErr($"[WorldManager] No model path for WorldObject type: {obj.ObjectType}");
        return;
    }

    var scene = ResourceLoader.Load<PackedScene>(path);
    if (scene == null)
    {
        GD.PrintErr($"[WorldManager] Failed to load model: {path}");
        return;
    }

    // Wrap in a StaticBody3D so the InteractionSystem raycast can hit it.
    // Kenney GLB files are visual-only and have no built-in physics bodies.
    var body = new StaticBody3D { Name = $"WorldObj_{obj.Id}" };

    var visual = scene.Instantiate<Node3D>();
    // Rock models have centred origins — offset upward so they sit on the ground
    if (obj.ObjectType is "rock_large" or "rock_small")
        visual.Position = new Vector3(0, 0.3f, 0);
    body.AddChild(visual);

    // Add an approximate box collision shape sized per object type
    var collision = new CollisionShape3D();
    collision.Shape = obj.ObjectType switch
    {
        "tree_pine"  => new BoxShape3D { Size = new Vector3(1f, 4f, 1f) },
        "tree_dead"  => new BoxShape3D { Size = new Vector3(0.8f, 3f, 0.8f) },
        "rock_large" => new BoxShape3D { Size = new Vector3(1.5f, 1.2f, 1.5f) },
        "rock_small" => new BoxShape3D { Size = new Vector3(0.8f, 0.6f, 0.8f) },
        "bush"       => new BoxShape3D { Size = new Vector3(1f, 0.8f, 1f) },
        _            => new BoxShape3D { Size = new Vector3(1f, 1f, 1f) },
    };
    // Centre collision vertically
    collision.Position = new Vector3(0, ((BoxShape3D)collision.Shape).Size.Y / 2f, 0);
    body.AddChild(collision);

    body.GlobalPosition = new Vector3(obj.PosX, obj.PosY, obj.PosZ);
    body.RotationDegrees = new Vector3(0, Mathf.RadToDeg(obj.RotY), 0);

    // Tag for InteractionSystem raycast detection
    body.AddToGroup("world_object");
    body.SetMeta("world_object_id", (long)obj.Id);
    body.SetMeta("object_type", obj.ObjectType);

    AddChild(body);
    _worldObjects[obj.Id] = body;
}

private static string? WorldObjectModelPath(string type) => type switch
{
    "tree_pine"  => "res://assets/models/nature-kit/gltf/tree_pine.glb",
    "tree_dead"  => "res://assets/models/nature-kit/gltf/tree_dead.glb",
    "rock_large" => "res://assets/models/nature-kit/gltf/rock_large.glb",
    "rock_small" => "res://assets/models/nature-kit/gltf/rock_small.glb",
    "bush"       => "res://assets/models/nature-kit/gltf/bush.glb",
    _            => null,
};
```

Note: Update the filenames in `WorldObjectModelPath` if actual Kenney filenames differ from the expected names verified in Task 5 Step 4.

- [ ] **Step 6: Build to verify**

```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 7: Commit**

```bash
git add client/scripts/world/WorldManager.cs
git commit -m "feat: spawn WorldObject visuals from Kenney models"
```

---

### Task 8: Swap structure and world item visuals to Kenney models

**Files:**
- Modify: `client/scripts/world/WorldManager.cs`

- [ ] **Step 1: Replace CreateStructureVisual** — replace the entire `CreateStructureVisual` method (lines 263–321). The new version loads Kenney .glb files instead of building primitive meshes. Keep the metadata tagging and positioning logic:

```csharp
private Node3D CreateStructureVisual(PlacedStructure structure)
{
    var path = StructureModelPath(structure.StructureType);
    Node3D node;

    if (path != null)
    {
        var scene = ResourceLoader.Load<PackedScene>(path);
        if (scene != null)
        {
            node = scene.Instantiate<Node3D>();
        }
        else
        {
            GD.PrintErr($"[WorldManager] Failed to load structure model: {path}");
            node = CreateFallbackStructureNode(structure);
        }
    }
    else
    {
        node = CreateFallbackStructureNode(structure);
    }

    node.Name = $"Structure_{structure.Id}";
    node.Position = new Vector3(structure.PosX, structure.PosY, structure.PosZ);
    node.Rotation = new Vector3(0, structure.RotY, 0);

    node.SetMeta("structure_id", (long)structure.Id);
    node.SetMeta("structure_type", structure.StructureType);
    node.SetMeta("owner_id", structure.OwnerId.ToString());

    return node;
}

private static string? StructureModelPath(string type) => type switch
{
    "campfire"    => "res://assets/models/survival-kit/gltf/campfire.glb",
    "chest"       => "res://assets/models/survival-kit/gltf/chest.glb",
    "workbench"   => "res://assets/models/survival-kit/gltf/workbench.glb",
    "wood_wall"   => "res://assets/models/building-kit/gltf/wall_wood.glb",
    "wood_floor"  => "res://assets/models/building-kit/gltf/floor_wood.glb",
    "stone_wall"  => "res://assets/models/building-kit/gltf/wall_stone.glb",
    "stone_floor" => "res://assets/models/building-kit/gltf/floor_stone.glb",
    "wood_door"   => "res://assets/models/building-kit/gltf/door_wood.glb",
    _             => null,
};

/// <summary>Fallback to primitive mesh if a Kenney model is missing.</summary>
private static Node3D CreateFallbackStructureNode(PlacedStructure structure)
{
    var node = new Node3D();
    var mesh = new MeshInstance3D();
    mesh.Mesh = structure.StructureType switch
    {
        "wood_wall" or "stone_wall"   => new BoxMesh { Size = new Vector3(2f, 2.5f, 0.2f) },
        "wood_floor" or "stone_floor" => new BoxMesh { Size = new Vector3(2f, 0.1f, 2f) },
        "wood_door"                   => new BoxMesh { Size = new Vector3(1f, 2.2f, 0.15f) },
        "campfire"                    => new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.5f, Height = 0.3f },
        "workbench"                   => new BoxMesh { Size = new Vector3(1.2f, 0.8f, 0.8f) },
        "chest"                       => new BoxMesh { Size = new Vector3(0.8f, 0.6f, 0.5f) },
        _                             => new BoxMesh { Size = new Vector3(1f, 1f, 1f) },
    };
    node.AddChild(mesh);
    return node;
}
```

- [ ] **Step 2: Replace CreateWorldItemVisual** — replace the method (lines 183–228). Keep label and metadata; replace box mesh with Kenney model where available:

```csharp
private Node3D CreateWorldItemVisual(WorldItem item)
{
    var node = new Node3D { Name = $"WorldItem_{item.Id}" };

    // Try Kenney model first, fall back to coloured box
    var modelPath = WorldItemModelPath(item.ItemType);
    if (modelPath != null)
    {
        var scene = ResourceLoader.Load<PackedScene>(modelPath);
        if (scene != null)
        {
            var model = scene.Instantiate<Node3D>();
            model.Position = new Vector3(0, 0.1f, 0);
            node.AddChild(model);
        }
        else
        {
            node.AddChild(CreateFallbackItemMesh(item.ItemType));
        }
    }
    else
    {
        node.AddChild(CreateFallbackItemMesh(item.ItemType));
    }

    // Floating label
    var label = new Label3D
    {
        Text = $"{item.ItemType} x{item.Quantity}",
        FontSize = 32,
        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
        NoDepthTest = true,
        Position = new Vector3(0, 0.7f, 0),
    };
    node.AddChild(label);

    node.Position = new Vector3(item.PosX, item.PosY, item.PosZ);
    node.SetMeta("world_item_id", (long)item.Id);
    node.SetMeta("item_type", item.ItemType);

    return node;
}

private static string? WorldItemModelPath(string type) => type switch
{
    "wood"  => "res://assets/models/nature-kit/gltf/log.glb",
    "stone" => "res://assets/models/nature-kit/gltf/rock_small.glb",
    _       => null,
};

private static MeshInstance3D CreateFallbackItemMesh(string itemType)
{
    var mesh = new MeshInstance3D();
    mesh.Mesh = new BoxMesh { Size = new Vector3(0.4f, 0.4f, 0.4f) };
    mesh.Position = new Vector3(0, 0.2f, 0);
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
```

- [ ] **Step 3: Build to verify**

```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 4: Commit**

```bash
git add client/scripts/world/WorldManager.cs
git commit -m "feat: swap structure and world item visuals to Kenney models with fallbacks"
```

---

### Task 9: Add world object interaction to InteractionSystem.cs

**Files:**
- Modify: `client/scripts/world/InteractionSystem.cs`

- [ ] **Step 1: Add world object raycast branch** — in `_Process`, the file currently computes `from`/`to` ray vectors (lines 47–49) but ignores them and calls `CheckNearbyWorldItems()`. Add a new physics raycast call for world objects immediately after line 52 (`CheckNearbyWorldItems();`):

Replace the body of `_Process` (lines 37–53) with:

```csharp
public override void _Process(double delta)
{
    _camera ??= GetViewport()?.GetCamera3D();
    if (_camera == null) return;

    // Proximity check for world items (unchanged)
    CheckNearbyWorldItems();

    // Raycast for harvestable world objects
    var spaceState = _camera.GetWorld3D()?.DirectSpaceState;
    if (spaceState == null) return;

    var screenCenter = GetViewport().GetVisibleRect().Size / 2;
    var from = _camera.ProjectRayOrigin(screenCenter);
    var to   = from + _camera.ProjectRayNormal(screenCenter) * InteractionRange;

    var query = PhysicsRayQueryParameters3D.Create(from, to);
    var result = spaceState.IntersectRay(query);

    if (result.Count > 0 && result["collider"].AsGodotObject() is Node hit
        && hit.IsInGroup("world_object"))
    {
        var id = (ulong)(long)hit.GetMeta("world_object_id");
        var objType = (string)hit.GetMeta("object_type");

        if (_interactionHint != null)
        {
            _interactionHint.Text = $"[E] Harvest {objType.Replace("_", " ")}";
            _interactionHint.Visible = true;
        }

        if (Input.IsActionJustPressed("interact"))
            GameManager.Instance.DamageWorldObject(id, 25);
    }
}
```

Note: The `CheckNearbyWorldItems` hint display may now compete with the world object hint. If both are in range, the world item hint will overwrite the world object hint in the same frame. This is acceptable for Phase 1.

- [ ] **Step 2: Build to verify**

```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 3: Commit**

```bash
git add client/scripts/world/InteractionSystem.cs
git commit -m "feat: add raycast-based world object interaction (harvest with E)"
```

---

### Task 10: In-game verification — Chunk 2

- [ ] **Step 1: Start the dev environment** — SpacetimeDB running, module published (from Task 4), Godot open.

- [ ] **Step 2: Run the game** and verify:
  - Kenney tree/rock/bush models appear in the world at the correct positions
  - Pressing E while looking at a tree/rock reduces its health (server-side); at 0 health the model disappears and a wood/stone world item appears
  - Structure visuals use Kenney models (place a campfire from inventory to test)
  - Wood/stone world items use Kenney log/rock models

- [ ] **Step 3: If models don't appear**, check Godot Output panel for `[WorldManager]` error messages. Verify exact filenames match what's on disk (Task 5 Step 4).

---

## Chunk 3: Terrain + Water + Building Slope Snap

### Task 11: Create Terrain.cs

**Files:**
- Create: `client/scripts/world/Terrain.cs`

- [ ] **Step 1: Create the file**

```csharp
using Godot;
using System.Collections.Generic;

namespace SandboxRPG;

/// <summary>
/// Generates the coastal terrain mesh and collision at runtime.
/// Beach (Z ≤ 5) is flat at Y=0; smooth rise to Y=4 by Z=25; plateau beyond.
/// Must be a child of (or itself be) a StaticBody3D with a MeshInstance3D and
/// CollisionShape3D child — see Task 13 for scene setup.
/// </summary>
public partial class Terrain : StaticBody3D
{
    [Export] public int Subdivisions = 50;   // quads per axis
    [Export] public float WorldSize  = 100f;
    [Export] public float MaxHeight  = 4f;
    [Export] public float BeachEnd   = 5f;
    [Export] public float SlopeWidth = 20f;

    /// <summary>Height at world position (X, Z). Used by other systems for Y placement.</summary>
    public static float HeightAt(float x, float z)
    {
        float t = Mathf.Clamp((z - 5f) / 20f, 0f, 1f);
        return Mathf.SmoothStep(0f, 4f, t);
    }

    public override void _Ready()
    {
        GenerateMesh();
        GenerateCollision();
    }

    private void GenerateMesh()
    {
        var meshInstance = GetNode<MeshInstance3D>("MeshInstance3D");
        meshInstance.Mesh = BuildArrayMesh();
    }

    private void GenerateCollision()
    {
        var shape = GetNode<CollisionShape3D>("CollisionShape3D");

        int mapSize = Subdivisions + 1;
        float[] heights = new float[mapSize * mapSize];
        float step = WorldSize / Subdivisions;

        for (int z = 0; z < mapSize; z++)
        for (int x = 0; x < mapSize; x++)
        {
            float worldX = x * step - WorldSize / 2f;
            float worldZ = z * step - WorldSize / 2f;
            heights[z * mapSize + x] = HeightAt(worldX, worldZ);
        }

        var heightmap = new HeightMapShape3D();
        heightmap.MapWidth  = mapSize;
        heightmap.MapDepth  = mapSize;
        heightmap.MapData   = heights;
        shape.Shape = heightmap;

        // HeightMapShape3D is centred at origin; scale to match world size
        shape.Scale = new Vector3(WorldSize / Subdivisions, 1f, WorldSize / Subdivisions);
    }

    private ArrayMesh BuildArrayMesh()
    {
        int verts = (Subdivisions + 1) * (Subdivisions + 1);
        var positions = new List<Vector3>(verts);
        var normals   = new List<Vector3>(verts);
        var uvs       = new List<Vector2>(verts);
        var indices   = new List<int>(Subdivisions * Subdivisions * 6);

        float step = WorldSize / Subdivisions;
        float uvStep = 1f / Subdivisions;

        for (int z = 0; z <= Subdivisions; z++)
        for (int x = 0; x <= Subdivisions; x++)
        {
            float wx = x * step - WorldSize / 2f;
            float wz = z * step - WorldSize / 2f;
            float wy = HeightAt(wx, wz);

            positions.Add(new Vector3(wx, wy, wz));
            uvs.Add(new Vector2(x * uvStep, z * uvStep));

            // Approximate normal via finite difference
            float hR = HeightAt(wx + 0.1f, wz);
            float hF = HeightAt(wx, wz + 0.1f);
            normals.Add(new Vector3(wy - hR, 0.1f, wy - hF).Normalized());
        }

        int w = Subdivisions + 1;
        for (int z = 0; z < Subdivisions; z++)
        for (int x = 0; x < Subdivisions; x++)
        {
            int i = z * w + x;
            indices.Add(i);         indices.Add(i + w + 1); indices.Add(i + 1);
            indices.Add(i);         indices.Add(i + w);     indices.Add(i + w + 1);
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV]  = uvs.ToArray();
        arrays[(int)Mesh.ArrayType.Index]  = indices.ToArray();

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
```

- [ ] **Step 2: Build to verify**

```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 3: Commit**

```bash
git add client/scripts/world/Terrain.cs
git commit -m "feat: add Terrain.cs procedural heightmap mesh generator"
```

---

### Task 12: Create terrain blend shader and textures

**Files:**
- Create: `client/assets/shaders/terrain_blend.gdshader`
- Create: `client/assets/textures/sand.png` (download from ambientCG)
- Create: `client/assets/textures/grass.png` (download from ambientCG)

- [ ] **Step 1: Download CC0 textures** — go to `https://ambientcg.com` and download:
  - Search "Sand" → download a 1024px PNG as `sand.png` → save to `client/assets/textures/sand.png`
  - Search "Grass" → download a 1024px PNG as `grass.png` → save to `client/assets/textures/grass.png`

  (Any CC0 sand/grass texture PNG at 512–1024px works fine.)

- [ ] **Step 2: Create the terrain shader**

```glsl
// client/assets/shaders/terrain_blend.gdshader
shader_type spatial;
render_mode diffuse_lambert, specular_disabled;

uniform sampler2D sand_texture : source_color, hint_default_white;
uniform sampler2D grass_texture : source_color, hint_default_white;
uniform float blend_height : hint_range(0.0, 4.0) = 0.5;
uniform float blend_sharpness : hint_range(0.1, 10.0) = 3.0;
uniform vec2 uv_scale = vec2(8.0, 8.0);

// Interpolated world-space Y passed from vertex shader
varying float world_y;

void vertex() {
    // MODEL_MATRIX transforms local VERTEX to world space
    world_y = (MODEL_MATRIX * vec4(VERTEX, 1.0)).y;
}

void fragment() {
    vec2 uv = UV * uv_scale;
    vec4 sand  = texture(sand_texture,  uv);
    vec4 grass = texture(grass_texture, uv);

    // Blend based on world-space height — sand at beach (Y≈0), grass on plateau (Y≈4)
    float t = clamp((world_y - blend_height) * blend_sharpness, 0.0, 1.0);
    ALBEDO = mix(sand.rgb, grass.rgb, t);
    ROUGHNESS = 0.9;
    METALLIC  = 0.0;
}
```

- [ ] **Step 3: Commit**

```bash
git add client/assets/shaders/ client/assets/textures/
git commit -m "assets: add terrain blend shader and sand/grass textures"
```

---

### Task 13: Update Main.tscn — replace Ground with Terrain + add Ocean

**Files:**
- Modify: `client/scenes/Main.tscn`

This task is done in the Godot editor, not in code.

- [ ] **Step 1: Open the project in Godot**

- [ ] **Step 2: Delete the old `Ground` node** — in the Scene panel, find `Ground` (StaticBody3D), right-click → Delete.

- [ ] **Step 3: Add `Terrain` node**:
  1. Add a new `Node3D` as a child of `Main`, rename it `Terrain`
  2. Attach `Terrain.cs` as its script — this works because `Terrain` extends `StaticBody3D`, and the editor will automatically upgrade the node type to `StaticBody3D` when the script is attached
  3. Add a `MeshInstance3D` child of `Terrain`, name it exactly `MeshInstance3D`
  4. Add a `CollisionShape3D` child of `Terrain`, name it exactly `CollisionShape3D`
  5. Run the scene once — `Terrain._Ready()` generates the mesh and collision automatically using `GetNode<MeshInstance3D>("MeshInstance3D")` and `GetNode<CollisionShape3D>("CollisionShape3D")`

- [ ] **Step 4: Apply terrain shader to the mesh**:
  1. Select `MeshInstance3D` inside `Terrain`
  2. In Inspector → Surface Material Override → create a new `ShaderMaterial`
  3. Set Shader to `res://assets/shaders/terrain_blend.gdshader`
  4. Assign `sand_texture` → `res://assets/textures/sand.png`
  5. Assign `grass_texture` → `res://assets/textures/grass.png`

- [ ] **Step 5: Add `Ocean` node**:
  1. Add a `MeshInstance3D` as a child of `Main`, rename it `Ocean`
  2. Set Mesh to a new `PlaneMesh`, Size `(80, 100)`
  3. Position: `(0, -0.2, 20)` (centred on the ocean side, slightly below beach)
  4. Create a `StandardMaterial3D` on the mesh:
     - Albedo color: `(0.2, 0.5, 0.7, 0.75)`
     - Transparency: `Alpha`
     - Roughness: `0.1`
     - Metallic: `0.0`

- [ ] **Step 6: Adjust WorldEnvironment**:
  - Open `WorldEnvironment` → `Environment` → Sky → Sky Material → `Sky Color` → set to light blue `(0.5, 0.75, 0.95)`
  - Fog → `Enabled: true`, Fog Color `(0.7, 0.85, 0.9)`, Density `0.003`

- [ ] **Step 7: Save the scene** (Ctrl+S)

- [ ] **Step 8: Commit**

```bash
git add client/scenes/Main.tscn
git commit -m "feat: replace flat ground with procedural terrain, add ocean plane"
```

---

### Task 14: Fix BuildSystem.cs slope snap

**Files:**
- Modify: `client/scripts/building/BuildSystem.cs`

- [ ] **Step 1: Add two new fields** — after line 23 (`private string? _currentGhostType`), add both fields before adding any code that uses them:

```csharp
private float _ghostRotationY = 0f;  // accumulated player rotation in degrees (90° steps)
private bool  _rWasPressed    = false;
```

- [ ] **Step 2: Replace R-key rotation handler** — in `_Process`, replace lines 55–56:

```csharp
// Old (remove this):
if (Input.IsKeyPressed(Key.R) && _ghostPreview != null)
    _ghostPreview.RotateY(Mathf.Pi / 2 * (float)delta * 3);
```

With a discrete 90° step on key-press:

```csharp
// New: discrete 90° step on R press
if (Input.IsKeyPressed(Key.R) && !_rWasPressed)
    _ghostRotationY = (_ghostRotationY + 90f) % 360f;
_rWasPressed = Input.IsKeyPressed(Key.R);
```

- [ ] **Step 3: Reset rotation when ghost type changes** — in `_Process`, after the `ClearGhost()` call in the "Rebuild ghost if the active item changed" branch (lines 47–50), add:

```csharp
_ghostRotationY = 0f;
```

So the block becomes:
```csharp
if (activeItem != _currentGhostType)
{
    ClearGhost();
    _ghostRotationY = 0f;
    _currentGhostType = activeItem;
}
```

- [ ] **Step 4: Replace UpdateGhostPosition** — replace the entire method (lines 67–89):

```csharp
private void UpdateGhostPosition()
{
    if (_camera == null) return;

    var spaceState = _camera.GetWorld3D()?.DirectSpaceState;
    if (spaceState == null) return;

    var screenCenter = GetViewport().GetVisibleRect().Size / 2;
    var from = _camera.ProjectRayOrigin(screenCenter);
    var dir  = _camera.ProjectRayNormal(screenCenter);

    var query = PhysicsRayQueryParameters3D.Create(from, from + dir * PlaceRange);
    var result = spaceState.IntersectRay(query);
    if (result.Count == 0) return;

    var hitPos = (Vector3)result["position"];
    var normal = (Vector3)result["normal"];

    // Grid snap X/Z; Y from terrain surface hit
    hitPos.X = Mathf.Round(hitPos.X / GridSize) * GridSize;
    hitPos.Z = Mathf.Round(hitPos.Z / GridSize) * GridSize;

    if (_ghostPreview == null)
        CreateGhostPreview(_currentGhostType!);

    if (_ghostPreview == null) return;

    // Align ghost up-axis to terrain normal, then apply player Y rotation on top.
    // Use Vector3.Right as fallback when normal is parallel to Vector3.Forward
    // (e.g., a perfectly vertical surface) to avoid a zero cross-product.
    var up    = normal.Normalized();
    var right = up.Cross(Vector3.Forward);
    if (right.LengthSquared() < 0.001f)
        right = up.Cross(Vector3.Right);
    right = right.Normalized();
    var forward      = right.Cross(up).Normalized();
    var surfaceBasis = new Basis(right, up, -forward);
    var yRotBasis    = Basis.FromEuler(new Vector3(0, Mathf.DegToRad(_ghostRotationY), 0));

    _ghostPreview.GlobalTransform = new Transform3D(surfaceBasis * yRotBasis, hitPos);
}
```

- [ ] **Step 5: Update PlaceStructure to use `_ghostRotationY`** — replace line 145 `var rotY = _ghostPreview.Rotation.Y;` with:

```csharp
var rotY = Mathf.DegToRad(_ghostRotationY);
```

This sends the clean accumulated rotation to the server rather than the Euler-decomposed value from the composed basis.

- [ ] **Step 6: Build to verify**

```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 7: Commit**

```bash
git add client/scripts/building/BuildSystem.cs
git commit -m "fix: slope-aware ghost positioning with surface normal alignment"
```

---

### Task 15: Full in-game verification — Chunk 3

- [ ] **Step 1: Run the game** and verify terrain:
  - Terrain mesh visible: beach near Z=0 (Y=0), smooth slope rising inland, plateau at Y≈4
  - Sand texture on beach, grass texture on plateau, blend visible at transition
  - Ocean plane visible behind the beach, semi-transparent blue
  - Player spawns on beach (not underground, not floating high)

- [ ] **Step 2: Verify world objects** on terrain:
  - Trees, rocks, and bushes sit on terrain surface (not floating or buried)

- [ ] **Step 3: Verify building on slope**:
  - Select a wall from hotbar, aim at sloped terrain
  - Ghost preview aligns to terrain surface (tilts to match slope)
  - Press R — ghost rotates 90° steps
  - Left-click places the structure; it appears aligned to the slope in the world

- [ ] **Step 4: If ghost alignment is off**, tweak the basis construction in `UpdateGhostPosition`. The `up.Cross(Vector3.Forward)` call can produce a zero vector if `up` is parallel to `Forward` — add a fallback: if `right.Length() < 0.01f`, use `up.Cross(Vector3.Right)` instead.

---

## Summary

| Chunk | Tasks | Key deliverable |
|---|---|---|
| 1 | 1–4 | WorldObject table seeded, bindings regenerated |
| 2 | 5–10 | Kenney models in-game, tree/rock harvesting working |
| 3 | 11–15 | Coastal terrain, ocean, building works on slopes |
