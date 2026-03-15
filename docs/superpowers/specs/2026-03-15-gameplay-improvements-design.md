# Gameplay Improvements Design

**Date:** 2026-03-15

## Goal

Improve core gameplay loop: server-authoritative procedural terrain, tool-based harvesting, inventory-consuming placement, item drop physics, and three bug fixes.

---

## Section 1: Server-Authoritative Terrain

### Problem
Terrain is currently generated purely client-side from a hardcoded formula. Future terrain editing or procedural variation requires the server to own the terrain parameters.

### Design

**New STDB table: `TerrainConfig`**
```
Seed: uint           — drives noise variation
WorldSize: float     — 500 units (was 100)
NoiseScale: float    — spatial frequency of hills (default 0.04)
NoiseAmplitude: float — max hill height added on top of base (default 1.5)
```

Single-row table (singleton). Server seeds it in `Init`. Client subscribes; when the config row changes, it regenerates the terrain mesh and collision.

**Height formula (identical on server and client):**
```
baseHeight = smoothstep coastal profile (beach → slope → plateau)
noise      = sin(x*scale + seed) * amp
           + sin(z*scale*1.7 + seed*1.3) * amp*0.6
           + sin((x+z)*scale*2.9 + seed*0.7) * amp*0.3
HeightAt(x,z) = max(baseHeight + noise, oceanFloor(z))
```

Beach zone (Z < 2) has noise suppressed (multiplied by a ramp) so the spawn area stays flat. Inland gets rolling hills.

**World size:** 500 × 500 units, 100 subdivisions (5m grid cells). Object seeding ranges updated to match.

### Files changed
- `server/Tables.cs` — add `TerrainConfig` table
- `server/Lifecycle.cs` — seed TerrainConfig + update world object ranges to 500-unit world
- `client/scripts/world/Terrain.cs` — subscribe to TerrainConfig, regenerate on change, new HeightAt formula
- `client/scripts/networking/GameManager.cs` — expose TerrainConfig accessor + signal

---

## Section 2: Tool-Based Harvesting

### Problem
Any equipped item (or none) can harvest any world object with identical damage. Tools have no gameplay differentiation.

### Design

**Rename reducer:** `DamageWorldObject` → `HarvestWorldObject(objectId: ulong, toolType: string)`

Server validates:
1. Player has `toolType` in their inventory (at least 1 quantity)
2. Applies damage from lookup table

**Damage table:**
| Tool | Trees | Rocks |
|------|-------|-------|
| (empty/wrong) | 5 | 5 |
| wood_axe | 34 | 5 |
| wood_pickaxe | 5 | 34 |
| stone_pickaxe | 5 | 50 |
| iron_pickaxe | 5 | 75 |

**Client change:** `InteractionSystem` reads `Hotbar.Instance.ActiveItemType` and passes it to `GameManager.HarvestWorldObject(id, toolType)`.

### Files changed
- `server/WorldReducers.cs` — rename + add tool validation + damage lookup
- `client/scripts/networking/GameManager.cs` — update reducer wrapper signature
- `client/scripts/world/InteractionSystem.cs` — pass active tool type

---

## Section 3: Bug Fixes

### Fix A: Wood wall shows stone wall model

**Problem:** `CreateStructureVisual` maps both `wood_wall` and `stone_wall` to `wall.glb`. The model appears stone-grey regardless.

**Fix:** After instantiating the GLB model, apply `SetSurfaceOverrideMaterial(0, mat)` with a tinted `StandardMaterial3D`:
- Wood structures: albedo `Color(0.65, 0.45, 0.25)` (warm brown)
- Stone structures: albedo `Color(0.6, 0.6, 0.65)` (grey)
- Survival items (campfire, chest, workbench): no override (model colors are fine)

### Fix B: Building rotation incorrect

**Problem:** Ghost preview combines slope-normal alignment with Y rotation. The placed structure receives only the raw `_ghostRotationY` degrees. On sloped terrain, this mismatch means the visual ghost doesn't match the placed result.

**Fix:**
- Walls/doors always placed vertical (Y-rotation only) — ghost visually tilts but placement is clean
- Floors align to terrain surface normal — send the full surface-aligned rotation as Euler angles

### Fix C: Structure placement doesn't consume inventory

**Problem:** `PlaceStructure` reducer places structures for free — no inventory check or deduction.

**Fix:** Before inserting the structure row, verify the player has ≥1 of `structureType` in inventory. If not, return early. If yes, deduct 1 quantity (or delete the row if quantity reaches 0).

### Files changed
- `server/BuildingReducers.cs` — add inventory check + deduction in PlaceStructure
- `client/scripts/world/WorldManager.cs` — apply material tints per structure type
- `client/scripts/building/BuildSystem.cs` — fix floor rotation, keep wall rotation clean

---

## Section 4: Item Drop Gravity

### Problem
Dropped world items are placed at the harvested object's Y position and stay there, floating if the terrain is sloped or if the drop position is above ground.

### Design

`CreateWorldItemVisual` in `WorldManager` creates a `RigidBody3D` instead of a `Node3D`. The rigid body has a `SphereShape3D` (radius 0.15) as its collider. Items spawn at the server-provided Y and fall under Godot physics onto the terrain collision mesh.

No freeze logic — items come to rest naturally via physics damping (`linear_damp = 2.0`).

The label (Label3D) and mesh remain children of the RigidBody3D so they move with it.

### Files changed
- `client/scripts/world/WorldManager.cs` — `CreateWorldItemVisual` uses `RigidBody3D`

---

## Section 5: Code Simplification

After all changes, run `superpowers:simplify` on every modified file to remove duplication, improve clarity, and ensure consistent patterns across the codebase.

---

## Out of Scope

- Terrain mesh deformation (digging/raising ground) — future feature
- Tool durability / breaking — future feature
- Palm trees / additional biomes — future feature
