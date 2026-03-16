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
│  connection, lifecycle hook forwarding,         │
│  model caching, terrain math                    │
└─────────────────────────────────────────────────┘
```

---

## 3. Server Split

### Engine (`server/`)

Four files remain after the refactor:

| File | Role |
|---|---|
| `mods/IMod.cs` | Interface — updated to add `OnClientConnected` and `OnClientDisconnected` hooks |
| `mods/ModLoader.cs` | Topological sort, `RunAll`, `ForwardClientConnected`, `ForwardClientDisconnected` |
| `mods/ModLoaderHelpers.cs` | **Stays in engine** — `TopoSort<T>` utility used by `ModLoader`; pure logic, no SpacetimeDB dependency |
| `Lifecycle.cs` | Thin dispatcher — forwards to ModLoader only |

`Lifecycle.cs` after refactor:
```csharp
[Reducer("init")]
public static void Init(ReducerContext ctx) => ModLoader.RunAll(ctx);

[Reducer("client_connected")]
public static void ClientConnected(ReducerContext ctx) =>
    ModLoader.ForwardClientConnected(ctx, ctx.Sender);

[Reducer("client_disconnected")]
public static void ClientDisconnected(ReducerContext ctx) =>
    ModLoader.ForwardClientDisconnected(ctx, ctx.Sender);
