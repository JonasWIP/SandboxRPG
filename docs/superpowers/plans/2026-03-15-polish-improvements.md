# Polish Improvements Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Smoother coastline, larger world objects, left-click harvesting, and stable dropped item physics.

**Architecture:** Four independent client-only changes (Tasks 2–4) plus one server+client pair (Task 1). Each task is self-contained with no cross-task dependencies. Server must be republished after Task 1 since the TerrainConfig seed values change.

**Tech Stack:** Godot 4.6.1 C#, SpacetimeDB 2.0, .NET 8

---

## File Structure

| File | What changes |
|------|-------------|
| `server/Lifecycle.cs` | `TerrainNoiseAmp` const 1.5→1.2; `TerrainHeightAt` formula |
| `client/scripts/world/Terrain.cs` | `_noiseAmp` default 1.5→1.2; `HeightAt` formula |
| `client/scripts/world/WorldManager.cs` | Model scale + collision sizes in `CreateWorldObjectVisual`; axis locks in `CreateWorldItemVisual` |
| `client/project.godot` | Add `primary_attack` input action |
| `client/scripts/building/BuildSystem.cs` | Expose `IsBuildable` helper; change placement trigger |
| `client/scripts/world/InteractionSystem.cs` | Change harvest trigger; add build guard; update hint text |

---

## Task 1: Gentler Coast Formula

**Files:**
- Modify: `server/Lifecycle.cs:8-12` (constants) and `server/Lifecycle.cs:118-133` (TerrainHeightAt)
- Modify: `client/scripts/world/Terrain.cs:20` (_noiseAmp default) and `client/scripts/world/Terrain.cs:26-38` (HeightAt)

No automated tests exist — verify by building both projects.

- [ ] **Step 1: Update server TerrainNoiseAmp constant, SeedTerrainConfig, and TerrainHeightAt formula**

In `server/Lifecycle.cs`, change:
```csharp
// Before (line 11):
private const float TerrainNoiseAmp   = 1.5f;
```
to:
```csharp
private const float TerrainNoiseAmp   = 1.2f;
```

The constant change automatically flows into `SeedTerrainConfig` since it already uses `TerrainNoiseAmp` — verify the `SeedTerrainConfig` call reads:
```csharp
NoiseAmplitude = TerrainNoiseAmp,   // now 1.2f
```
(no code change needed in SeedTerrainConfig itself — the constant reference handles it)

Then replace `TerrainHeightAt` (the full method body after the `{`):
```csharp
/// <summary>Mirrors client Terrain.HeightAt — must stay in sync with module constants.</summary>
private static float TerrainHeightAt(float x, float z)
{
    if (z < 0f) return (float)Math.Max(z * 0.15, -3.0);
    double t     = Math.Clamp((z - 5.0) / 30.0, 0.0, 1.0);
    double baseH = t * t * (3.0 - 2.0 * t) * 2.0;
    double nr    = Math.Clamp((z - 8.0) / 20.0, 0.0, 1.0);
    double s     = TerrainSeed * 0.001;
    double noise = Math.Sin(x * TerrainNoiseScale + s) * Math.Cos(z * TerrainNoiseScale * 1.7 + s * 1.3) * TerrainNoiseAmp
                 + Math.Sin((x + z) * TerrainNoiseScale * 2.9 + s * 0.7) * TerrainNoiseAmp * 0.3;
    return (float)(baseH + noise * nr);
}
```

- [ ] **Step 2: Update client HeightAt formula and _noiseAmp default**

In `client/scripts/world/Terrain.cs`, change line 20:
```csharp
// Before:
private static float _noiseAmp   = 1.5f;
// After:
private static float _noiseAmp   = 1.2f;
```

Then replace the `HeightAt` method body:
```csharp
/// <summary>World-space height at (x, z). Identical formula to server TerrainHeightAt.</summary>
public static float HeightAt(float x, float z)
{
    if (z < 0f) return Mathf.Max(z * 0.15f, -3f);
    float t     = Mathf.Clamp((z - 5f) / 30f, 0f, 1f);
    float baseH = 2f * t * t * (3f - 2f * t);
    float nr    = Mathf.Clamp((z - 8f) / 20f, 0f, 1f);
    float s     = _seed * 0.001f;
    float noise = (float)(
        Math.Sin(x * _noiseScale + s) * Math.Cos(z * _noiseScale * 1.7 + s * 1.3) * _noiseAmp
      + Math.Sin((x + z) * _noiseScale * 2.9 + s * 0.7) * _noiseAmp * 0.3
    );
    return baseH + noise * nr;
}
```

- [ ] **Step 3: Build both projects**

```bash
cd server && spacetime build
```
Expected: `Build finished successfully.`

