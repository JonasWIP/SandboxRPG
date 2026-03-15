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
    public string ObjectType;   // "tree_pine", "rock_large", "tree_dead", "bush", etc.
    public float PosX, PosY, PosZ;
    public float RotY;          // Y-axis rotation only
    public uint Health;
    public uint MaxHealth;
}
```

Note: `Health`/`MaxHealth` use `uint` (integer hit points). Pre-existing `PlacedStructure` uses `float` — that inconsistency is out of scope.

**New file — `server/WorldReducers.cs`:**

`DamageWorldObject(ulong id, uint damage)` reducer — full implementation pattern:
```csharp
var obj = ctx.Db.WorldObject.Id.Find(id);  // Index accessor: .Id.Find(key)
if (obj is null) return;
var o = obj.Value;  // unpack once — matches codebase convention (see BuildingReducers.cs)

uint newHealth = o.Health <= damage ? 0 : o.Health - damage;

// Always delete the old row first (no UpdateByField in STDB 2.0)
ctx.Db.WorldObject.Delete(o);

if (newHealth == 0) {
    // Spawn a drop item inline — no separate helper
    ctx.Db.WorldItem.Insert(new WorldItem {
        ItemType = DropTypeFor(o.ObjectType),
        Quantity = 1,
        PosX = o.PosX, PosY = o.PosY, PosZ = o.PosZ
    });
} else {
    // Reinsert with updated health. Note: [AutoInc] on Id means STDB assigns a new Id
    // on every insert — the client will see an OnDelete + OnInsert pair per hit (brief
    // visual flicker). Acceptable for Phase 1; can be optimised later.
    ctx.Db.WorldObject.Insert(new WorldObject {
        ObjectType = o.ObjectType,
        PosX = o.PosX, PosY = o.PosY, PosZ = o.PosZ,
        RotY = o.RotY,
        Health = newHealth,
        MaxHealth = o.MaxHealth
        // Do NOT set Id — AutoInc field, STDB assigns it
    });
}
```

`DropTypeFor` private static helper:
```csharp
private static string DropTypeFor(string objectType) => objectType switch {
    "rock_large" or "rock_small" => "stone",
    _ => "wood"   // trees, stumps, bushes all drop wood
};
```

**Modified — `server/Lifecycle.cs`:**

Add `TerrainHeightAt` static helper (mirrors client formula; no `Mathf` on server):
```csharp
private static float TerrainHeightAt(float x, float z) {
    float t = Math.Clamp((z - 5f) / 20f, 0f, 1f);
    return t * t * (3f - 2f * t) * 4f;  // SmoothStep(0, 4, t)
}
```

In `Init` reducer: seed ~150–200 `WorldObject` rows using a fixed `Random` with a seed constant, calling `TerrainHeightAt(x, z)` for Y:
- 40–60 `"tree_pine"` on the inland plateau (Z range 20–50)
- 20–30 `"rock_large"` / `"rock_small"` on beach and hillside (Z range 0–30)
- 10–15 `"tree_dead"` at the treeline (Z range 15–25) — no separate "stump" type; `tree_dead` covers stumps
- 8–10 `"bush"` near spawn (Z range 5–20)

Also update the existing `SeedWorldItems()` call to use `TerrainHeightAt(x, z) + 0.2f` for Y instead of the hardcoded `0.5f`, so dropped items don't go underground when terrain is added in Phase 2.

In `ClientConnected` reducer: change player spawn from `PosY = 1f` to `PosY = 0.3f`. Note: during Phase 1 testing (before terrain is added) the player will float 0.3 units above the old flat ground — this is expected and harmless.

### Client changes

**Modified — `client/scripts/networking/GameManager.cs`:**

Add (same pattern as existing wrappers/signals):
```csharp
// Signal — use long not ulong (Godot Variant only supports long for integers)
[Signal] public delegate void WorldObjectUpdatedEventHandler(long id, bool removed);

// Reducer wrapper
public void DamageWorldObject(ulong id, uint damage)
    => Conn?.Reducers.DamageWorldObject(id, damage);