```

### Updated `IMod` Interface

```csharp
public interface IMod {
    string Name { get; }
    string Version { get; }
    string[] Dependencies { get; }
    void Seed(ReducerContext ctx);
    void OnClientConnected(ReducerContext ctx, Identity identity) { }    // default empty
    void OnClientDisconnected(ReducerContext ctx, Identity identity) { } // default empty
}
```

### Base Mod Server (`mods/base/server/`)

```
mods/base/server/
├── BaseMod.cs           ← implements IMod; wires Seed, OnClientConnected, OnClientDisconnected
├── Tables.cs            ← Player, InventoryItem, WorldItem, WorldObject,
│                           PlacedStructure, CraftingRecipe, ChatMessage, TerrainConfig
├── Seeding.cs           ← SeedRecipes(), SeedWorldObjects(), SeedWorldItems(),
│                           SeedTerrainConfig(); TerrainHeightAt() utility moved here
├── StructureConfig.cs   ← static registry: structure_type → max health + is-placeable flag
├── HarvestConfig.cs     ← static registry: tool damage tables, drop tables
├── InventoryHelpers.cs  ← moved from engine; ParseIngredients() pure helper
├── PlayerReducers.cs
├── InventoryReducers.cs
├── CraftingReducers.cs
├── BuildingReducers.cs  ← uses StructureConfig instead of switch
└── WorldReducers.cs     ← uses HarvestConfig instead of switch
```

`BaseMod.OnClientConnected` gives starter items to new players.
`BaseMod.OnClientDisconnected` sets `IsOnline = false` on the player row (preserving existing behaviour).
`TerrainHeightAt()` moves from `Lifecycle.cs` (where it was private static) into `Seeding.cs` as an internal utility.

### `StdbModule.csproj` Change

Add a compile include for the base mod alongside the existing hello-world entry:
```xml
<Compile Include="../mods/base/server/**/*.cs" />
```

### Note: Static Registries vs SpacetimeDB Tables

`StructureConfig` and `HarvestConfig` are **plain static C# dictionaries**, not SpacetimeDB tables. They are populated during `Init → ModLoader.RunAll → BaseMod.Seed()` and exist only in server memory. They cannot be queried by clients and are not persisted. They are re-populated on every module publish, which is consistent with the in-memory server model.

---

## 4. Client Split

### Engine stays in `client/scripts/`

| Path | Notes |
|---|---|
| `networking/GameManager.cs` | Unchanged |
| `networking/SpacetimeDB/` | Unchanged (generated) |
| `networking/SceneRouter.cs` | Unchanged |
| `mods/IClientMod.cs` | Updated — add `string[] Dependencies` property |
| `mods/ModManager.cs` | Updated — topological sort on `Dependencies` before `InitializeAll` |
| `ui/UIManager.cs` | Unchanged (scene management infrastructure) |
| `networking/PlayerPrefs.cs` | **Stays in engine** — local ConfigFile persistence (name, colour, settings); referenced by UI screens |
| `world/ModelRegistry.cs` | **Stays in engine** — pure infrastructure (PackedScene caching, material helpers) |
| `world/Terrain.cs` | **Stays in engine** — world generation math (`HeightAt(x, z)`) used by base mod spawners |
| `ui/BasePanel.cs` | **Stays in engine** — abstract base class for all modal panels |
| `ui/CharacterSetup.cs` | **Stays in engine** — character creation screen (session/login flow, not game content) |
| `ui/EscapeMenu.cs` | **Stays in engine** — in-game pause menu (session infrastructure) |
| `ui/MainMenu.cs` | **Stays in engine** — main menu entry point |
| `ui/SettingsUI.cs` | **Stays in engine** — settings panel |
| `ui/UIFactory.cs` | **Stays in engine** — static UI control factory |

`WorldManager.cs` is **deleted** from the engine. Its responsibilities move into `BaseClientMod.Initialize()`.

**`ModManager.InitializeAll` call-site:** With `WorldManager` deleted, `ModManager` connects to `GameManager.Instance.SubscriptionApplied` in its own `_Ready()` and calls `InitializeAll(GetTree().Root)` when that signal fires. No scene node or mod calls `InitializeAll` directly — `ModManager` bootstraps itself.

**Namespaces stay flat** — no `SandboxRPG.Base` namespace is introduced. All files continue to use the same global namespace, so `UIManager` can reference base mod types without changes.

### Updated `IClientMod` Interface

```csharp
public interface IClientMod {
    string ModName { get; }
    string[] Dependencies { get; }            // new — enables ordering in ModManager
    void Initialize(Node sceneRoot);
}
```

`ModManager.InitializeAll` performs topological sort by `Dependencies` before calling `Initialize` on each mod, matching the server-side pattern. This guarantees `BaseClientMod.Initialize` (which populates the registries) always runs before dependent mods that call `ItemRegistry.Register(...)`.

### Base Mod Client (`client/mods/base/`)

```
client/mods/base/
├── BaseClientMod.cs         ← Godot autoload; implements IClientMod (Dependencies = [])
│                               wires GameManager signals, sets up spawners and UI
├── registries/
│   ├── ItemRegistry.cs      ← itemType → ItemDef (loads .tres from content/items/)
│   ├── StructureRegistry.cs ← structureType → StructureDef (loads .tres from content/structures/)
│   └── ObjectRegistry.cs   ← objectType → ObjectDef (loads .tres from content/objects/)
├── content/
│   ├── items/               ← *.tres files (one per item type: wood.tres, stone.tres, ...)
│   ├── structures/          ← *.tres files (wood_wall.tres, campfire.tres, ...)
│   └── objects/             ← *.tres files (tree_pine.tres, rock_large.tres, ...)
├── spawners/
│   ├── WorldItemSpawner.cs  ← uses ItemRegistry (no switch statements)
│   ├── StructureSpawner.cs  ← uses StructureRegistry
│   ├── WorldObjectSpawner.cs← uses ObjectRegistry
│   └── PlayerSpawner.cs
├── world/
│   ├── WorldManager.cs      ← moved from engine; wires GameManager → spawners
│   └── InteractionSystem.cs ← moved from engine (references BuildSystem + Hotbar)
├── player/
│   ├── PlayerController.cs  ← moved from engine (local player movement + sync)
│   └── RemotePlayer.cs      ← moved from engine (remote player interpolation)
├── building/
│   └── BuildSystem.cs       ← moved from engine; reads StructureRegistry for placeable types
└── ui/
    ├── HUD.cs
    ├── InventoryCraftingPanel.cs  ← kept as combined panel (no split; name unchanged)
    ├── ChatUI.cs
    └── Hotbar.cs
