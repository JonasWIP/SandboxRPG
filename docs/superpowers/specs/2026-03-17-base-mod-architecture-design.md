# Base Mod Architecture Design

**Date:** 2026-03-17
**Status:** Approved
**Topic:** Extract all game content into a "base" mod, leaving the engine as pure infrastructure

---

## 1. Goal

Reorganise SandboxRPG so that the engine (server + client scripts) provides only tools and infrastructure, and all gameplay content (items, structures, world objects, recipes, UI, spawners) lives in a first-party mod called `base`. Other mods may declare a dependency on `base` and extend it.

**Behaviour does not change.** This is a source-level reorganisation only.

---

## 2. Architecture Overview

```
┌─────────────────────────────────────────────────┐
│  Other Mods  (e.g. hello-world, food, farming)  │
│  → depends on "base"                            │
├─────────────────────────────────────────────────┤
│  Base Mod  (mods/base/)                         │
│  All current game content: tables, reducers,    │
│  spawners, UI, registries, seed data            │
├─────────────────────────────────────────────────┤
│  Engine  (server/ + client/scripts/)            │
│  Pure infrastructure: mod loading, SpacetimeDB  │
│  connection, lifecycle hook forwarding          │
└─────────────────────────────────────────────────┘
```

---

## 3. Server Split

### Engine (`server/`)

Three files remain after the refactor:

| File | Role |
|---|---|
| `mods/IMod.cs` | Interface — updated to add `OnClientConnected` hook |
| `mods/ModLoader.cs` | Topological sort, `RunAll`, `ForwardClientConnected` |
| `Lifecycle.cs` | Thin dispatcher — forwards to ModLoader only |

`Lifecycle.cs` after refactor:
```csharp
[Reducer("init")]
public static void Init(ReducerContext ctx) => ModLoader.RunAll(ctx);

[Reducer("client_connected")]
public static void ClientConnected(ReducerContext ctx) =>
    ModLoader.ForwardClientConnected(ctx, ctx.Sender);

[Reducer("client_disconnected")]
public static void ClientDisconnected(ReducerContext ctx) { }
```

### Updated `IMod` Interface

```csharp
public interface IMod {
    string Name { get; }
    string Version { get; }
    string[] Dependencies { get; }
    void Seed(ReducerContext ctx);
    void OnClientConnected(ReducerContext ctx, Identity identity) { } // default empty
}
```

### Base Mod (`mods/base/server/`)

```
mods/base/server/
├── BaseMod.cs           ← implements IMod; wires Seed + OnClientConnected
├── Tables.cs            ← Player, InventoryItem, WorldItem, WorldObject,
│                           PlacedStructure, CraftingRecipe, ChatMessage, TerrainConfig
├── Seeding.cs           ← SeedRecipes(), SeedWorldObjects(), starter items
├── StructureConfig.cs   ← static registry: structure_type → max health
├── HarvestConfig.cs     ← static registry: tool damage + drop tables
├── PlayerReducers.cs
├── InventoryReducers.cs
├── CraftingReducers.cs
├── BuildingReducers.cs  ← uses StructureConfig instead of switch
└── WorldReducers.cs     ← uses HarvestConfig instead of switch
```

`StructureConfig` and `HarvestConfig` replace hard-coded switch statements in the reducers. `BaseMod.Seed()` populates them; dependent mods add to them in their own `Seed()`.

---

## 4. Client Split

### Engine stays in `client/scripts/`

| Path | Notes |
|---|---|
| `networking/GameManager.cs` | Unchanged |
| `networking/SpacetimeDB/` | Unchanged (generated) |
| `networking/SceneRouter.cs` | Unchanged |
| `mods/IClientMod.cs` | Unchanged |
| `mods/ModManager.cs` | Unchanged |
| `ui/UIManager.cs` | Unchanged (scene management, not game content) |

`WorldManager.cs` is **deleted** from the engine — its responsibilities move to `BaseClientMod.Initialize()`.

### Base Mod (`client/mods/base/`)

