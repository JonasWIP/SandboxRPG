# Collision and Water Fixes Design

**Date:** 2026-03-15

## Goal

Fix four issues found after in-game testing: water Z-fighting at the coast, world object convex hull not working (GetFaces() returns empty on Kenney GLBs), wall stacking gap, and dropped items using a generic sphere instead of their model shape.

---

## Section 1: Water Z-fighting

### Problem

The Ocean plane at `Y=0` clips with terrain vertices that sit at exactly `height=0` at the shoreline, causing visible flickering (Z-fighting).

### Design

In `client/scenes/Main.tscn`, lower the Ocean transform Y from `0` to `-0.05`. Five centimetres below sea level is visually imperceptible from above but eliminates the geometry overlap.

### Files changed
- `client/scenes/Main.tscn` — Ocean node Y: `0` → `-0.05`

---

## Section 2: Convex Hull Fix (World Objects + Dropped Items)

### Problem

`BuildConvexShape` uses `Mesh.GetFaces()` which returns empty data for Kenney GLB meshes imported at runtime. This results in `ConvexPolygonShape3D` with no points, falling back to a unit-sized or invalid collision shape that does not match the visual model.

### Design

Replace `BuildConvexShape` and `CollectFaces` with a new implementation that uses `Mesh.CreateConvexShape(clean: true, simplify: true)` — Godot's built-in convex hull generator that works reliably on all imported mesh types including Kenney GLBs.

New helper `BuildConvexShape(Node3D model, float scale)`:
1. Recursively traverse all `MeshInstance3D` children of `model`
2. For each mesh, call `mesh.CreateConvexShape(true, true)` to get a `ConvexPolygonShape3D`
3. Accumulate all `Points` arrays into one combined `List<Vector3>`
4. Multiply each accumulated point by `scale`
5. Return a single `ConvexPolygonShape3D { Points = combined.ToArray() }`

`CollectFaces` is removed entirely — no longer needed.

**Dropped items** (`CreateWorldItemVisual`):
- Replace `SphereShape3D { Radius = 0.15f }` with `BuildConvexShape(model, 1.0f)` on the loaded item model
- Keep `SphereShape3D { Radius = 0.15f }` as fallback when no model GLB exists (same pattern as world objects)
- The model must be instantiated before calling `BuildConvexShape` (same as world objects)

### Files changed
- `client/scripts/world/WorldManager.cs` — replace `BuildConvexShape` + remove `CollectFaces`; update `CreateWorldItemVisual` collision

---

## Section 3: Wall Stacking Gap

### Problem

`UpdateGhostPosition` in `BuildSystem.cs` snaps X and Z to the grid but leaves Y as the raw raycast hit position. Floating-point imprecision means the hit lands at e.g. `Y=2.497` instead of `Y=2.5`, producing a small visual gap between stacked walls.

### Design

After snapping X and Z, also snap Y to 0.25f increments:

```csharp
hitPos.Y = Mathf.Snapped(hitPos.Y, 0.25f);
```

Wall tops sit at exact multiples of 2.5 (which is divisible by 0.25), so snapping to 0.25 always lands at the correct stacking height. Terrain placement is unaffected — 0.25f granularity is fine-grained enough for natural surface building.

### Files changed
- `client/scripts/building/BuildSystem.cs` — add Y snap in `UpdateGhostPosition`

---

## Out of Scope
- Per-mesh compound collision (multiple convex shapes per sub-mesh) — single merged hull is sufficient
- Water physics / buoyancy
- Sub-0.25 terrain Y precision for building — not needed in practice
