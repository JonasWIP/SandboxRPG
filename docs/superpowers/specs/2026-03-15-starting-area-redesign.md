# Starting Area Redesign

**Date:** 2026-03-15
**Status:** Approved

## Overview

Replace the current placeholder visuals (flat green plane, coloured primitive meshes) with a proper low-poly coastal starting area. All world state is authoritative on the server via SpacetimeDB. Client work is purely visual — no gameplay logic changes.

---

## Style

- **Art direction:** Low poly / flat-shaded, bold colours, minimal detail (Rust early / Ylands aesthetic)
- **Setting:** Coastal shoreline — beach at water's edge, rising to a grassy plateau inland
- **Assets:** Kenney.nl free packs (Nature Kit, Survival Kit, Building Kit) — .glb files imported into `client/assets/models/`
- **Textures:** Kenney pack textures + CC0 textures from ambientCG for ground blend

---

## Architecture

### Server changes

**New table — `WorldObject`** (added to `server/Tables.cs`):
```csharp
[Table(Name = "world_object", Public = true)]
public partial struct WorldObject {
    [PrimaryKey, AutoInc] public ulong Id;
    public string ObjectType;   // "tree_pine", "rock_large", "bush", etc.
    public float PosX, PosY, PosZ;
    public float RotY;          // Y-axis rotation only
    public uint Health;
    public uint MaxHealth;
}
```

**New file — `server/WorldReducers.cs`:**
- `DamageWorldObject(ulong id, uint damage)` — subtract health from row; when Health reaches 0, delete row and call `SpawnItemDrop` to create a `WorldItem` (wood from trees, stone from rocks)

**Modified — `server/Lifecycle.cs` (`Init` reducer):**
- Seed ~150–200 `WorldObject` rows using a fixed RNG seed:
  - 40–60 pine trees on the inland plateau
  - 20–30 large/small rocks on beach and hillside
  - 10–15 dead trees / stumps at the treeline
  - 8–10 bushes in the clearing near spawn

### Client changes

**New file — `client/scripts/world/Terrain.cs`:**
- Generates an `ArrayMesh` (100×100 units) using a smooth height function:
  `height = Mathf.SmoothStep(0, maxHeight, (z - beachEnd) / slopeWidth)`
- Beach strip near Z=0 stays at Y=0; rises to Y≈4 inland
- `HeightMapShape3D` collision sampled from same function
- Two-texture blend shader (sand below Y=0.5, grass above) — single `.gdshader` file

**Modified — `client/scripts/world/WorldManager.cs`:**
- New `_worldObjects: Dictionary<ulong, Node3D>`
- `CreateWorldObjectVisual(WorldObject obj)` — load Kenney .glb by `ObjectType`, place at position/rotation
- Subscribe to `WorldObject` insert/delete events (same pattern as `WorldItem`)

**Modified — `client/scripts/world/InteractionSystem.cs`:**
- Additional raycast branch: if hit node is a world object → call `GameManager.DamageWorldObject(id, damage)` on E press (or left-click with tool equipped)

**Modified — `client/scripts/building/BuildSystem.cs`:**
- Normal alignment: read `collision.Normal` from raycast hit, rotate ghost mesh basis to align with surface normal
- Grid snap applies to X/Z only; Y taken from raycast hit position
- No changes to hotbar flow or server reducer calls

**Modified — `client/scenes/Main.tscn`:**
- Remove old flat `Ground` node
- Add `Terrain` node (Node3D + `Terrain.cs`)
- Add `Ocean` node (MeshInstance3D, PlaneMesh at Y=-0.2, semi-transparent blue material, size 80×100)
- Adjust `WorldEnvironment`: coastal sky colour, slight fog haze

### Regenerated bindings

After adding `WorldObject` table and `DamageWorldObject` reducer, regenerate client bindings:
```bash
cd server && spacetime generate --lang csharp \
  --out-dir ../client/scripts/networking/SpacetimeDB \
  --bin-path bin/Release/net8.0/wasi-wasm/AppBundle/StdbModule.wasm
```

---

## Kenney Asset Mapping

| ObjectType | File |
|---|---|
| `tree_pine` | `nature-kit/Models/GLTF format/tree_pine.glb` |
| `tree_dead` | `nature-kit/Models/GLTF format/tree_dead.glb` |
| `rock_large` | `nature-kit/Models/GLTF format/rock_large.glb` |
| `rock_small` | `nature-kit/Models/GLTF format/rock_small.glb` |
| `bush` | `nature-kit/Models/GLTF format/bush.glb` |

Structure / item visuals (Phase 2 of model swap, same phase):
| Structure | File |
|---|---|
| `campfire` | `survival-kit/Models/GLTF format/campfire.glb` |
| `chest` | `survival-kit/Models/GLTF format/chest.glb` |
| `workbench` | `survival-kit/Models/GLTF format/workbench.glb` |
| `wood_wall` | `building-kit/Models/GLTF format/wall_wood.glb` |
| `wood_floor` | `building-kit/Models/GLTF format/floor_wood.glb` |
| `stone_wall` | `building-kit/Models/GLTF format/wall_stone.glb` |
| `stone_floor` | `building-kit/Models/GLTF format/floor_stone.glb` |

---

## Implementation Phases

### Phase 1 — Server World Objects + Kenney Visuals
1. Add `WorldObject` table to `server/Tables.cs`
2. Add `WorldReducers.cs` with `DamageWorldObject`
3. Seed world objects in `Lifecycle.cs` `Init`
4. Build + publish server, regenerate bindings
5. Download Kenney Nature Kit + Survival Kit + Building Kit → `client/assets/models/`
6. Update `WorldManager.cs`: subscribe to `WorldObject`, spawn Kenney models
7. Update `InteractionSystem.cs`: E on world objects → `DamageWorldObject`
8. Swap structure visuals in `WorldManager.cs` to use Kenney building-kit models
9. Swap world item visuals (wood/stone/iron) to use Kenney prop models

### Phase 2 — Terrain + Water + Textures
1. Write `Terrain.cs` heightmap mesh generator
2. Write terrain blend shader (`client/assets/shaders/terrain_blend.gdshader`)
3. Add sand/grass textures to `client/assets/textures/`
4. Replace `Ground` node in `Main.tscn` with `Terrain` node
5. Add `Ocean` water plane to `Main.tscn`
6. Adjust `WorldEnvironment` (sky, fog)

### Phase 3 — Building Slope Snap
1. Update `BuildSystem.cs`: read `collision.Normal`, align ghost basis to surface normal
2. Apply grid snap to X/Z only, Y from hit position
3. Test placement on various slope angles

---

## What Does Not Change

- All SpacetimeDB networking, connection, authentication
- `Player`, `WorldItem`, `PlacedStructure`, `InventoryItem`, `CraftingRecipe`, `ChatMessage` tables
- All existing reducers (player movement, inventory, crafting, chat)
- `GameManager.cs` (signals and reducer call wrappers — only additions for `DamageWorldObject`)
- Hotbar-driven build flow
- `PlayerController.cs`, `RemotePlayer.cs`, all UI scripts
