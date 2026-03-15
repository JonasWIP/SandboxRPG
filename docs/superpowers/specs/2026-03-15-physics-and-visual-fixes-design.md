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

Currently the code structure is:
```
model branch (if modelPath exists)   → adds model visual
fallback branch (else)               → adds box mesh
collision shape                      → BoxShape3D added unconditionally after both branches
```

The restructured flow moves the collision creation inside each branch:
```
model branch    → adds model visual + ConvexPolygonShape3D (from mesh vertices)
fallback branch → adds box mesh   + BoxShape3D (existing sizes)
```

A private helper `BuildConvexShape(Node3D model, float scale)`:
1. Recursively traverses all `MeshInstance3D` children of the model node
2. Collects triangle vertices via `Mesh.GetFaces()` — works for any Mesh subclass
3. Scales each vertex by `modelScale` (matches the visual scale already applied to the node)
4. Assigns the result to `ConvexPolygonShape3D.Points` — Godot computes the convex hull automatically

Note: `GetFaces()` returns mesh-local vertices. For Kenney GLB assets the root node has identity transform so scaling by `modelScale` is sufficient. Baked sub-node transforms are out of scope.

**Effect:** Collision follows model silhouette — tree trunks are narrow, rock shapes are rounded, bushes are compact.

### Files changed
- `client/scripts/world/WorldManager.cs` — add `BuildConvexShape` helper; restructure `CreateWorldObjectVisual` to add collision inside each branch

---

## Section 2: Ghost Preview Using Real Model (Rotation Fix)

### Problem

The ghost preview always shows a plain fallback box (`StructureFallbackMesh`), while the placed structure shows the real Kenney GLB model. Kenney models have a baked 90° rotation in their local space that the fallback box does not have, causing the placed structure to appear 90° rotated relative to what the ghost showed.

### Design

Update `CreateGhostPreview` in `BuildSystem.cs` to load the actual GLB model when one exists, then apply the transparent green material recursively to all `MeshInstance3D` children (same pattern as `TintMeshes` in `WorldManager`).

Supporting changes in `WorldManager.cs`:
- Add `public static string? StructureModelPath(string t)` — mirrors the existing `StructureFallbackMesh` / `StructureYOffset` pattern, returns the `res://` path for a structure type or `null` if none
- Update `CreateStructureVisual` to call `StructureModelPath` instead of its current inline switch, so both callers stay in sync

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

Players cannot stack walls vertically. `CreateStructureVisual` currently returns a plain `Node3D` with no physics body — placed structures have no collision at all. Players walk through walls and the BuildSystem raycast cannot hit them. Adding `StaticBody3D` + `CollisionShape3D` to structures fixes stacking and also makes walls and floors physically solid.

### Design

Change `CreateStructureVisual` to return a `StaticBody3D` instead of a plain `Node3D`. Add a per-type `CollisionShape3D` child:

| Structure type | BoxShape3D Size | Position (center) |
|---|---|---|
| wall (wood/stone) | `(2.5f, 2.5f, 0.25f)` | `(0, 1.25f, 0)` |
| floor (wood/stone) | `(2.5f, 0.1f, 2.5f)` | `(0, 0.05f, 0)` |
| door | `(2.5f, 2.5f, 0.25f)` | `(0, 1.25f, 0)` |
| campfire | `(0.8f, 0.4f, 0.8f)` | `(0, 0.2f, 0)` |
| workbench | `(1.2f, 0.8f, 0.6f)` | `(0, 0.4f, 0)` |
| chest | `(0.8f, 0.6f, 0.6f)` | `(0, 0.3f, 0)` |

The `_structures` dictionary type changes from `Dictionary<ulong, Node3D>` to `Dictionary<ulong, StaticBody3D>`. The visual model (GLB or fallback mesh) becomes a child of the `StaticBody3D` exactly as before.

The BuildSystem raycast already hits any `StaticBody3D`. Once walls have collision, pointing at the top of an existing wall returns a hitPos Y equal to the wall's top face — and the ghost positions itself there automatically.

**Effect:** Walls and floors are physically solid. Pointing at the top of a wall and placing creates a stacked wall.

### Files changed
- `client/scripts/world/WorldManager.cs` — change `CreateStructureVisual` to use `StaticBody3D` as root; add per-type `CollisionShape3D`; update `_structures` dictionary type

---

## Out of Scope
- Per-mesh compound collision (multiple convex shapes per model) — convex hull is sufficient for gameplay
- Water physics / buoyancy — visual only
- Multi-level building snapping beyond natural raycast — no auto-elevation logic needed