// In RegisterCallbacks():
conn.Db.WorldObject.OnInsert += (ctx, obj) =>
    EmitSignal(SignalName.WorldObjectUpdated, (long)obj.Id, false);
conn.Db.WorldObject.OnDelete += (ctx, obj) =>
    EmitSignal(SignalName.WorldObjectUpdated, (long)obj.Id, true);
```

**New file — `client/scripts/world/Terrain.cs`:**
- Attached to a `StaticBody3D` node in `Main.tscn`
- In `_Ready()`: generates an `ArrayMesh` (100×100 units, 50×50 quad subdivisions) using:
  ```csharp
  public static float HeightAt(float x, float z) {
      float t = Mathf.Clamp((z - 5f) / 20f, 0f, 1f);
      return Mathf.SmoothStep(0f, 4f, t);
  }
  ```
  Beach (Z < 5) stays at Y=0; smooth rise to Y=4 by Z=25; plateau flat beyond Z=25.
- Builds `HeightMapShape3D` collision from the same function
- Exposes `HeightAt` as `public static` so `WorldManager` can use it for Y-offset verification

**Modified — `client/scripts/world/WorldManager.cs`:**

Add differential-update pattern for `WorldObject` (intentionally different from the full-rebuild pattern used for `WorldItem`/`PlacedStructure`):
- New `_worldObjects: Dictionary<ulong, Node3D>`
- On `GameManager.WorldObjectUpdated(long id, bool removed)` signal:
  - `removed=false`: guard with `if (_worldObjects.ContainsKey((ulong)id)) return;` before calling `CreateWorldObjectVisual` — prevents duplicate nodes if the signal fires during initial subscription apply
  - `removed=true`: queue-free `_worldObjects[(ulong)id]`, remove from dict
- On initial connect (`OnSubscriptionApplied` or equivalent): iterate `conn.Db.WorldObject` and call `CreateWorldObjectVisual` for each row, using the same `ContainsKey` guard

`CreateWorldObjectVisual(WorldObject obj)`:
- Load `ResourceLoader.Load<PackedScene>(ModelPath(obj.ObjectType))` — see asset mapping below
- Instantiate, add to scene, set `GlobalPosition = new Vector3(obj.PosX, obj.PosY, obj.PosZ)`
- Set `RotationDegrees = new Vector3(0, obj.RotY, 0)`
- Add to group `"world_object"` and store id: `node.SetMeta("world_object_id", (long)obj.Id)`
- Note: Kenney tree models have base at Y=0 (no offset needed). Rock models have centred origins — apply `+0.3f` Y offset for rocks. Verify on import; adjust per-type if needed.

`ModelPath(string type)` switch:
```csharp
private static string ModelPath(string type) => type switch {
    "tree_pine"  => "res://assets/models/nature-kit/gltf/tree_pine.glb",
    "tree_dead"  => "res://assets/models/nature-kit/gltf/tree_dead.glb",
    "rock_large" => "res://assets/models/nature-kit/gltf/rock_large.glb",
    "rock_small" => "res://assets/models/nature-kit/gltf/rock_small.glb",
    "bush"       => "res://assets/models/nature-kit/gltf/bush.glb",
    _            => null
};
```

Existing structure visuals — swap in `CreateStructureVisual` (same method, replace mesh creation with `ResourceLoader.Load`):

| StructureType | `res://` path |
|---|---|
| `campfire`    | `res://assets/models/survival-kit/gltf/campfire.glb` |
| `chest`       | `res://assets/models/survival-kit/gltf/chest.glb` |
| `workbench`   | `res://assets/models/survival-kit/gltf/workbench.glb` |
| `wood_wall`   | `res://assets/models/building-kit/gltf/wall_wood.glb` |
| `wood_floor`  | `res://assets/models/building-kit/gltf/floor_wood.glb` |
| `stone_wall`  | `res://assets/models/building-kit/gltf/wall_stone.glb` |
| `stone_floor` | `res://assets/models/building-kit/gltf/floor_stone.glb` |
| `wood_door`   | `res://assets/models/building-kit/gltf/door_wood.glb` |