```

---

## 5. Content Registries

### `ContentDef` — Shared Base Class

All content definition classes extend `Godot.Resource`. This means they can be saved as `.tres` files and edited directly in the Godot inspector — offsets, collision sizes, model paths, and tints are all adjustable visually without touching code.

```csharp
[GlobalClass]
public partial class ContentDef : Resource {
    [Export] public string ModelPath { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    [Export] public float Scale { get; set; } = 1.0f;
    [Export] public Color TintColor { get; set; } = Colors.White;
    /// <summary>
    /// Optional path to a .tscn scene (the "prefab"). If set, the spawner instantiates
    /// this scene directly — allowing full visual editing of mesh, hitbox, and offsets
    /// in the Godot editor. If empty, the spawner generates visuals from ModelPath.
    /// </summary>
    [Export] public string ScenePath { get; set; } = "";
}
```

### Subclasses

**`ItemDef : ContentDef`** — for items in inventory and on the ground:
```csharp
[GlobalClass]
public partial class ItemDef : ContentDef {
    [Export] public int MaxStack { get; set; } = 64;
}
```

**`StructureDef : ContentDef`** — for player-placed structures:
```csharp
[GlobalClass]
public partial class StructureDef : ContentDef {
    [Export] public Vector3 CollisionSize { get; set; } = Vector3.One;
    [Export] public float YOffset { get; set; } = 0f;
    [Export] public bool IsPlaceable { get; set; } = true;
    // IsPlaceable = true → shown in BuildSystem's build menu
}
```

**`ObjectDef : ContentDef`** — for harvestable world objects (trees, rocks):
```csharp
[GlobalClass]
public partial class ObjectDef : ContentDef {
    [Export] public bool UseConvexCollision { get; set; } = true;
}
```

### Registry Loading

Each registry scans its content folder at startup and loads all `.tres` files:
```csharp
// ItemRegistry.Initialize() — called from BaseClientMod.Initialize()
foreach (var path in DirAccess.GetFilesAt("res://mods/base/content/items/"))
    Register(Path.GetFileNameWithoutExtension(path),
             GD.Load<ItemDef>("res://mods/base/content/items/" + path));
```

Spawners call `ItemRegistry.Get(itemType)` — returns `null` if not found, spawner falls back to coloured box mesh (preserving existing behaviour).

### Spawner Dispatch: `ScenePath` vs `ModelPath`

Every spawner follows this priority rule:

```csharp
Node3D SpawnVisual(ContentDef def) {
    if (!string.IsNullOrEmpty(def.ScenePath))
        return GD.Load<PackedScene>(def.ScenePath).Instantiate<Node3D>();
        // Scene contains its own mesh + CollisionShape3D — no further setup needed
    if (!string.IsNullOrEmpty(def.ModelPath))
        return ModelRegistry.CreateFromPath(def.ModelPath, def.Scale, def.TintColor);
        // Engine generates collision from def properties (e.g. CollisionSize on StructureDef)
    return CreateFallbackMesh(def.TintColor); // coloured box
}
```

`ScenePath` takes precedence. If it is set, the scene is responsible for its own mesh, collision, and offsets (the "prefab" model). `ModelPath` is the lightweight alternative — the spawner generates collision programmatically from the def's properties.

### `LoadFolder()` API

`LoadFolder(string folderPath)` is a static helper on each registry that scans a folder, loads every `.tres` file, and registers it keyed by filename (without extension):

```csharp
public static void LoadFolder(string folderPath) {
    foreach (var file in DirAccess.GetFilesAt(folderPath).Where(f => f.EndsWith(".tres"))) {
        var key = Path.GetFileNameWithoutExtension(file);
        var def = GD.Load<ItemDef>(folderPath.TrimEnd('/') + "/" + file);
        if (def is not null) Register(key, def);
        // Missing or wrong type: silently skipped (fallback mesh applies)
    }
}
```

Dependent mods load their own content folders in their `Initialize()`:
```csharp
ItemRegistry.LoadFolder("res://mods/farming/content/items/");
```

### `IsBuildable` Contract

Registration in `StructureRegistry` with `IsPlaceable = true` marks a structure as player-buildable. `BuildSystem` iterates `StructureRegistry.All()` filtering by `IsPlaceable`. `InteractionSystem.IsBuildable(type)` checks `StructureRegistry.Get(type)?.IsPlaceable ?? false`.

### Server-Side Registries

**`StructureConfig`** — maps structure type → max health:
```csharp
StructureConfig.Register("wood_wall", maxHealth: 200);
```

**`HarvestConfig`** — tool damage and drop tables:
```csharp
HarvestConfig.RegisterToolDamage("wood_pickaxe", "rock_large", damage: 10);
HarvestConfig.RegisterDrop("tree_pine", "wood", quantity: 3);
```

These replace all hard-coded switch statements in `BuildingReducers` and `WorldReducers`.

---

## 6. Lifecycle Hooks

### Server

`ModLoader` gains two forwarding methods called in dependency order:

```csharp
ModLoader.ForwardClientConnected(ctx, identity);    // → each IMod.OnClientConnected
ModLoader.ForwardClientDisconnected(ctx, identity); // → each IMod.OnClientDisconnected
```

Base mod uses these hooks:
```csharp
// BaseMod.cs
public void OnClientConnected(ReducerContext ctx, Identity identity) {
    ctx.Db.InventoryItem.Insert(new InventoryItem { Owner = identity, ItemType = "wood_pickaxe", Slot = 0, Quantity = 1 });
    ctx.Db.InventoryItem.Insert(new InventoryItem { Owner = identity, ItemType = "wood_axe",     Slot = 1, Quantity = 1 });
}

public void OnClientDisconnected(ReducerContext ctx, Identity identity) {
    var player = ctx.Db.Player.Identity.Find(identity);
    if (player is not null) {
        player.IsOnline = false;
        ctx.Db.Player.UpdateByIdentity(identity, player);
    }
}
```

### Client

`ModManager.InitializeAll` topologically sorts mods by `Dependencies` before calling `Initialize`. `BaseClientMod` declares no dependencies and always initialises first, populating registries before any dependent mod's `Initialize` runs.

---

## 7. How Dependent Mods Use Base

A mod that adds a `copper_vein` world object and `copper_ore` item touches only its own files:

**Server** (`mods/farming/server/FarmingMod.cs`):
```csharp
public class FarmingMod : IMod {
    public string[] Dependencies => new[] { "base" };

