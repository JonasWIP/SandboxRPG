# Physics and Visual Fixes Design

**Date:** 2026-03-15

## Goal

Fix five issues: convex hull hitboxes for world objects, ghost preview using real model (fixes rotation mismatch), item spawn height (fixes fall-through), water plane coverage, and wall stacking via collision.

---

## Section 1: Convex Hull Hitboxes for World Objects

### Problem

World objects (trees, rocks, bushes) use generic `BoxShape3D` collision shapes that don't match their actual GLB model geometry. Trees have wide canopies but narrow trunks; a box covers neither accurately.

### Design

In `CreateWorldObjectVisual` in `WorldManager.cs`, when the GLB model loads successfully, replace the `BoxShape3D` with a `ConvexPolygonShape3D` derived from the actual mesh geometry.

A private helper `BuildConvexShape(Node3D model, float scale)`:
1. Recursively traverses all `MeshInstance3D` children of the model node
2. Collects triangle vertices via `Mesh.GetFaces()` — works for any Mesh subclass
3. Scales each vertex by `modelScale` (matches the visual scale already applied to the node)
4. Assigns the result to `ConvexPolygonShape3D.Points` — Godot computes the convex hull automatically

The existing `BoxShape3D` fallback is kept for when no GLB file exists (fallback mesh path).

**Effect:** Collision follows model silhouette — tree trunks are narrow, rock shapes are rounded, bushes are compact.

### Files changed
- `client/scripts/world/WorldManager.cs` — add `BuildConvexShape` helper; use it in `CreateWorldObjectVisual` when model loads

---

## Section 2: Ghost Preview Using Real Model (Rotation Fix)

### Problem

The ghost preview always shows a plain fallback box (`StructureFallbackMesh`), while the placed structure shows the real Kenney GLB model. Kenney models have a baked 90° rotation in their local space that the fallback box does not have, causing the placed structure to appear 90° rotated relative to what the ghost showed.

### Design

Update `CreateGhostPreview` in `BuildSystem.cs` to load the actual GLB model when one exists, then apply the transparent green material recursively to all `MeshInstance3D` children (same pattern as `TintMeshes` in `WorldManager`).

Supporting change in `WorldManager.cs`:
- Add `public static string? StructureModelPath(string t)` — mirrors the existing `StructureFallbackMesh` / `StructureYOffset` pattern, returns the `res://` path for a structure type or `null` if none

In `BuildSystem.CreateGhostPreview`:
1. Call `WorldManager.StructureModelPath(structureType)` — if a path exists and the resource loads, instantiate the GLB as the ghost node
2. Apply transparent green material recursively to all `MeshInstance3D` children of the instantiated model
3. Apply `StructureYOffset` as a child position offset (same as current fallback behavior)
4. If no model path exists, fall back to the current `MeshInstance3D` with `StructureFallbackMesh`

**Effect:** Ghost shows the real model shape and default orientation, so rotating the ghost with R exactly predicts how the placed structure will appear.

### Files changed
- `client/scripts/world/WorldManager.cs` — add `public static string? StructureModelPath(string t)`
- `client/scripts/building/BuildSystem.cs` — update `CreateGhostPreview` to use real model with transparent material

---

## Section 3: Item Spawn Height (Fall-Through Fix)

### Problem

Dropped world items (`RigidBody3D`) sometimes fall through the terrain. The `SphereShape3D` (radius 0.15f) spawns at `item.PosY` from the server, which is at or just below terrain level. The physics body clips through before the collision resolves.

### Design

In `CreateWorldItemVisual` in `WorldManager.cs`, offset the spawn Y by `+0.5f`:

```csharp
body.Position = new Vector3(item.PosX, item.PosY + 0.5f, item.PosZ);
```

The item spawns 0.5 units above its server-authoritative resting position and falls naturally onto the terrain surface. The `LinearDamp = 2.0f` and `AxisLockAngular` locks already in place ensure it settles cleanly.

**Effect:** Items no longer clip through terrain on spawn.

### Files changed
- `client/scripts/world/WorldManager.cs` — add `+0.5f` Y offset in `CreateWorldItemVisual`

---

## Section 4: Water Coverage

### Problem

The Ocean plane is `200×120` units at `Y=-0.2, Z=-30`. With the current terrain formula the shoreline is at `z=0` (where terrain height reaches 0), but the water plane doesn't extend far enough to cover the full coast and stops abruptly before the beach.

### Design

In `Main.tscn`, update the Ocean `MeshInstance3D`:

| Property | Old | New |
|----------|-----|-----|
| Mesh size | `Vector2(200, 120)` | `Vector2(600, 252)` |
| Position Y | `-0.2` | `0` (sea level) |
| Position Z | `-30` | `-124` |

The new dimensions cover X from −300 to +300 (world is 500 wide) and Z from −250 to +2 — past the world's south edge to just past the shoreline. Y=0 is true sea level matching the terrain formula's water line.

**Effect:** Water meets the beach smoothly with no visible edge.

### Files changed
- `client/scenes/Main.tscn` — update Ocean node transform and mesh size

---

## Section 5: Wall Stacking via Collision

### Problem

Players cannot stack walls vertically. The BuildSystem raycast already hits any `StaticBody3D`, so pointing at the top of an existing wall would naturally position the ghost above it — but only if the wall's collision shape covers the full wall height with its top face accessible.

### Design

Ensure placed wall collision shapes are full-height boxes centered correctly:

- Wall collision `BoxShape3D`: `Size = Vector3(2.5f, 2.5f, 0.25f)`, `Position = Vector3(0, 1.25f, 0)` — 2.5 units tall, centered at half-height so the top face is at Y=2.5 above the wall base

No logic changes needed. The existing BuildSystem raycast hits the wall's top face and returns the correct Y for the next wall to be placed on top.

**Effect:** Aiming at the top of a placed wall and clicking places a new wall directly above it.

### Files changed
- `client/scripts/world/WorldManager.cs` — update wall `CollisionShape3D` sizing in `CreateStructureVisual`

---

## Out of Scope
- Per-mesh compound collision (multiple convex shapes per model) — convex hull is sufficient for gameplay
- Water physics / buoyancy — visual only
- Multi-level building snapping beyond natural raycast — no auto-elevation logic needed