World item visuals — swap in `CreateWorldItemVisual`:
| ItemType | Model |
|---|---|
| `wood`  | `res://assets/models/nature-kit/gltf/log.glb` (fallback to existing brown box if absent) |
| `stone` | `res://assets/models/nature-kit/gltf/rock_small.glb` |
| `iron`  | fallback to existing grey box |

**Modified — `client/scripts/world/InteractionSystem.cs`:**

The existing `CheckNearbyWorldItems()` proximity check is unchanged. Add a new physics raycast branch alongside it for world objects:

In `_Process`:
```csharp
// Existing proximity check for items — unchanged
CheckNearbyWorldItems();

// New: raycast for world objects
if (Input.IsActionJustPressed("interact")) {   // same key as item pickup
    var camera = GetViewport().GetCamera3D();
    var spaceState = camera.GetWorld3D().DirectSpaceState;
    var from = camera.GlobalPosition;
    var to = from + (-camera.GlobalTransform.Basis.Z) * 5f;
    var query = PhysicsRayQueryParameters3D.Create(from, to);
    var result = spaceState.IntersectRay(query);
    if (result.Count > 0 && result["collider"].AsGodotObject() is Node hit
        && hit.IsInGroup("world_object")) {
        var id = (ulong)(long)hit.GetMeta("world_object_id");
        GameManager.Instance.DamageWorldObject(id, 25);
    }
}
```

**Modified — `client/scripts/building/BuildSystem.cs`:**

Replace the math ray-plane intersection (`float t = -from.Y / direction.Y`) with a physics raycast. Also fix ghost rotation to preserve player's accumulated Y rotation on slopes:

```csharp
// NEW field — does not exist yet in BuildSystem.cs
private float _ghostRotationY = 0f;

// Replace the existing continuous RotateY R-key handler with a discrete 90° step.
// Current code: if (Input.IsKeyPressed(Key.R) && _ghostPreview != null) _ghostPreview.RotateY(...)
// New code (in _Process, before UpdateGhostPosition):
//   if (Input.IsActionJustPressed("ui_rotate") || Input.IsKeyPressed(Key.R) && !_rWasPressed) {
//       _ghostRotationY = (_ghostRotationY + 90f) % 360f;
//   }
// The live RotateY call must be REMOVED — it conflicts with the transform-composition below.

private void UpdateGhostPosition() {
    var camera = GetViewport().GetCamera3D();
    var from = camera.GlobalPosition;
    var dir = -camera.GlobalTransform.Basis.Z;
    var spaceState = camera.GetWorld3D().DirectSpaceState;
    var query = PhysicsRayQueryParameters3D.Create(from, from + dir * 15f);
    var result = spaceState.IntersectRay(query);
    if (result.Count == 0) return;

    var hitPos = (Vector3)result["position"];
    var normal = (Vector3)result["normal"];

    // Grid snap X/Z only; Y from terrain hit
    hitPos.X = Mathf.Round(hitPos.X / GridSize) * GridSize;
    hitPos.Z = Mathf.Round(hitPos.Z / GridSize) * GridSize;

    // Build basis: align up to surface normal, then apply player Y rotation
    var up = normal.Normalized();
    var right = up.Cross(Vector3.Forward).Normalized();
    var forward = right.Cross(up).Normalized();
    var surfaceBasis = new Basis(right, up, -forward);
    var yRotation = Basis.FromEuler(new Vector3(0, Mathf.DegToRad(_ghostRotationY), 0));
    _ghostNode.GlobalTransform = new Transform3D(surfaceBasis * yRotation, hitPos);
}
```

No changes to hotbar flow or `PlaceStructure` reducer call. The server already stores PosY from hit position.

**Modified — `client/scenes/Main.tscn`:**
- Remove old flat `Ground` StaticBody3D node
- Add `Terrain` StaticBody3D (with `Terrain.cs` script, `MeshInstance3D` child, `CollisionShape3D` child)
- Add `Ocean` MeshInstance3D: PlaneMesh at Y=-0.2, size 80×100, `StandardMaterial3D` with albedo (0.2, 0.5, 0.7), `Transparency = Alpha`, alpha 0.75, roughness 0.1 — must set `Transparency` flag or plane will be opaque
- Adjust `WorldEnvironment`: coastal sky colour, fog colour (0.7, 0.85, 0.9), fog density 0.003

