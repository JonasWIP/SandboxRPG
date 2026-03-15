# Polish Improvements Design

**Date:** 2026-03-15

## Goal

Four targeted improvements to world feel and interaction: smoother coastline, larger world objects, left-click harvesting, and stable dropped item physics.

---

## Section 1: Gentler Coast Profile

### Problem
The beach-to-inland transition is too abrupt. The terrain rises from flat to plateau in only 15 world units (z=2→17), and the underwater slope is steep. The result feels like a wall rather than a coast.

### Design

Update `HeightAt` formula on both client and server:

**New formula:**
```
if (z < 0): return max(z * 0.15, -3)          // gentler underwater slope (was 0.3)
t     = clamp((z - 5) / 30, 0, 1)             // rise starts at z=5, ends at z=35 (was 2→17)
baseH = 2 * t² * (3 - 2t)                      // same smoothstep shape, just wider
nr    = clamp((z - 8) / 20, 0, 1)             // noise ramp starts at z=8 (was z=5)
HeightAt = baseH + noise * nr
```

Also lower `NoiseAmplitude` from 1.5 → 1.2 in TerrainConfig (server seed + client default).

**Effect:** Wide flat sand from z=0 to z=5, gentle 30-unit climb to the plateau, hills only start after z=8, ocean floor drops away slowly.

### Files changed
- `server/Lifecycle.cs` — update `TerrainHeightAt` formula + `TerrainNoiseAmp` constant (1.5 → 1.2)
- `server/Lifecycle.cs` — update `SeedTerrainConfig` to insert `NoiseAmplitude = 1.2f`
- `client/scripts/world/Terrain.cs` — update `HeightAt` formula + `_noiseAmp` default (1.5 → 1.2)

---

## Section 2: Larger World Object Visuals

### Problem
Trees, rocks, and bushes are too small relative to the 500-unit world. GLB models are instantiated at 1:1 scale with no adjustment.

### Design

Apply a per-type scale to the instantiated model node in `CreateWorldObjectVisual`. Update the collision `BoxShape3D` sizes to match.

| Type | Model scale | New collision (X, Y, Z) |
|------|-------------|--------------------------|
| tree_pine | 2.5× | 1.2, 6.0, 1.2 |
| tree_dead | 2.0× | 1.0, 5.0, 1.0 |
| tree_palm | 2.5× | 1.2, 6.0, 1.2 |
| rock_large | 2.0× | 2.4, 1.6, 2.4 |
| rock_small | 1.8× | 1.1, 0.7, 1.1 |
| bush | 1.5× | 1.5, 1.0, 1.5 |

Collision center Y offset stays at `shapeSize.Y / 2f` so shapes remain ground-aligned.

### Files changed
- `client/scripts/world/WorldManager.cs` — scale model node + update collision sizes in `CreateWorldObjectVisual`

---

## Section 3: Left-Click Harvesting

### Problem
Harvesting is mapped to E (`interact`). Tools should be used with the mouse — left click swings the tool to harvest.

### Design

Add a `primary_attack` input action (Left Mouse Button). Context determines what left click does:
- **Buildable item active** → BuildSystem handles placement (existing behavior, no change)
- **Tool/empty active** → InteractionSystem triggers harvest

The contexts are mutually exclusive so there is no conflict.

**Input changes:**
- `project.godot` — add `primary_attack` action → Left Mouse Button
- Keep `interact` (E) for item pickup only

**Code changes:**
- `InteractionSystem.cs` — change harvest from `IsActionJustPressed("interact")` to `IsActionJustPressed("primary_attack")`; add guard: skip if `BuildSystem.IsBuildable(activeItem)` returns true
- `BuildSystem.cs` — change place trigger to `IsActionJustPressed("primary_attack")`; change `BuildableTypes` from `private` to `private static` and expose a `public static bool IsBuildable(string? itemType)` helper so InteractionSystem can check without a direct reference to a BuildSystem instance

### Files changed
- `client/project.godot` — add `primary_attack` input action
- `client/scripts/world/InteractionSystem.cs` — use primary_attack for harvest, skip when building
- `client/scripts/building/BuildSystem.cs` — use primary_attack for placement

---

## Section 4: Stable Dropped Item Physics

### Problem
Dropped world items (RigidBody3D) tumble and roll away after falling. The Label3D child inherits the parent's rotation, causing the floating label to spin with the physics body.

### Design

Lock all angular axes on the `RigidBody3D` in `CreateWorldItemVisual`. The body can still translate (fall, slide) but cannot rotate. This prevents rolling and keeps the label upright without any separate tracking logic.

```csharp
var body = new RigidBody3D
{
    LinearDamp       = 2.0f,
    AngularDamp      = 10f,   // high value absorbs any initial spin on spawn
    AxisLockAngularX = true,
    AxisLockAngularY = true,
    AxisLockAngularZ = true,
};
```

The existing `Billboard = Enabled` on the Label3D continues to make it face the camera correctly.

### Files changed
- `client/scripts/world/WorldManager.cs` — add axis locks + AngularDamp to RigidBody3D in `CreateWorldItemVisual`

---

## Out of Scope
- Procedural beach decoration (shells, driftwood) — future feature
- Tool swing animation — future feature
- Right-click secondary action — future feature