```
client/mods/base/
├── BaseClientMod.cs         ← Godot autoload; implements IClientMod
│                               registers all content, wires GameManager signals,
│                               sets up spawners and UI
├── registries/
│   ├── ItemRegistry.cs      ← itemType → ItemDef
│   ├── StructureRegistry.cs ← structureType → StructureDef
│   └── ObjectRegistry.cs   ← objectType → ObjectDef
├── content/
│   ├── BaseItems.cs         ← registers wood, stone, iron, tools...
│   ├── BaseStructures.cs    ← registers wood_wall, stone_wall, campfire...
│   └── BaseObjects.cs       ← registers tree_pine, rock_large, bush...
├── spawners/
│   ├── WorldItemSpawner.cs  ← uses ItemRegistry (no switch statements)
│   ├── StructureSpawner.cs  ← uses StructureRegistry
│   ├── WorldObjectSpawner.cs← uses ObjectRegistry
│   └── PlayerSpawner.cs
├── world/
│   └── WorldManager.cs      ← moved from engine; wires GameManager → spawners
├── building/
│   └── BuildSystem.cs       ← moved from engine; reads StructureRegistry for buildable types
└── ui/
    ├── HUD.cs
    ├── InventoryUI.cs
    ├── CraftingUI.cs
    ├── ChatUI.cs
    └── Hotbar.cs
```

---

## 5. Content Registries

All registries are static classes (plain static dictionaries). Spawners call `Registry.Get(typeString)` and fall back to a coloured box mesh if nothing is registered — preserving current fallback behaviour.

### Client-Side (in base mod)

**`ItemDef`**
```csharp
public class ItemDef {
    public string ModelPath;
    public string DisplayName;
    public int MaxStack = 64;
    public Color? TintColor;
}
```

**`StructureDef`**
```csharp
public class StructureDef {
    public string ModelPath;
    public Vector3 CollisionSize;
    public float YOffset;
    public Color? TintColor;
}
```

**`ObjectDef`**
```csharp
public class ObjectDef {
    public string ModelPath;
    public float Scale = 1.0f;
    public bool UseConvexCollision = true;
}
```

### Server-Side (in base mod)

**`StructureConfig`** — maps structure type → max health (used by `PlaceStructure` reducer)

**`HarvestConfig`** — maps (tool type, object type) → damage; (object type) → drop item + quantity (used by `HarvestWorldObject` reducer)

---

## 6. Lifecycle Hooks

`ModLoader` gains a `ForwardClientConnected(ctx, identity)` method that calls `OnClientConnected` on all registered mods in topological order. Base mod uses this to give starter items to new players.

```csharp
// BaseMod.cs
public void OnClientConnected(ReducerContext ctx, Identity identity) {
    ctx.Db.InventoryItem.Insert(new InventoryItem { Owner = identity, ItemType = "wood_pickaxe", Slot = 0, Quantity = 1 });
    ctx.Db.InventoryItem.Insert(new InventoryItem { Owner = identity, ItemType = "wood_axe",     Slot = 1, Quantity = 1 });
}
```

---

## 7. How Dependent Mods Use Base

A mod that adds new content only touches its own files:

**Server** (`mods/farming/server/FarmingMod.cs`):
```csharp
public class FarmingMod : IMod {
    public string[] Dependencies => new[] { "base" };
    public void Seed(ReducerContext ctx) {
        StructureConfig.Register("garden_bed", maxHealth: 150);
        HarvestConfig.RegisterDrop("copper_vein", "copper_ore", quantity: 3);
        HarvestConfig.RegisterToolDamage("iron_pickaxe", "copper_vein", damage: 25);
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe { ... });
    }
}
```

**Client** (`client/mods/farming/FarmingClientMod.cs`):
```csharp
public partial class FarmingClientMod : Node, IClientMod {
    public string ModName => "farming";
    public void Initialize(Node sceneRoot) {
        ItemRegistry.Register("copper_ore", new ItemDef {
            ModelPath = "res://mods/farming/models/copper_ore.glb",
            DisplayName = "Copper Ore"
        });
        ObjectRegistry.Register("copper_vein", new ObjectDef {
            ModelPath = "res://mods/farming/models/copper_vein.glb",
            Scale = 1.8f
        });
    }
}
```

No engine files touched. No base mod files touched. Spawners and reducers handle it automatically.

---

## 8. What Does Not Change

- SpacetimeDB compilation model (single WASM binary, compile-time mod inclusion)
- `IMod.Dependencies` string array + topological sort — unchanged mechanism
- Fallback coloured box meshes in spawners
- All public-facing game behaviour
- The hello-world mod structure (already follows the pattern)
- Generated bindings in `client/scripts/networking/SpacetimeDB/`

---

## 9. Out of Scope

- Dynamic/hot-loadable mods at runtime (not possible with SpacetimeDB WASM)
- JSON/resource-file mod definitions
- `OnClientDisconnected` hook (add later when a concrete use case arises)
- Any new gameplay features