```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: `0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add server/Lifecycle.cs client/scripts/world/Terrain.cs
git commit -m "feat: gentler coast — wider beach zone, slower rise, quieter hills"
```

---

## Task 2: Larger World Object Visuals

**Files:**
- Modify: `client/scripts/world/WorldManager.cs:389-443` (`CreateWorldObjectVisual`)

- [ ] **Step 1: Add model scale and update collision sizes**

In `CreateWorldObjectVisual` in `client/scripts/world/WorldManager.cs`, replace the model instantiation block and the `shapeSize` switch:

```csharp
// Replace the model instantiation block (currently ~lines 404-416):
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
    var model = ResourceLoader.Load<PackedScene>(modelPath).Instantiate<Node3D>();
    model.Scale = Vector3.One * modelScale;
    body.AddChild(model);
}
else
{
    body.AddChild(new MeshInstance3D
    {
        Mesh = new BoxMesh { Size = new Vector3(0.8f, 1.5f, 0.8f) * modelScale },
        Position = new Vector3(0, 0.75f * modelScale, 0),
    });
}
```

Then replace the `shapeSize` switch (currently ~lines 419-426). **Note:** pine and palm scale 2.5× while dead trees scale 2.0×, so they need separate collision entries:
```csharp
var shapeSize = obj.ObjectType switch
{
    "tree_pine" or "tree_palm" => new Vector3(1.2f, 6.0f, 1.2f),
    "tree_dead"                => new Vector3(1.0f, 5.0f, 1.0f),
    "rock_large"               => new Vector3(2.4f, 1.6f, 2.4f),
    "rock_small"               => new Vector3(1.1f, 0.7f, 1.1f),
    "bush"                     => new Vector3(1.5f, 1.0f, 1.5f),
    _                          => new Vector3(0.8f, 1.0f, 0.8f),
};
```

- [ ] **Step 2: Build client**

```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: `0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add client/scripts/world/WorldManager.cs
git commit -m "feat: scale up world objects — trees 2-2.5x, rocks 1.8-2x, bushes 1.5x"
```

---

## Task 3: Left-Click Harvesting

**Files:**
- Modify: `client/project.godot` — add `primary_attack` action
- Modify: `client/scripts/building/BuildSystem.cs` — expose `IsBuildable`, switch trigger
- Modify: `client/scripts/world/InteractionSystem.cs` — switch harvest trigger, add guard, update hint

### Step 1: Add input action to project.godot

- [ ] **Step 1: Add primary_attack action**

In `client/project.godot`, find the line `interact={` and add the following block **before** it:

```
primary_attack={
"deadzone": 0.5,
"events": [Object(InputEventMouseButton,"resource_local_to_scene":false,"resource_name":"","device":-1,"window_id":0,"alt_pressed":false,"shift_pressed":false,"ctrl_pressed":false,"meta_pressed":false,"button_mask":0,"position":Vector2(0, 0),"global_position":Vector2(0, 0),"factor":1.0,"button_index":1,"canceled":false,"pressed":false,"double_click":false,"script":null)
]
}
```

### Step 2: Update BuildSystem

- [ ] **Step 2: Expose IsBuildable and switch placement trigger**

In `client/scripts/building/BuildSystem.cs`:

`BuildableTypes` is already `private static readonly` — **no change to the declaration**. Just add the public helper directly after the closing `};` of the `BuildableTypes` field initializer:
```csharp
public static bool IsBuildable(string? itemType) =>
    itemType != null && BuildableTypes.Contains(itemType);
```

Change the placement trigger (currently line 64):
```csharp
// Before:
if (Input.IsMouseButtonPressed(MouseButton.Left) && _ghostPreview != null)
    PlaceStructure();
// After:
if (Input.IsActionJustPressed("primary_attack") && _ghostPreview != null)
    PlaceStructure();
```

### Step 3: Update InteractionSystem

- [ ] **Step 3: Switch harvest to primary_attack + guard + updated hint**

In `client/scripts/world/InteractionSystem.cs`, update `CheckWorldObjectRaycast`:

```csharp
// Change hint text (line 116):
_interactionHint.Text = $"[LMB] Harvest {objectType}";

// Change trigger (line 120):
if (Input.IsActionJustPressed("primary_attack") && !BuildSystem.IsBuildable(Hotbar.Instance?.ActiveItemType))
```

- [ ] **Step 4: Build client**

```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: `0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add client/project.godot client/scripts/building/BuildSystem.cs client/scripts/world/InteractionSystem.cs
git commit -m "feat: harvest world objects with left-click instead of E key"
```

---

## Task 4: Stable Dropped Item Physics

**Files:**
- Modify: `client/scripts/world/WorldManager.cs:163-167` (`CreateWorldItemVisual` — the RigidBody3D initializer)

- [ ] **Step 1: Add angular axis locks to the RigidBody3D**

In `client/scripts/world/WorldManager.cs`, replace the `RigidBody3D` initializer in `CreateWorldItemVisual`:

```csharp
// Before:
var body = new RigidBody3D
{
    Name       = $"WorldItem_{item.Id}",
    LinearDamp = 2.0f,
};

// After:
var body = new RigidBody3D
{
    Name             = $"WorldItem_{item.Id}",
    LinearDamp       = 2.0f,
    AngularDamp      = 10f,
    AxisLockAngularX = true,
    AxisLockAngularY = true,
    AxisLockAngularZ = true,
};
```

- [ ] **Step 2: Build client**

```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: `0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add client/scripts/world/WorldManager.cs
git commit -m "fix: lock item rotation axes so drops don't roll or spin the label"
```

---

## Task 5: Publish server + verify in game

- [ ] **Step 1: Publish server (required because TerrainConfig seed values changed)**

Use the `start_server` MCP tool — this wipes data, re-publishes, and logs in fresh.

- [ ] **Step 2: Launch game**

Use the `start_godot` MCP tool (editor=false).

- [ ] **Step 3: Verify**

- [ ] Coast: spawn area is flat; terrain rises very gradually starting around z=5; no abrupt wall
- [ ] Objects: trees visibly taller, rocks larger; look proportional to the world
- [ ] Harvesting: clicking E does NOT harvest; left-click on a tree while holding an axe harvests; E still picks up items
- [ ] Building: left-click still places structures when a buildable item is in hotbar
- [ ] Dropped items: harvest a tree; item falls and settles without rolling; label stays upright
