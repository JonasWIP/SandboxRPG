# Gameplay Improvements Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add server-authoritative terrain config, tool-based harvesting, item drop physics, and fix wood-wall visual and building rotation bugs.

**Architecture:** Server owns a singleton `TerrainConfig` row (seed, world size, noise params). Clients subscribe and regenerate terrain on change. `DamageWorldObject` is replaced by `HarvestWorldObject` which validates the tool type. World items spawn as `RigidBody3D` and fall under gravity. Structure model tints distinguish wood from stone visually.

**Tech Stack:** SpacetimeDB 2.0 (C# WASM), Godot 4.6.1 C# client, Kenney GLB assets, Godot physics engine.

---

## Chunk 1: Server Changes

### Task 1: Add TerrainConfig table

**Files:**
- Modify: `server/Tables.cs`

- [ ] **Step 1: Add TerrainConfig struct** at the end of `Tables.cs`, before the closing `}`:

```csharp
/// <summary>Singleton terrain configuration. Always has exactly one row (Id = 0).
/// Clients subscribe and regenerate terrain mesh + collision whenever this changes.</summary>
[Table(Name = "terrain_config", Public = true)]
public partial struct TerrainConfig
{
    [PrimaryKey]
    public uint Id;            // always 0
    public uint Seed;
    public float WorldSize;    // units (side length of the square world)
    public float NoiseScale;   // spatial frequency of hills
    public float NoiseAmplitude; // max height added by noise
}
```

- [ ] **Step 2: Build server**

```bash
cd server && spacetime build
```
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 3: Commit**

```bash
git add server/Tables.cs
git commit -m "feat: add TerrainConfig table for server-authoritative terrain"
```

---

### Task 2: Update Lifecycle.cs — seed TerrainConfig + expand world

**Files:**
- Modify: `server/Lifecycle.cs`

Context: current file starts with `using SpacetimeDB; using System;`. WorldSize was 100, now 500.

- [ ] **Step 1: Add `SeedTerrainConfig` method** after `SeedWorldItems` (before the `TerrainHeightAt` helper):

```csharp
private static void SeedTerrainConfig(ReducerContext ctx)
{
    ctx.Db.TerrainConfig.Insert(new TerrainConfig
    {
        Id             = 0,
        Seed           = 42,
        WorldSize      = 500f,
        NoiseScale     = 0.04f,
        NoiseAmplitude = 1.5f,
    });
    Log.Info("Seeded terrain config.");
}
```

- [ ] **Step 2: Call it from Init** — add `SeedTerrainConfig(ctx);` as the first call in the `Init` reducer body:

```csharp
[Reducer(ReducerKind.Init)]
public static void Init(ReducerContext ctx)
{
    Log.Info("SandboxRPG server module initialized!");
    SeedTerrainConfig(ctx);
    SeedRecipes(ctx);
    SeedWorldItems(ctx);
    SeedWorldObjects(ctx);
}
```

- [ ] **Step 3: Replace `TerrainHeightAt`** with the version that includes noise (must match client `Terrain.HeightAt` exactly):

Replace the existing `TerrainHeightAt` method with:

```csharp
/// <summary>Mirrors client Terrain.HeightAt. Seed/noise constants must match TerrainConfig defaults.</summary>
private static float TerrainHeightAt(float x, float z)
{
    const uint  Seed  = 42;
    const float NScl  = 0.04f;
    const float NAmp  = 1.5f;

    if (z < 0f) return (float)Math.Max(z * 0.3, -3.0);
    double t     = Math.Clamp((z - 2.0) / 15.0, 0.0, 1.0);
    double baseH = t * t * (3.0 - 2.0 * t) * 2.0;
    double nr    = Math.Clamp((z - 5.0) / 15.0, 0.0, 1.0);
    double s     = Seed * 0.001;
    double noise = Math.Sin(x * NScl + s) * Math.Cos(z * NScl * 1.7 + s * 1.3) * NAmp
                 + Math.Sin((x + z) * NScl * 2.9 + s * 0.7) * NAmp * 0.3;
    return (float)(baseH + noise * nr);
}
```

- [ ] **Step 4: Expand world object seeding ranges** to match the 500-unit world. Replace the body of `SeedWorldObjects`:

```csharp
private static void SeedWorldObjects(ReducerContext ctx)
{
    var rng = new Random(42);

    WorldObject MakeObject(string type, float xMin, float xMax, float zMin, float zMax, uint hp)
    {
        float x = (float)(rng.NextDouble() * (xMax - xMin) + xMin);
        float z = (float)(rng.NextDouble() * (zMax - zMin) + zMin);
        return new WorldObject
        {
            ObjectType = type,
            PosX = x, PosY = TerrainHeightAt(x, z), PosZ = z,
            RotY = (float)(rng.NextDouble() * Math.PI * 2),
            Health = hp, MaxHealth = hp,
        };
    }

    for (int i = 0; i < 250; i++)  ctx.Db.WorldObject.Insert(MakeObject("tree_pine",  -200f, 200f,  30f, 230f, 100));
    for (int i = 0; i < 60;  i++)  ctx.Db.WorldObject.Insert(MakeObject("tree_dead",  -150f, 150f,  20f,  60f, 60));
    for (int i = 0; i < 90;  i++)  ctx.Db.WorldObject.Insert(MakeObject("rock_large", -200f, 200f,   0f, 150f, 150));
    for (int i = 0; i < 75;  i++)  ctx.Db.WorldObject.Insert(MakeObject("rock_small", -220f, 220f, -20f, 170f, 80));
    for (int i = 0; i < 50;  i++)  ctx.Db.WorldObject.Insert(MakeObject("bush",       -100f, 100f,   5f,  50f, 30));

    Log.Info("Seeded world objects.");
}
```

- [ ] **Step 5: Build server**

```bash
cd server && spacetime build
```
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 6: Commit**

```bash
git add server/Lifecycle.cs
git commit -m "feat: seed TerrainConfig, expand world to 500 units, add terrain noise"
```

---

### Task 3: Replace DamageWorldObject with HarvestWorldObject

**Files:**
- Modify: `server/WorldReducers.cs`

- [ ] **Step 1: Replace the entire file contents** with:

```csharp
using SpacetimeDB;

namespace SandboxRPG.Server;

public static partial class Module
{
    [Reducer]
    public static void HarvestWorldObject(ReducerContext ctx, ulong id, string toolType)
    {
        var obj = ctx.Db.WorldObject.Id.Find(id);
        if (obj is null) return;
        var o = obj.Value;

        // Validate the player owns the stated tool (empty string = bare hands, always valid)
        if (!string.IsNullOrEmpty(toolType))
        {
            bool found = false;
            foreach (var item in ctx.Db.InventoryItem.Iter())
            {
                if (item.OwnerId == ctx.Sender && item.ItemType == toolType)
                { found = true; break; }
            }
            if (!found) return;
        }

        uint damage    = ToolDamage(toolType, o.ObjectType);
        uint newHealth = o.Health <= damage ? 0 : o.Health - damage;

        ctx.Db.WorldObject.Delete(o);

        if (newHealth == 0)
        {
            ctx.Db.WorldItem.Insert(new WorldItem
            {
                ItemType = DropTypeFor(o.ObjectType),
                Quantity = DropQuantityFor(o.ObjectType),
                PosX = o.PosX, PosY = o.PosY, PosZ = o.PosZ,
            });
        }
        else
        {
            ctx.Db.WorldObject.Insert(new WorldObject
            {
                ObjectType = o.ObjectType,
                PosX = o.PosX, PosY = o.PosY, PosZ = o.PosZ,
                RotY = o.RotY, Health = newHealth, MaxHealth = o.MaxHealth,
            });
        }
    }

    private static uint ToolDamage(string toolType, string objectType)
    {
        bool isTree = objectType is "tree_pine" or "tree_dead" or "tree_palm" or "bush";
        bool isRock = objectType is "rock_large" or "rock_small";
        return toolType switch
        {
            "wood_axe"      => isTree ? 34u : 5u,
            "wood_pickaxe"  => isRock ? 34u : 5u,
            "stone_pickaxe" => isRock ? 50u : 8u,
            "iron_pickaxe"  => isRock ? 75u : 10u,
            _               => 5u,
        };
    }

    private static string DropTypeFor(string objectType) => objectType switch
    {
        "rock_large" or "rock_small" => "stone",
        _ => "wood",
    };

    private static uint DropQuantityFor(string objectType) => objectType switch
    {
        "rock_large" => 3u,
        "tree_pine"  => 4u,
        "tree_dead"  => 2u,
        _            => 1u,
    };
}
```

- [ ] **Step 2: Build server**

```bash
cd server && spacetime build
```
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 3: Commit**

```bash
git add server/WorldReducers.cs
git commit -m "feat: HarvestWorldObject with tool-type damage and quantity drops"
```

---

### Task 4: Publish server + regenerate client bindings

**Files:**
- Modify: `client/scripts/networking/SpacetimeDB/` (auto-generated — do not hand-edit)

- [ ] **Step 1: Ensure SpacetimeDB is running and re-login if needed**

```bash
spacetime logout && spacetime login --server-issued-login local --no-browser
```

- [ ] **Step 2: Publish**

```bash
cd server && spacetime publish -b bin/Release/net8.0/wasi-wasm/AppBundle/StdbModule.wasm
```
Expected: `Published successfully.`

- [ ] **Step 3: Regenerate bindings**

```bash
cd server && spacetime generate --lang csharp \
  --out-dir ../client/scripts/networking/SpacetimeDB \
  --bin-path bin/Release/net8.0/wasi-wasm/AppBundle/StdbModule.wasm
```
Verify: `TerrainConfig.cs` appears in the output directory, `Reducers.cs` now has `HarvestWorldObject` (not `DamageWorldObject`).

- [ ] **Step 4: Build client to confirm bindings compile**

```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 5: Commit**

```bash
git add client/scripts/networking/SpacetimeDB/
git commit -m "chore: regenerate bindings for TerrainConfig + HarvestWorldObject"
```

---

## Chunk 2: Client Changes

### Task 5: Update GameManager.cs — TerrainConfig signal + HarvestWorldObject

**Files:**
- Modify: `client/scripts/networking/GameManager.cs`

- [ ] **Step 1: Add signal** — in the `=== Signals ===` block after `WorldObjectUpdatedEventHandler`, add:

```csharp
[Signal] public delegate void TerrainConfigChangedEventHandler();
```

- [ ] **Step 2: Replace `DamageWorldObject` reducer call** (line 115) with:

```csharp
public void HarvestWorldObject(ulong id, string toolType) => Conn?.Reducers.HarvestWorldObject(id, toolType);
```

(Remove the old `DamageWorldObject` line entirely.)

- [ ] **Step 3: Add data accessor** — after `GetWorldObject` (line 145), add:

```csharp
public TerrainConfig? GetTerrainConfig() => Conn?.Db.TerrainConfig.Id.Find(0);
```

- [ ] **Step 4: Register TerrainConfig callbacks** — in `RegisterCallbacks`, after the `WorldObject` callbacks (line 263), add:

```csharp
conn.Db.TerrainConfig.OnInsert += (ctx, _) => CallDeferred(nameof(EmitTerrainConfigChanged));
conn.Db.TerrainConfig.OnUpdate += (ctx, _, _) => CallDeferred(nameof(EmitTerrainConfigChanged));
```

- [ ] **Step 5: Add deferred emitter** — after `EmitWorldObjectUpdated` (line 274), add:

```csharp
private void EmitTerrainConfigChanged() => EmitSignal(SignalName.TerrainConfigChanged);
```

- [ ] **Step 6: Build**

```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 7: Commit**

```bash
git add client/scripts/networking/GameManager.cs
git commit -m "feat: add TerrainConfigChanged signal and HarvestWorldObject call"
```

---

### Task 6: Update Terrain.cs — server config + noise + 500-unit world

**Files:**
- Modify: `client/scripts/world/Terrain.cs`

- [ ] **Step 1: Replace the entire file** with:

```csharp
using Godot;
using System;
using System.Collections.Generic;

namespace SandboxRPG;

/// <summary>
/// Generates the terrain mesh and collision from server-authoritative TerrainConfig.
/// Subscribe to GameManager.TerrainConfigChanged to regenerate when the server updates config.
/// HeightAt() is static so other systems (WorldManager, BuildSystem) can query ground height.
/// </summary>
public partial class Terrain : StaticBody3D
{
    [Export] public int Subdivisions = 100;
    [Export] public Material? TerrainMaterial;

    // Noise params — updated from TerrainConfig, defaults match server seed values
    private static uint  _seed      = 42;
    private static float _noiseScale = 0.04f;
    private static float _noiseAmp   = 1.5f;
    private static float _worldSize  = 500f;

    public static float WorldSize => _worldSize;

    /// <summary>World-space height at (x, z). Identical formula to server TerrainHeightAt.</summary>
    public static float HeightAt(float x, float z)
    {
        if (z < 0f) return Mathf.Max(z * 0.3f, -3f);
        float t     = Mathf.Clamp((z - 2f) / 15f, 0f, 1f);
        float baseH = 2f * t * t * (3f - 2f * t);
        float nr    = Mathf.Clamp((z - 5f) / 15f, 0f, 1f);
        float s     = _seed * 0.001f;
        float noise = (float)(
            Math.Sin(x * _noiseScale + s) * Math.Cos(z * _noiseScale * 1.7 + s * 1.3) * _noiseAmp
          + Math.Sin((x + z) * _noiseScale * 2.9 + s * 0.7) * _noiseAmp * 0.3
        );
        return baseH + noise * nr;
    }

    public override void _Ready()
    {
        GD.Print("[Terrain] _Ready called");
        var gm = GameManager.Instance;
        gm.TerrainConfigChanged += OnTerrainConfigChanged;

        var cfg = gm.GetTerrainConfig();
        if (cfg != null) ApplyConfig(cfg.Value);

        GenerateMesh();
        GenerateCollision();
    }

    private void OnTerrainConfigChanged()
    {
        var cfg = GameManager.Instance.GetTerrainConfig();
        if (cfg == null) return;
        ApplyConfig(cfg.Value);
        GenerateMesh();
        GenerateCollision();
    }

    private static void ApplyConfig(SpacetimeDB.Types.TerrainConfig cfg)
    {
        _seed       = cfg.Seed;
        _noiseScale = cfg.NoiseScale;
        _noiseAmp   = cfg.NoiseAmplitude;
        _worldSize  = cfg.WorldSize;
    }

    private void GenerateMesh()
    {
        var meshInstance = GetNode<MeshInstance3D>("MeshInstance3D");
        meshInstance.Mesh = BuildArrayMesh();
        if (TerrainMaterial != null)
            meshInstance.SetSurfaceOverrideMaterial(0, TerrainMaterial);
        else
            GD.PrintErr("[Terrain] No TerrainMaterial assigned!");
    }

    private void GenerateCollision()
    {
        var shape   = GetNode<CollisionShape3D>("CollisionShape3D");
        int mapSize = Subdivisions + 1;
        float step  = _worldSize / Subdivisions;
        var heights = new float[mapSize * mapSize];

        for (int z = 0; z < mapSize; z++)
        for (int x = 0; x < mapSize; x++)
            heights[z * mapSize + x] = HeightAt(x * step - _worldSize / 2f, z * step - _worldSize / 2f);

        shape.Shape = new HeightMapShape3D { MapWidth = mapSize, MapDepth = mapSize, MapData = heights };
        shape.Scale = new Vector3(step, 1f, step);
    }

    private ArrayMesh BuildArrayMesh()
    {
        int   n      = Subdivisions + 1;
        float step   = _worldSize / Subdivisions;
        float uvStep = 1f / Subdivisions;

        var positions = new List<Vector3>(n * n);
        var normals   = new List<Vector3>(n * n);
        var uvs       = new List<Vector2>(n * n);
        var indices   = new List<int>(Subdivisions * Subdivisions * 6);

        for (int z = 0; z <= Subdivisions; z++)
        for (int x = 0; x <= Subdivisions; x++)
        {
            float wx = x * step - _worldSize / 2f;
            float wz = z * step - _worldSize / 2f;
            float wy = HeightAt(wx, wz);
            positions.Add(new Vector3(wx, wy, wz));
            uvs.Add(new Vector2(x * uvStep, z * uvStep));
            normals.Add(new Vector3(wy - HeightAt(wx + 0.1f, wz), 0.1f, wy - HeightAt(wx, wz + 0.1f)).Normalized());
        }

        int w = Subdivisions + 1;
        for (int z = 0; z < Subdivisions; z++)
        for (int x = 0; x < Subdivisions; x++)
        {
            int i = z * w + x;
            indices.Add(i);         indices.Add(i + 1);     indices.Add(i + w + 1);
            indices.Add(i);         indices.Add(i + w + 1); indices.Add(i + w);
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

- [ ] **Step 2: Build**

```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 3: Commit**

```bash
git add client/scripts/world/Terrain.cs
git commit -m "feat: Terrain reads TerrainConfig from server, adds sin-wave noise, 500-unit world"
```

---

### Task 7: Update WorldManager.cs — item gravity + structure color tints

**Files:**
- Modify: `client/scripts/world/WorldManager.cs`

Two independent changes: (A) world items use `RigidBody3D` for gravity; (B) structure visuals get wood/stone color tints.

**Change A — Item gravity:**

- [ ] **Step 1: Replace `CreateWorldItemVisual`** with a version that returns `RigidBody3D`:

```csharp
private Node3D CreateWorldItemVisual(WorldItem item)
{
    var body = new RigidBody3D
    {
        Name        = $"WorldItem_{item.Id}",
        LinearDamp  = 2.0f,
    };

    // Collision so it lands on terrain
    body.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = 0.15f } });

    // Visual
    var modelPath = WorldItemModelPath(item.ItemType);
    if (modelPath != null && ResourceLoader.Exists(modelPath))
    {
        var model = ResourceLoader.Load<PackedScene>(modelPath).Instantiate<Node3D>();
        model.Position = new Vector3(0, 0.1f, 0);
        body.AddChild(model);
    }
    else
    {
        body.AddChild(CreateFallbackItemMesh(item.ItemType));
    }

    body.AddChild(new Label3D
    {
        Text        = $"{item.ItemType} x{item.Quantity}",
        FontSize    = 32,
        Billboard   = BaseMaterial3D.BillboardModeEnum.Enabled,
        NoDepthTest = true,
        Position    = new Vector3(0, 0.7f, 0),
    });

    body.Position = new Vector3(item.PosX, item.PosY, item.PosZ);
    body.SetMeta("world_item_id", (long)item.Id);
    body.SetMeta("item_type", item.ItemType);
    return body;
}
```

**Change B — Structure tints:**

- [ ] **Step 2: Add `TintMeshes` helper** after `CreateFallbackStructureNode` (or `CreateFallbackItemMesh`):

```csharp
/// <summary>Recursively applies a color tint to all MeshInstance3D children.</summary>
private static void TintMeshes(Node root, Color color)
{
    if (root is MeshInstance3D mi)
        mi.MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.85f };
    foreach (Node child in root.GetChildren())
        TintMeshes(child, color);
}
```

- [ ] **Step 3: Apply tints in `CreateStructureVisual`** — add this block immediately after the model is instantiated (before setting Name/Position/meta), inside the `if (modelPath != null && ResourceLoader.Exists(modelPath))` branch:

```csharp
// Apply wood/stone color tint so the same model looks different per material type
Color? tint = structure.StructureType switch
{
    "wood_wall" or "wood_floor" or "wood_door" => new Color(0.65f, 0.45f, 0.25f),
    "stone_wall" or "stone_floor"              => new Color(0.6f,  0.6f,  0.65f),
    _                                           => (Color?)null,
};
if (tint.HasValue) TintMeshes(node, tint.Value);
```

Note: `node` here is the instantiated `Node3D` from the GLB scene before it's named/positioned.

- [ ] **Step 4: Build**

```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 5: Commit**

```bash
git add client/scripts/world/WorldManager.cs
git commit -m "feat: world items fall under gravity, wood/stone structure tints"
```

---

### Task 8: Update InteractionSystem.cs — pass tool type to HarvestWorldObject

**Files:**
- Modify: `client/scripts/world/InteractionSystem.cs`

- [ ] **Step 1: Replace the harvest line** in `CheckWorldObjectRaycast`. Find:

```csharp
if (Input.IsActionJustPressed("interact"))
{
    var id = (ulong)collider.GetMeta("world_object_id", 0L).AsInt64();
    GameManager.Instance.DamageWorldObject(id, 25);
}
```

Replace with:

```csharp
if (Input.IsActionJustPressed("interact"))
{
    var id       = (ulong)collider.GetMeta("world_object_id", 0L).AsInt64();
    var toolType = Hotbar.Instance?.ActiveItemType ?? string.Empty;
    GameManager.Instance.HarvestWorldObject(id, toolType);
}
```

- [ ] **Step 2: Build**

```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 3: Commit**

```bash
git add client/scripts/world/InteractionSystem.cs
git commit -m "feat: pass active tool type when harvesting world objects"
```

---

### Task 9: Fix BuildSystem.cs — remove surface-normal tilt from ghost

**Files:**
- Modify: `client/scripts/building/BuildSystem.cs`

**Problem:** Ghost applies surface normal alignment (`surfaceBasis * yRotBasis`), but the placed structure is axis-aligned. Ghost and result look different, which is confusing. Fix: ghost stays vertical, matching the placed result exactly.

- [ ] **Step 1: Replace `UpdateGhostPosition`** — find the method starting at `private void UpdateGhostPosition()` and replace the entire method body:

```csharp
private void UpdateGhostPosition()
{
    if (_camera == null) return;
    var spaceState = _camera.GetWorld3D()?.DirectSpaceState;
    if (spaceState == null) return;

    var screenCenter = GetViewport().GetVisibleRect().Size / 2;
    var from = _camera.ProjectRayOrigin(screenCenter);
    var dir  = _camera.ProjectRayNormal(screenCenter);

    var result = spaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + dir * PlaceRange));
    if (result.Count == 0) return;

    var hitPos = (Vector3)result["position"];
    hitPos.X = Mathf.Round(hitPos.X / GridSize) * GridSize;
    hitPos.Z = Mathf.Round(hitPos.Z / GridSize) * GridSize;

    if (_ghostPreview == null) CreateGhostPreview(_currentGhostType!);
    if (_ghostPreview == null) return;

    var yRot = Basis.FromEuler(new Vector3(0, Mathf.DegToRad(_ghostRotationY), 0));
    _ghostPreview.GlobalTransform = new Transform3D(yRot, hitPos);
}
```

This keeps the ghost vertical at all times, matching placed results exactly.

- [ ] **Step 2: Build**

```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: `Build succeeded.` 0 errors.

- [ ] **Step 3: Commit**

```bash
git add client/scripts/building/BuildSystem.cs
git commit -m "fix: ghost preview stays vertical to match placed structure orientation"
```

---

## Chunk 3: Simplify + Verify

### Task 10: Simplify all changed files

**Files:**
- All files modified in Tasks 1–9

- [ ] **Step 1: Run simplify on server files**

Use `superpowers:simplify` skill on:
- `server/Tables.cs`
- `server/Lifecycle.cs`
- `server/WorldReducers.cs`
- `server/BuildingReducers.cs`

- [ ] **Step 2: Run simplify on client files**

Use `superpowers:simplify` skill on:
- `client/scripts/networking/GameManager.cs`
- `client/scripts/world/Terrain.cs`
- `client/scripts/world/WorldManager.cs`
- `client/scripts/world/InteractionSystem.cs`
- `client/scripts/building/BuildSystem.cs`

- [ ] **Step 3: Build after simplify**

```bash
cd client && dotnet build SandboxRPG.csproj
cd server && spacetime build
```
Both must succeed with 0 errors.

- [ ] **Step 4: Commit simplified files**

```bash
git add server/ client/scripts/
git commit -m "refactor: simplify all gameplay improvement files"
```

---

### Task 11: Full dev restart + in-game verification

- [ ] **Step 1: Start fresh dev environment**

```bash
# Start server
spacetime start --in-memory
spacetime logout && spacetime login --server-issued-login local --no-browser
cd server && spacetime publish -b bin/Release/net8.0/wasi-wasm/AppBundle/StdbModule.wasm
# Build client
cd client && dotnet build SandboxRPG.csproj
```

- [ ] **Step 2: Launch game and verify terrain**
  - Terrain is 500 units wide (player can run inland for a long time)
  - Rolling hills visible inland (noise variation)
  - Beach spawn area is flat (noise suppressed near Z=0)

- [ ] **Step 3: Verify harvesting**
  - Equip `wood_axe` (slot 1) → look at a tree → press E → tree health decreases; at 0 it drops 4 wood
  - Equip `wood_pickaxe` (slot 2) → look at a rock → press E → rock drops stone
  - Bare hands / wrong tool → minimal damage (5 per hit)

- [ ] **Step 4: Verify item gravity**
  - After a tree/rock drops an item, the item should fall and land on the terrain surface
  - Item should not float mid-air

- [ ] **Step 5: Verify structure placement**
  - With a `wood_wall` in hotbar: ghost preview is vertical (no slope tilt)
  - Ghost preview is brown (wood tint)
  - Place a wall → it appears with brown tint
  - Place a `stone_wall` → it appears grey
  - Trying to place without the item in inventory → nothing happens (server rejects)

- [ ] **Step 6: Verify building rotation**
  - Ghost preview rotates 90° cleanly on R press
  - Placed structure matches ghost orientation exactly