    public void Seed(ReducerContext ctx) {
        StructureConfig.Register("garden_bed", maxHealth: 150);
        HarvestConfig.RegisterDrop("copper_vein", "copper_ore", quantity: 3);
        HarvestConfig.RegisterToolDamage("iron_pickaxe", "copper_vein", damage: 25);
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe { /* ... */ });
    }
}
```

**Client** (`client/mods/farming/FarmingClientMod.cs`):
```csharp
public partial class FarmingClientMod : Node, IClientMod {
    public string ModName => "farming";
    public string[] Dependencies => new[] { "base" };

    public void Initialize(Node sceneRoot) {
        ItemRegistry.LoadFolder("res://mods/farming/content/items/");
        ObjectRegistry.LoadFolder("res://mods/farming/content/objects/");
    }
}
```

Content defs live in `mods/farming/content/items/copper_ore.tres` — editable in the Godot inspector.

No engine files touched. No base mod files touched.

---

## 8. What Does Not Change

- SpacetimeDB compilation model (single WASM binary, compile-time mod inclusion)
- `IMod.Dependencies` string array + topological sort — same mechanism, extended to client side
- Fallback coloured box meshes in spawners
- All public-facing game behaviour
- `InventoryCraftingPanel.cs` — kept as a combined panel, not split
- The hello-world mod (already follows the pattern; add `Dependencies = []` to its `IClientMod`)
- Generated bindings in `client/scripts/networking/SpacetimeDB/`
- `UIManager.cs` (namespaces stay flat; no changes required)

---

## 9. Out of Scope

- Dynamic/hot-loadable mods at runtime (not possible with SpacetimeDB WASM)
- Any new gameplay features
- Splitting `InventoryCraftingPanel` into separate panels