### Regenerated bindings

After adding `WorldObject` table and `DamageWorldObject` reducer, regenerate:
```bash
cd server && spacetime generate --lang csharp \
  --out-dir ../client/scripts/networking/SpacetimeDB \
  --bin-path bin/Release/net8.0/wasi-wasm/AppBundle/StdbModule.wasm
```

---

## Kenney Asset Setup

Download and unzip:
- [Nature Kit](https://kenney.nl/assets/nature-kit)
- [Survival Kit](https://kenney.nl/assets/survival-kit)
- [Building Kit](https://kenney.nl/assets/building-kit)

Rename each pack's `GLTF format` folder to `gltf` (removes the space from the path). Copy to `client/assets/models/`:
```
client/assets/models/
├── nature-kit/gltf/     (tree_pine.glb, tree_dead.glb, rock_large.glb, rock_small.glb, bush.glb, log.glb, ...)
├── survival-kit/gltf/   (campfire.glb, chest.glb, workbench.glb, ...)
└── building-kit/gltf/   (wall_wood.glb, floor_wood.glb, wall_stone.glb, floor_stone.glb, door_wood.glb, ...)
```

Verify actual filenames after unzipping — Kenney sometimes uses underscores vs hyphens. Update `ModelPath` switch to match exact filenames on disk.

---

## Implementation Phases

### Phase 1 — Server World Objects + Kenney Visuals
1. Add `WorldObject` table to `server/Tables.cs`
2. Add `server/WorldReducers.cs` with `DamageWorldObject` + `DropTypeFor`
3. Add `TerrainHeightAt` static helper to `Lifecycle.cs`; seed world objects in `Init` using it; update `SeedWorldItems` Y positions
4. Fix player spawn Y in `ClientConnected`: `1f` → `0.3f`
5. Build + publish server; regenerate client bindings
6. Download Kenney packs; rename `GLTF format` → `gltf`; copy to `client/assets/models/`
7. Add `WorldObjectUpdated` signal (with `long` id param), `DamageWorldObject` wrapper, and `OnInsert`/`OnDelete` callbacks to `GameManager.cs`
8. Update `WorldManager.cs`: differential-update pattern for `WorldObject`, `CreateWorldObjectVisual`, `ModelPath`
9. Update `InteractionSystem.cs`: add raycast branch for world objects alongside existing proximity check
10. Swap structure visuals in `CreateStructureVisual` to use Kenney building/survival-kit models
11. Swap world item visuals in `CreateWorldItemVisual` to use Kenney prop models

### Phase 2 — Terrain + Water + Textures
1. Write `client/scripts/world/Terrain.cs` (mesh generator + `public static HeightAt`)
2. Write `client/assets/shaders/terrain_blend.gdshader` (blend sand/grass by vertex Y)
3. Add sand + grass textures to `client/assets/textures/`
4. Replace `Ground` node in `Main.tscn` with `Terrain` StaticBody3D
5. Add `Ocean` MeshInstance3D to `Main.tscn` (set `Transparency = Alpha` on material)
6. Adjust `WorldEnvironment` (sky colour, fog)

### Phase 3 — Building Slope Snap
1. Replace math ray-plane intersection in `BuildSystem.cs` with `spaceState.IntersectRay`
2. Build normal-aligned basis; compose with `_ghostRotationY` to preserve player rotation
3. Grid snap X/Z only, Y from hit position
4. Test placement on beach slope and plateau

---

## What Does Not Change

- All SpacetimeDB networking, connection, authentication
- `Player`, `WorldItem`, `PlacedStructure`, `InventoryItem`, `CraftingRecipe`, `ChatMessage` tables and their reducers
- `PlayerController.cs`, `RemotePlayer.cs`, all UI scripts (HUD, InventoryUI, CraftingUI, ChatUI)
- Hotbar-driven build flow (only ghost positioning changes in Phase 3)
