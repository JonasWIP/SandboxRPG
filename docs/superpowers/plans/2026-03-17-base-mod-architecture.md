# Base Mod Architecture — Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reorganise all game content (tables, reducers, spawners, UI) into `mods/base/`, leaving the engine as a zero-content infrastructure layer.

**Architecture:** Engine provides `ModLoader`/`IMod`/`IClientMod` and lifecycle forwarding only. Base mod is a compile-time inclusion (added to `StdbModule.csproj` and Godot project) that defines all SpacetimeDB tables, C# reducers, content registries, spawners, and game UI. Other mods declare `"base"` as a dependency and call `ItemRegistry.Register(...)` / `StructureConfig.Register(...)` to extend content.

**Tech Stack:** SpacetimeDB 2.0 C# WASM server (`net8.0/wasi-wasm`), Godot 4.6.1 C# client, `spacetime build` for server, `dotnet build SandboxRPG.csproj` for client.

**Spec:** `docs/superpowers/specs/2026-03-17-base-mod-architecture-design.md`

---

## Chunk 1: Server-Side Changes

### Task 1: Update `IMod` — add lifecycle hooks

**Files:**
- Modify: `server/mods/IMod.cs`

- [ ] **Step 1: Edit `IMod.cs`** — add two default-body hook methods

```csharp
// server/mods/IMod.cs
using SpacetimeDB;

namespace SandboxRPG.Server.Mods;

public interface IMod
{
    string Name { get; }
    string Version { get; }
    string[] Dependencies { get; }  // mod names that must seed before this one
    void Seed(ReducerContext ctx);
    void OnClientConnected(ReducerContext ctx, Identity identity) { }    // default: no-op
    void OnClientDisconnected(ReducerContext ctx, Identity identity) { } // default: no-op
}
```

- [ ] **Step 2: Verify server compiles**

```bash
cd server && dotnet build
```
Expected: 0 errors (existing mods don't implement the new methods — default impls cover them).

- [ ] **Step 3: Commit**

```bash
git add server/mods/IMod.cs
git commit -m "feat(server): add OnClientConnected/OnClientDisconnected hooks to IMod"
```

---

### Task 2: Update `ModLoader` — add forwarding methods

**Files:**
- Modify: `server/mods/ModLoader.cs`

- [ ] **Step 1: Edit `ModLoader.cs`** — cache sorted list, add two forwarding methods

```csharp
// server/mods/ModLoader.cs
using SpacetimeDB;

namespace SandboxRPG.Server.Mods;

public static class ModLoader
{
    private static readonly List<IMod> _mods   = new();
    private static List<IMod>?         _sorted = null;  // cached after first sort

    public static void Register(IMod mod) => _mods.Add(mod);

    private static List<IMod> Sorted() =>
        _sorted ??= ModLoaderHelpers.TopoSort(_mods, m => m.Name, m => m.Dependencies);

    public static void RunAll(ReducerContext ctx)
    {
        foreach (var mod in Sorted())
        {
            Log.Info($"[ModLoader] Seeding mod: {mod.Name} v{mod.Version}");
            mod.Seed(ctx);
        }
    }

    public static void ForwardClientConnected(ReducerContext ctx, Identity identity)
    {
        foreach (var mod in Sorted())
            mod.OnClientConnected(ctx, identity);
    }

    public static void ForwardClientDisconnected(ReducerContext ctx, Identity identity)
    {
        foreach (var mod in Sorted())
            mod.OnClientDisconnected(ctx, identity);
    }
}
```

- [ ] **Step 2: Verify server compiles**

```bash
cd server && dotnet build
```

- [ ] **Step 3: Run existing server tests**

```bash
cd server && dotnet test SandboxRPG.Server.Tests
```
Expected: all passing (ModLoaderHelpers tests unchanged).

- [ ] **Step 4: Commit**

```bash
git add server/mods/ModLoader.cs
git commit -m "feat(server): add ForwardClientConnected/ForwardClientDisconnected to ModLoader"
```

---

### Task 3: Create `mods/base/server/` skeleton

**Files:**
- Create: `mods/base/server/StructureConfig.cs`
- Create: `mods/base/server/HarvestConfig.cs`
- Create: `mods/base/server/BaseMod.cs`

- [ ] **Step 1: Create `StructureConfig.cs`**

```csharp
// mods/base/server/StructureConfig.cs
using System.Collections.Generic;

namespace SandboxRPG.Server;

/// <summary>
/// Runtime registry populated by BaseMod.Seed (and any dependent mod's Seed).
/// Replaces the hard-coded switch in BuildingReducers.PlaceStructure.
/// This is a plain static dictionary — NOT a SpacetimeDB table. Repopulated on every Init.
/// </summary>
public static class StructureConfig
{
    private static readonly Dictionary<string, float> _maxHealth = new();

    public static void Register(string structureType, float maxHealth)
        => _maxHealth[structureType] = maxHealth;

    public static float GetMaxHealth(string structureType)
        => _maxHealth.TryGetValue(structureType, out var h) ? h : 100f;
}
```

- [ ] **Step 2: Create `HarvestConfig.cs`**

```csharp
// mods/base/server/HarvestConfig.cs
using System.Collections.Generic;

namespace SandboxRPG.Server;

/// <summary>
/// Runtime registry for tool damage and world object drop tables.
/// Replaces the hard-coded switches in WorldReducers.
/// Plain static dictionaries — NOT SpacetimeDB tables.
/// </summary>
public static class HarvestConfig
{
    private record struct DropEntry(string ItemType, uint Quantity);
    private record struct DamageKey(string Tool, string Object);

    private static readonly Dictionary<string, DropEntry> _drops  = new();
    private static readonly Dictionary<DamageKey, uint>   _damage = new();

    public static void RegisterDrop(string objectType, string itemType, uint quantity)
        => _drops[objectType] = new DropEntry(itemType, quantity);

    public static void RegisterToolDamage(string toolType, string objectType, uint damage)
        => _damage[new DamageKey(toolType, objectType)] = damage;

    public static (string ItemType, uint Quantity) GetDrop(string objectType)
        => _drops.TryGetValue(objectType, out var d) ? (d.ItemType, d.Quantity) : ("wood", 1u);

    /// <summary>Returns damage dealt; 5 is the bare-hands/unknown-tool default.</summary>
    public static uint GetToolDamage(string toolType, string objectType)
        => _damage.TryGetValue(new DamageKey(toolType, objectType), out var d) ? d : 5u;
}
```

- [ ] **Step 3: Create `BaseMod.cs`** — skeleton only (full implementation in Task 5)

```csharp
// mods/base/server/BaseMod.cs
using SpacetimeDB;
using System;
using System.Collections.Generic;
using SandboxRPG.Server.Mods;

namespace SandboxRPG.Server;

public static partial class Module
{
    // Registers BaseMod with the loader when the module class is initialised.
    private static readonly BaseModImpl _baseMod = new();

    private sealed class BaseModImpl : IMod
    {
        public BaseModImpl() => ModLoader.Register(this);

        public string   Name         => "base";
        public string   Version      => "1.0.0";
        public string[] Dependencies => Array.Empty<string>();

        public void Seed(ReducerContext ctx)
        {
            RegisterStructures();
            RegisterHarvest();
            SeedTerrainConfig(ctx);
            SeedRecipes(ctx);
            SeedWorldItems(ctx);
            SeedWorldObjects(ctx);
            Log.Info("[BaseMod] Seeded.");
        }

        public void OnClientConnected(ReducerContext ctx, Identity identity)
        {
            // Implemented in BaseMod — see bottom of this file.
            BaseModHandleClientConnected(ctx, identity);
        }

        public void OnClientDisconnected(ReducerContext ctx, Identity identity)
        {
            BaseModHandleClientDisconnected(ctx, identity);
        }
    }

    // ---- registry setup (populated during Seed) ----------------------------

    private static void RegisterStructures()
    {
        StructureConfig.Register("wood_wall",   100f);
        StructureConfig.Register("stone_wall",  250f);
        StructureConfig.Register("wood_floor",   80f);
        StructureConfig.Register("stone_floor", 200f);
        StructureConfig.Register("wood_door",    60f);
        StructureConfig.Register("campfire",     50f);
        StructureConfig.Register("workbench",   100f);
        StructureConfig.Register("chest",        80f);
    }

    private static void RegisterHarvest()
    {
        // Tool damage
        HarvestConfig.RegisterToolDamage("wood_axe",      "tree_pine",  34);
        HarvestConfig.RegisterToolDamage("wood_axe",      "tree_dead",  34);
        HarvestConfig.RegisterToolDamage("wood_axe",      "tree_palm",  34);
        HarvestConfig.RegisterToolDamage("wood_axe",      "bush",       34);
        HarvestConfig.RegisterToolDamage("wood_pickaxe",  "rock_large", 34);
        HarvestConfig.RegisterToolDamage("wood_pickaxe",  "rock_small", 34);
        HarvestConfig.RegisterToolDamage("stone_pickaxe", "rock_large", 50);
        HarvestConfig.RegisterToolDamage("stone_pickaxe", "rock_small", 50);
        HarvestConfig.RegisterToolDamage("iron_pickaxe",  "rock_large", 75);
        HarvestConfig.RegisterToolDamage("iron_pickaxe",  "rock_small", 75);

        // Off-primary damage — preserves original switch semantics exactly:
        // stone_pickaxe on trees = 8, iron_pickaxe on trees = 10, axe on rocks = 5
        HarvestConfig.RegisterToolDamage("wood_axe",      "rock_large", 5);
        HarvestConfig.RegisterToolDamage("wood_axe",      "rock_small", 5);
        HarvestConfig.RegisterToolDamage("stone_pickaxe", "tree_pine",  8);
        HarvestConfig.RegisterToolDamage("stone_pickaxe", "tree_dead",  8);
        HarvestConfig.RegisterToolDamage("stone_pickaxe", "tree_palm",  8);
        HarvestConfig.RegisterToolDamage("stone_pickaxe", "bush",       8);
        HarvestConfig.RegisterToolDamage("iron_pickaxe",  "tree_pine",  10);
        HarvestConfig.RegisterToolDamage("iron_pickaxe",  "tree_dead",  10);
        HarvestConfig.RegisterToolDamage("iron_pickaxe",  "tree_palm",  10);
        HarvestConfig.RegisterToolDamage("iron_pickaxe",  "bush",       10);

        // Drop tables
        HarvestConfig.RegisterDrop("rock_large", "stone", 3);
        HarvestConfig.RegisterDrop("rock_small", "stone", 1);
        HarvestConfig.RegisterDrop("tree_pine",  "wood",  4);
        HarvestConfig.RegisterDrop("tree_dead",  "wood",  2);
        HarvestConfig.RegisterDrop("tree_palm",  "wood",  1);
        HarvestConfig.RegisterDrop("bush",       "wood",  1);
    }

    // ---- lifecycle forwarded from IMod hooks --------------------------------

    private static void BaseModHandleClientConnected(ReducerContext ctx, Identity identity)
    {
        var existing = ctx.Db.Player.Identity.Find(identity);

        if (existing is not null)
        {
            var player = existing.Value;
            player.IsOnline = true;
            ctx.Db.Player.Identity.Update(player);
            Log.Info($"Player '{player.Name}' reconnected.");
        }
        else
        {
            ctx.Db.Player.Insert(new Player
            {
                Identity    = identity,
                Name        = $"Player_{identity.ToString()[..8]}",
                PosX        = 0f,
                PosY        = 0.3f,
                PosZ        = 1f,
                RotY        = (float)Math.PI,
                Health      = 100f,
                MaxHealth   = 100f,
                Stamina     = 100f,
                MaxStamina  = 100f,
                IsOnline    = true,
                ColorHex    = "#3CB4E5",
            });
            ctx.Db.InventoryItem.Insert(new InventoryItem { OwnerId = identity, ItemType = "wood_pickaxe", Quantity = 1, Slot = 0 });
            ctx.Db.InventoryItem.Insert(new InventoryItem { OwnerId = identity, ItemType = "wood_axe",     Quantity = 1, Slot = 1 });
            Log.Info($"[BaseMod] New player created: {identity}");
        }
    }

    private static void BaseModHandleClientDisconnected(ReducerContext ctx, Identity identity)
    {
        var existing = ctx.Db.Player.Identity.Find(identity);
        if (existing is not null)
        {
            var player = existing.Value;
            player.IsOnline = false;
            ctx.Db.Player.Identity.Update(player);
            Log.Info($"[BaseMod] Player '{player.Name}' disconnected.");
        }
    }
}
```

*Note: `SeedTerrainConfig`, `SeedRecipes`, `SeedWorldItems`, `SeedWorldObjects`, and `TerrainHeightAt` will be moved from `Lifecycle.cs` into `mods/base/server/Seeding.cs` in Task 4. Until that task is complete, this file will have unresolved method references — do not compile between Task 3 and Task 4.*

- [ ] **Step 4: Update `StdbModule.csproj`** — add base mod compile include

In `server/StdbModule.csproj`, add inside the mods `<ItemGroup>`:
```xml
<Compile Include="../mods/base/server/**/*.cs" />
```

Result:
```xml
<!-- Mods: add one line per enabled mod -->
<ItemGroup>
  <Compile Include="../mods/base/server/**/*.cs" />
  <Compile Include="../mods/hello-world/server/**/*.cs" Exclude="../mods/hello-world/server/tests/**/*.cs" />
</ItemGroup>
```

*Do NOT compile yet — `BaseMod.cs` references methods not yet moved from `Lifecycle.cs`.*

---

### Task 4: Move server content files to base mod

This task moves all content files atomically (copy to new location + delete from old in one commit).

**Files:**
- Create: `mods/base/server/Tables.cs` (moved from `server/Tables.cs`)
- Create: `mods/base/server/Seeding.cs` (extracted from `server/Lifecycle.cs`)
- Create: `mods/base/server/InventoryHelpers.cs` (moved from `server/InventoryHelpers.cs`)
- Create: `mods/base/server/PlayerReducers.cs` (moved from `server/PlayerReducers.cs`)
- Create: `mods/base/server/InventoryReducers.cs` (moved from `server/InventoryReducers.cs`)
- Create: `mods/base/server/CraftingReducers.cs` (moved from `server/CraftingReducers.cs`)
- Create: `mods/base/server/BuildingReducers.cs` (moved from `server/BuildingReducers.cs` — updated to use `StructureConfig`)
- Create: `mods/base/server/WorldReducers.cs` (moved from `server/WorldReducers.cs` — updated to use `HarvestConfig`)
- Delete: `server/Tables.cs`
- Delete: `server/InventoryHelpers.cs`
- Delete: `server/PlayerReducers.cs`
- Delete: `server/InventoryReducers.cs`
- Delete: `server/CraftingReducers.cs`
- Delete: `server/BuildingReducers.cs`
- Delete: `server/WorldReducers.cs`
- Modify: `server/Lifecycle.cs` — strip to thin dispatcher

- [ ] **Step 1: Copy `Tables.cs`** — contents identical, just moved

```bash
cp server/Tables.cs mods/base/server/Tables.cs
```

- [ ] **Step 2: Create `mods/base/server/Seeding.cs`** — extract from `Lifecycle.cs`

```csharp
// mods/base/server/Seeding.cs
using SpacetimeDB;
using System;

namespace SandboxRPG.Server;

public static partial class Module
{
    // Terrain constants — must match client Terrain.cs
    private const uint  TerrainSeed      = 42;
    private const float TerrainNoiseScale = 0.04f;
    private const float TerrainNoiseAmp   = 1.2f;
    private const float TerrainWorldSize  = 500f;

    internal static void SeedTerrainConfig(ReducerContext ctx)
    {
        ctx.Db.TerrainConfig.Insert(new TerrainConfig
        {
            Id             = 0,
            Seed           = TerrainSeed,
            WorldSize      = TerrainWorldSize,
            NoiseScale     = TerrainNoiseScale,
            NoiseAmplitude = TerrainNoiseAmp,
        });
        Log.Info("Seeded terrain config.");
    }

    internal static void SeedRecipes(ReducerContext ctx)
    {
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe { ResultItemType = "wood_wall",     ResultQuantity = 1, Ingredients = "wood:4",         CraftTimeSeconds = 2f });
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe { ResultItemType = "stone_wall",    ResultQuantity = 1, Ingredients = "stone:6",        CraftTimeSeconds = 3f });
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe { ResultItemType = "wood_floor",    ResultQuantity = 1, Ingredients = "wood:3",         CraftTimeSeconds = 1.5f });
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe { ResultItemType = "wood_door",     ResultQuantity = 1, Ingredients = "wood:3,iron:1",  CraftTimeSeconds = 2f });
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe { ResultItemType = "campfire",      ResultQuantity = 1, Ingredients = "wood:5,stone:3", CraftTimeSeconds = 3f });
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe { ResultItemType = "workbench",     ResultQuantity = 1, Ingredients = "wood:8,stone:4", CraftTimeSeconds = 5f });
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe { ResultItemType = "chest",         ResultQuantity = 1, Ingredients = "wood:6,iron:2",  CraftTimeSeconds = 4f });
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe { ResultItemType = "stone_pickaxe", ResultQuantity = 1, Ingredients = "wood:2,stone:3", CraftTimeSeconds = 2f });
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe { ResultItemType = "iron_pickaxe",  ResultQuantity = 1, Ingredients = "wood:2,iron:3",  CraftTimeSeconds = 3f });
        Log.Info("Seeded crafting recipes.");
    }

    internal static void SeedWorldItems(ReducerContext ctx)
    {
        ctx.Db.WorldItem.Insert(new WorldItem { ItemType = "wood",  Quantity = 5, PosX =  3f, PosY = TerrainHeightAt( 3f,  3f) + 0.2f, PosZ =  3f });
        ctx.Db.WorldItem.Insert(new WorldItem { ItemType = "stone", Quantity = 3, PosX = -4f, PosY = TerrainHeightAt(-4f,  2f) + 0.2f, PosZ =  2f });
        ctx.Db.WorldItem.Insert(new WorldItem { ItemType = "wood",  Quantity = 8, PosX =  7f, PosY = TerrainHeightAt( 7f,  6f) + 0.2f, PosZ =  6f });
        ctx.Db.WorldItem.Insert(new WorldItem { ItemType = "iron",  Quantity = 2, PosX = -8f, PosY = TerrainHeightAt(-8f,  4f) + 0.2f, PosZ =  4f });
        ctx.Db.WorldItem.Insert(new WorldItem { ItemType = "stone", Quantity = 5, PosX = 10f, PosY = TerrainHeightAt(10f,  8f) + 0.2f, PosZ =  8f });
        Log.Info("Seeded starter world items.");
    }

    internal static void SeedWorldObjects(ReducerContext ctx)
    {
        var rng = new Random(42);

        WorldObject MakeObject(string type, float xMin, float xMax, float zMin, float zMax, uint hp)
        {
            float x = (float)(rng.NextDouble() * (xMax - xMin) + xMin);
            float z = (float)(rng.NextDouble() * (zMax - zMin) + zMin);
            return new WorldObject
            {
                ObjectType = type,
                PosX = x, PosY = TerrainHeightAt(x, z), PosZ = z,
                RotY = (float)(rng.NextDouble() * Math.PI * 2),
                Health = hp, MaxHealth = hp,
            };
        }

        for (int i = 0; i < 250; i++) ctx.Db.WorldObject.Insert(MakeObject("tree_pine",  -200f, 200f,  30f, 230f, 100));
        for (int i = 0; i < 60;  i++) ctx.Db.WorldObject.Insert(MakeObject("tree_dead",  -150f, 150f,  20f,  60f,  60));
        for (int i = 0; i < 90;  i++) ctx.Db.WorldObject.Insert(MakeObject("rock_large", -200f, 200f,   0f, 150f, 150));
        for (int i = 0; i < 75;  i++) ctx.Db.WorldObject.Insert(MakeObject("rock_small", -220f, 220f, -20f, 170f,  80));
        for (int i = 0; i < 50;  i++) ctx.Db.WorldObject.Insert(MakeObject("bush",       -100f, 100f,   5f,  50f,  30));
        Log.Info("Seeded world objects.");
    }

    /// <summary>Mirrors client Terrain.HeightAt — must stay in sync with terrain constants above.</summary>
    internal static float TerrainHeightAt(float x, float z)
    {
        if (z < 0f) return (float)Math.Max(z * 0.15, -3.0);
        double t     = Math.Clamp((z - 5.0)  / 30.0, 0.0, 1.0);
        double baseH = t * t * (3.0 - 2.0 * t) * 2.0;
        double nr    = Math.Clamp((z - 8.0)  / 20.0, 0.0, 1.0);
        double s     = TerrainSeed * 0.001;
        double noise = Math.Sin(x * TerrainNoiseScale + s) * Math.Cos(z * TerrainNoiseScale * 1.7 + s * 1.3) * TerrainNoiseAmp
                     + Math.Sin((x + z) * TerrainNoiseScale * 2.9 + s * 0.7) * TerrainNoiseAmp * 0.3;
        return (float)(baseH + noise * nr);
    }
}
```

- [ ] **Step 3: Copy `InventoryHelpers.cs`** to base mod (contents unchanged)

```bash
cp server/InventoryHelpers.cs mods/base/server/InventoryHelpers.cs
```

- [ ] **Step 4: Copy `PlayerReducers.cs`, `CraftingReducers.cs` to base mod** (contents unchanged)

```bash
cp server/PlayerReducers.cs   mods/base/server/PlayerReducers.cs
cp server/CraftingReducers.cs mods/base/server/CraftingReducers.cs
cp server/InventoryReducers.cs mods/base/server/InventoryReducers.cs
```

- [ ] **Step 5: Create `mods/base/server/BuildingReducers.cs`** — updated to use `StructureConfig`

Replace the `maxHealth` switch statement with a registry lookup:

```csharp
// mods/base/server/BuildingReducers.cs
using System;
using SpacetimeDB;

namespace SandboxRPG.Server;

public static partial class Module
{
    [Reducer]
    public static void PlaceStructure(ReducerContext ctx, string structureType, float posX, float posY, float posZ, float rotY)
    {
        var identity = ctx.Sender;

        InventoryItem? foundItem = null;
        foreach (var inv in ctx.Db.InventoryItem.Iter())
        {
            if (inv.OwnerId == identity && inv.ItemType == structureType)
            { foundItem = inv; break; }
        }
        if (foundItem is null)
            throw new Exception($"You don't have a {structureType} to place.");

        float maxHealth = StructureConfig.GetMaxHealth(structureType); // ← replaces switch

        ctx.Db.PlacedStructure.Insert(new PlacedStructure
        {
            OwnerId = identity, StructureType = structureType,
            PosX = posX, PosY = posY, PosZ = posZ, RotY = rotY,
            Health = maxHealth, MaxHealth = maxHealth,
        });

        var item = foundItem.Value;
        if (item.Quantity <= 1)
            ctx.Db.InventoryItem.Delete(item);
        else
        {
            item.Quantity -= 1;
            ctx.Db.InventoryItem.Id.Update(item);
        }

        Log.Info($"Player placed {structureType} at ({posX:F1}, {posY:F1}, {posZ:F1})");
    }

    [Reducer]
    public static void RemoveStructure(ReducerContext ctx, ulong structureId)
    {
        var structure = ctx.Db.PlacedStructure.Id.Find(structureId);
        if (structure is null) return;

        var s = structure.Value;
        if (s.OwnerId != ctx.Sender)
            throw new Exception("You can only remove your own structures.");

        ctx.Db.InventoryItem.Insert(new InventoryItem
        {
            OwnerId = ctx.Sender, ItemType = s.StructureType, Quantity = 1, Slot = -1,
        });
        ctx.Db.PlacedStructure.Delete(s);
    }
}
```

- [ ] **Step 6: Create `mods/base/server/WorldReducers.cs`** — updated to use `HarvestConfig`

Replace the three private switch methods with registry lookups:

```csharp
// mods/base/server/WorldReducers.cs
using SpacetimeDB;

namespace SandboxRPG.Server;

public static partial class Module
{
    [Reducer]
    public static void HarvestWorldObject(ReducerContext ctx, ulong id, string toolType)
    {
        var obj = ctx.Db.WorldObject.Id.Find(id);
        if (obj is null) return;
        var o = obj.Value;

        if (!string.IsNullOrEmpty(toolType))
        {
            bool found = false;
            foreach (var item in ctx.Db.InventoryItem.Iter())
            {
                if (item.OwnerId == ctx.Sender && item.ItemType == toolType)
                { found = true; break; }
            }
            if (!found) return;
        }

        uint damage    = HarvestConfig.GetToolDamage(toolType, o.ObjectType); // ← replaces switch
        uint newHealth = o.Health <= damage ? 0 : o.Health - damage;

        ctx.Db.WorldObject.Delete(o);

        if (newHealth == 0)
        {
            var (dropType, dropQty) = HarvestConfig.GetDrop(o.ObjectType); // ← replaces switches
            ctx.Db.WorldItem.Insert(new WorldItem
            {
                ItemType = dropType, Quantity = dropQty,
                PosX = o.PosX, PosY = o.PosY, PosZ = o.PosZ,
            });
        }
        else
        {
            ctx.Db.WorldObject.Insert(new WorldObject
            {
                ObjectType = o.ObjectType,
                PosX = o.PosX, PosY = o.PosY, PosZ = o.PosZ,
                RotY = o.RotY, Health = newHealth, MaxHealth = o.MaxHealth,
            });
        }
    }
}
```

- [ ] **Step 7: Strip `server/Lifecycle.cs`** — thin dispatcher only

Replace the entire file with:

```csharp
// server/Lifecycle.cs
using SpacetimeDB;
using SandboxRPG.Server.Mods;

namespace SandboxRPG.Server;

public static partial class Module
{
    [Reducer(ReducerKind.Init)]
    public static void Init(ReducerContext ctx)
    {
        Log.Info("SandboxRPG server module initialized!");
        ModLoader.RunAll(ctx);
    }

    [Reducer(ReducerKind.ClientConnected)]
    public static void ClientConnected(ReducerContext ctx) =>
        ModLoader.ForwardClientConnected(ctx, ctx.Sender);

    [Reducer(ReducerKind.ClientDisconnected)]
    public static void ClientDisconnected(ReducerContext ctx) =>
        ModLoader.ForwardClientDisconnected(ctx, ctx.Sender);
}
```

- [ ] **Step 8: Delete old engine content files**

```bash
git rm server/Tables.cs
git rm server/InventoryHelpers.cs
git rm server/PlayerReducers.cs
git rm server/InventoryReducers.cs
git rm server/CraftingReducers.cs
git rm server/BuildingReducers.cs
git rm server/WorldReducers.cs
```

- [ ] **Step 9: Verify server builds**

```bash
cd server && dotnet build
```
Expected: 0 errors. All types still in `SandboxRPG.Server` namespace; partial class `Module` spans both engine and base mod files — C# compiles them together.

Also verify WASM build:
```bash
cd server && spacetime build
```

- [ ] **Step 10: Run server tests**

```bash
cd server && dotnet test SandboxRPG.Server.Tests
```
Expected: all passing.

- [ ] **Step 11: Commit**

```bash
git add mods/base/server/
git add server/Lifecycle.cs server/StdbModule.csproj
git commit -m "refactor(server): extract all game content into mods/base/server"
```

---

## Chunk 2: Client-Side Infrastructure

### Task 5: Update `IClientMod` — add `Dependencies`

**Files:**
- Modify: `client/scripts/mods/IClientMod.cs`

- [ ] **Step 1: Edit `IClientMod.cs`**

```csharp
// client/scripts/mods/IClientMod.cs
using Godot;

namespace SandboxRPG;

public interface IClientMod
{
    string   ModName      { get; }
    string[] Dependencies { get; }  // mod names that must Initialize before this one
    void     Initialize(Node sceneRoot);
}
```

- [ ] **Step 2: Update `HelloWorldClientMod.cs`** — add `Dependencies` property

In `client/mods/hello-world/HelloWorldClientMod.cs`, add:
```csharp
public string[] Dependencies => System.Array.Empty<string>();
```

- [ ] **Step 3: Verify client compiles**

```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: 0 errors (HelloWorldClientMod now satisfies the interface).

- [ ] **Step 4: Commit**

```bash
git add client/scripts/mods/IClientMod.cs client/mods/hello-world/HelloWorldClientMod.cs
git commit -m "feat(client): add Dependencies to IClientMod interface"
```

---

### Task 6: Update `ModManager` — topological sort

**Files:**
- Modify: `client/scripts/mods/ModManager.cs`

- [ ] **Step 1: Edit `ModManager.cs`** — add topological sort before initialising

```csharp
// client/scripts/mods/ModManager.cs
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SandboxRPG;

/// <summary>
/// Autoload singleton. Client mods call ModManager.Register(this) from their _Ready.
/// WorldManager._Ready() calls InitializeAll(this) once the game scene is ready.
/// Mods are initialised in dependency order (topological sort on Dependencies).
/// </summary>
public partial class ModManager : Node
{
    public static ModManager Instance { get; private set; } = null!;
    private static readonly List<IClientMod> _pending = new();

    public static void Register(IClientMod mod) => _pending.Add(mod);

    public override void _Ready() => Instance = this;

    private bool _initialized;

    public void InitializeAll(Node sceneRoot)
    {
        if (_initialized) return;
        _initialized = true;

        var sorted = TopoSort(_pending);
        foreach (var mod in sorted)
        {
            GD.Print($"[ModManager] Initializing: {mod.ModName}");
            mod.Initialize(sceneRoot);
        }
    }

    private static List<IClientMod> TopoSort(List<IClientMod> mods)
    {
        var byName   = mods.ToDictionary(m => m.ModName);
        var inDegree = mods.ToDictionary(m => m.ModName, _ => 0);

        foreach (var mod in mods)
            foreach (var dep in mod.Dependencies)
                if (byName.ContainsKey(dep))
                    inDegree[mod.ModName]++;

        var queue  = new Queue<IClientMod>(mods.Where(m => inDegree[m.ModName] == 0));
        var result = new List<IClientMod>();

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            result.Add(current);
            foreach (var dependent in mods.Where(m => m.Dependencies.Contains(current.ModName)))
            {
                inDegree[dependent.ModName]--;
                if (inDegree[dependent.ModName] == 0)
                    queue.Enqueue(dependent);
            }
        }

        if (result.Count != mods.Count)
            throw new InvalidOperationException("[ModManager] Circular dependency detected in client mods.");

        return result;
    }
}
```

- [ ] **Step 2: Verify client compiles**

```bash
cd client && dotnet build SandboxRPG.csproj
```

*Note: The spec describes ModManager self-bootstrapping via `SubscriptionApplied`. This plan intentionally keeps the existing call-site: `WorldManager._Ready()` (now in `client/mods/base/world/`) still calls `ModManager.Instance.InitializeAll(this)`. This avoids timing complexity and preserves the existing `sceneRoot` reference. The spec description is superseded by this plan.*

- [ ] **Step 3: Commit**

```bash
git add client/scripts/mods/ModManager.cs
git commit -m "feat(client): topological sort in ModManager.InitializeAll"
```

---

### Task 7: Create content definition classes and registries

**Files:**
- Create: `client/mods/base/registries/ContentDef.cs`
- Create: `client/mods/base/registries/ItemRegistry.cs`
- Create: `client/mods/base/registries/StructureRegistry.cs`
- Create: `client/mods/base/registries/ObjectRegistry.cs`

- [ ] **Step 1: Create `ContentDef.cs`** — base and subclasses

```csharp
// client/mods/base/registries/ContentDef.cs
using Godot;

namespace SandboxRPG;

/// <summary>Base content definition — saved as .tres for Godot inspector editing.</summary>
[GlobalClass]
public partial class ContentDef : Resource
{
    /// <summary>Path to a .glb model. Used if ScenePath is empty.</summary>
    [Export] public string ModelPath  { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    [Export] public float  Scale      { get; set; } = 1.0f;
    [Export] public Color  TintColor  { get; set; } = Colors.White;
    /// <summary>
    /// Optional path to a .tscn "prefab" scene. When set, the spawner instantiates
    /// this scene directly — mesh, hitbox, and offsets are all editable in the
    /// Godot editor. Takes precedence over ModelPath.
    /// </summary>
    [Export] public string ScenePath  { get; set; } = "";
}

/// <summary>Definition for items (inventory and world drops).</summary>
[GlobalClass]
public partial class ItemDef : ContentDef
{
    [Export] public int MaxStack { get; set; } = 64;
}

/// <summary>Definition for player-placed structures.</summary>
[GlobalClass]
public partial class StructureDef : ContentDef
{
    [Export] public Vector3 CollisionSize   { get; set; } = Vector3.One;
    /// <summary>Local position of the CollisionShape3D within the body. Used to raise box shapes above ground.</summary>
    [Export] public Vector3 CollisionCenter { get; set; } = Vector3.Zero;
    [Export] public float   YOffset         { get; set; } = 0f;
    /// <summary>True → shown in BuildSystem's placement menu.</summary>
    [Export] public bool    IsPlaceable     { get; set; } = true;
}

/// <summary>Definition for harvestable world objects (trees, rocks).</summary>
[GlobalClass]
public partial class ObjectDef : ContentDef
{
    [Export] public bool UseConvexCollision { get; set; } = true;
}
```

- [ ] **Step 2: Create `ItemRegistry.cs`**

```csharp
// client/mods/base/registries/ItemRegistry.cs
using System.Collections.Generic;
using Godot;

namespace SandboxRPG;

public static class ItemRegistry
{
    private static readonly Dictionary<string, ItemDef> _defs = new();

    public static void     Register(string itemType, ItemDef def) => _defs[itemType] = def;
    public static ItemDef? Get(string itemType) => _defs.TryGetValue(itemType, out var d) ? d : null;

    /// <summary>
    /// Scans folderPath for *.tres files and registers each one keyed by filename
    /// (without extension). Silently skips files that fail to load or are wrong type.
    /// </summary>
    public static void LoadFolder(string folderPath)
    {
        var dir = DirAccess.Open(folderPath);
        if (dir is null) return;
        dir.ListDirBegin();
        string file;
        while ((file = dir.GetNext()) != "")
        {
            if (!file.EndsWith(".tres")) continue;
            var key = System.IO.Path.GetFileNameWithoutExtension(file);
            var def = ResourceLoader.Load<ItemDef>(folderPath.TrimEnd('/') + "/" + file);
            if (def is not null) Register(key, def);
        }
        dir.ListDirEnd();
    }
}
```

- [ ] **Step 3: Create `StructureRegistry.cs`**

```csharp
// client/mods/base/registries/StructureRegistry.cs
using System.Collections.Generic;
using Godot;

namespace SandboxRPG;

public static class StructureRegistry
{
    private static readonly Dictionary<string, StructureDef> _defs = new();

    public static void          Register(string type, StructureDef def) => _defs[type] = def;
    public static StructureDef? Get(string type) => _defs.TryGetValue(type, out var d) ? d : null;

    /// <summary>Returns all registered structure types where IsPlaceable = true.</summary>
    public static IEnumerable<(string Type, StructureDef Def)> AllPlaceable()
    {
        foreach (var kvp in _defs)
            if (kvp.Value.IsPlaceable)
                yield return (kvp.Key, kvp.Value);
    }

    public static void LoadFolder(string folderPath)
    {
        var dir = DirAccess.Open(folderPath);
        if (dir is null) return;
        dir.ListDirBegin();
        string file;
        while ((file = dir.GetNext()) != "")
        {
            if (!file.EndsWith(".tres")) continue;
            var key = System.IO.Path.GetFileNameWithoutExtension(file);
            var def = ResourceLoader.Load<StructureDef>(folderPath.TrimEnd('/') + "/" + file);
            if (def is not null) Register(key, def);
        }
        dir.ListDirEnd();
    }
}
```

- [ ] **Step 4: Create `ObjectRegistry.cs`**

```csharp
// client/mods/base/registries/ObjectRegistry.cs
using System.Collections.Generic;
using Godot;

namespace SandboxRPG;

public static class ObjectRegistry
{
    private static readonly Dictionary<string, ObjectDef> _defs = new();

    public static void       Register(string type, ObjectDef def) => _defs[type] = def;
    public static ObjectDef? Get(string type) => _defs.TryGetValue(type, out var d) ? d : null;

    public static void LoadFolder(string folderPath)
    {
        var dir = DirAccess.Open(folderPath);
        if (dir is null) return;
        dir.ListDirBegin();
        string file;
        while ((file = dir.GetNext()) != "")
        {
            if (!file.EndsWith(".tres")) continue;
            var key = System.IO.Path.GetFileNameWithoutExtension(file);
            var def = ResourceLoader.Load<ObjectDef>(folderPath.TrimEnd('/') + "/" + file);
            if (def is not null) Register(key, def);
        }
        dir.ListDirEnd();
    }
}
```

- [ ] **Step 5: Verify client compiles**

```bash
cd client && dotnet build SandboxRPG.csproj
```

- [ ] **Step 6: Commit**

```bash
git add client/mods/base/registries/
git commit -m "feat(client): add ContentDef hierarchy and ItemRegistry/StructureRegistry/ObjectRegistry"
```

---

## Chunk 3: Client Content Migration

### Task 8: Create `BaseClientMod` and `BaseContent`

**Files:**
- Create: `client/mods/base/BaseClientMod.cs`
- Create: `client/mods/base/content/BaseContent.cs`

- [ ] **Step 1: Create `BaseContent.cs`** — programmatic content registration (all existing model paths and tints preserved exactly)

```csharp
// client/mods/base/content/BaseContent.cs
using Godot;

namespace SandboxRPG;

/// <summary>
/// Registers all base-game content definitions into the registries.
/// Called from BaseClientMod.Initialize().
/// Add .tres files to mods/base/content/ subdirectories for visual editor editing;
/// until then, registration happens here in code.
/// </summary>
public static class BaseContent
{
    public static void RegisterAll()
    {
        RegisterItems();
        RegisterStructures();
        RegisterObjects();
    }

    private static void RegisterItems()
    {
        ItemRegistry.Register("wood",          new ItemDef { ModelPath = "res://assets/models/survival/resource-wood.glb",  DisplayName = "Wood",          MaxStack = 50 });
        ItemRegistry.Register("stone",         new ItemDef { ModelPath = "res://assets/models/survival/resource-stone.glb", DisplayName = "Stone",         MaxStack = 50 });
        ItemRegistry.Register("iron",          new ItemDef { DisplayName = "Iron",          TintColor = new Color(0.7f, 0.7f, 0.75f), MaxStack = 50 });
        ItemRegistry.Register("wood_pickaxe",  new ItemDef { DisplayName = "Wood Pickaxe",  MaxStack = 1 });
        ItemRegistry.Register("wood_axe",      new ItemDef { DisplayName = "Wood Axe",      MaxStack = 1 });
        ItemRegistry.Register("stone_pickaxe", new ItemDef { DisplayName = "Stone Pickaxe", MaxStack = 1 });
        ItemRegistry.Register("iron_pickaxe",  new ItemDef { DisplayName = "Iron Pickaxe",  MaxStack = 1 });
    }

    private static void RegisterStructures()
    {
        // Tints from original StructureSpawner.CreateStructureVisual
        var woodTint  = new Color(1.0f, 0.78f, 0.55f);
        var stoneTint = new Color(0.82f, 0.82f, 0.88f);

        // Collision sizes + centers from original GetBoxShape — must match exactly
        StructureRegistry.Register("wood_wall",   new StructureDef { ModelPath = "res://assets/models/building/wall.glb",               TintColor = woodTint,  CollisionSize = new Vector3(0.25f, 2.4f, 2.0f), CollisionCenter = new Vector3(0, 1.2f,  0), YOffset = 1.25f });
        StructureRegistry.Register("stone_wall",  new StructureDef { ModelPath = "res://assets/models/building/wall.glb",               TintColor = stoneTint, CollisionSize = new Vector3(0.25f, 2.4f, 2.0f), CollisionCenter = new Vector3(0, 1.2f,  0), YOffset = 1.25f });
        StructureRegistry.Register("wood_floor",  new StructureDef { ModelPath = "res://assets/models/building/floor.glb",              TintColor = woodTint,  CollisionSize = new Vector3(2.0f,  0.1f, 2.0f), CollisionCenter = new Vector3(0, 0.05f, 0), YOffset = 0.05f });
        StructureRegistry.Register("stone_floor", new StructureDef { ModelPath = "res://assets/models/building/floor.glb",              TintColor = stoneTint, CollisionSize = new Vector3(2.0f,  0.1f, 2.0f), CollisionCenter = new Vector3(0, 0.05f, 0), YOffset = 0.05f });
        StructureRegistry.Register("wood_door",   new StructureDef { ModelPath = "res://assets/models/building/wall-doorway-square.glb", TintColor = woodTint, CollisionSize = new Vector3(0.25f, 2.4f, 2.0f), CollisionCenter = new Vector3(0, 1.2f,  0), YOffset = 1.1f  });
        StructureRegistry.Register("campfire",    new StructureDef { ModelPath = "res://assets/models/survival/campfire-pit.glb",  CollisionSize = new Vector3(0.8f, 0.4f, 0.8f), CollisionCenter = new Vector3(0, 0.2f, 0), YOffset = 0.15f });
        StructureRegistry.Register("workbench",   new StructureDef { ModelPath = "res://assets/models/survival/workbench.glb",    CollisionSize = new Vector3(1.2f, 0.8f, 0.6f), CollisionCenter = new Vector3(0, 0.4f, 0), YOffset = 0.4f  });
        StructureRegistry.Register("chest",       new StructureDef { ModelPath = "res://assets/models/survival/chest.glb",        CollisionSize = new Vector3(0.8f, 0.6f, 0.6f), CollisionCenter = new Vector3(0, 0.3f, 0), YOffset = 0.3f  });
    }

    private static void RegisterObjects()
    {
        // Scales + tints match original WorldObjectSpawner exactly
        var rockTint = new Color(0.6f, 0.6f, 0.6f); // applied via ModelRegistry.ApplyMaterials
        ObjectRegistry.Register("tree_pine",  new ObjectDef { ModelPath = "res://assets/models/nature/tree_pineRoundA.glb",  Scale = 2.5f });
        ObjectRegistry.Register("tree_dead",  new ObjectDef { ModelPath = "res://assets/models/nature/tree_thin_dark.glb",   Scale = 2.0f }); // 2.0 not 2.5
        ObjectRegistry.Register("tree_palm",  new ObjectDef { ModelPath = "res://assets/models/nature/tree_palmTall.glb",    Scale = 2.5f });
        ObjectRegistry.Register("rock_large", new ObjectDef { ModelPath = "res://assets/models/nature/rock_largeA.glb",      Scale = 2.0f, TintColor = rockTint });
        ObjectRegistry.Register("rock_small", new ObjectDef { ModelPath = "res://assets/models/nature/rock_smallA.glb",      Scale = 1.8f, TintColor = rockTint }); // 1.8 not 2.0
        ObjectRegistry.Register("bush",       new ObjectDef { ModelPath = "res://assets/models/nature/plant_bush.glb",       Scale = 1.5f, UseConvexCollision = false });
    }
}
```

- [ ] **Step 2: Create `BaseClientMod.cs`** — Godot autoload

```csharp
// client/mods/base/BaseClientMod.cs
using Godot;
using System;

namespace SandboxRPG;

/// <summary>
/// Autoload mod — registers the base game content.
/// Depends on nothing; all other mods that use base registries declare Dependencies = ["base"].
/// Initialize() is called by ModManager (in dependency order) from WorldManager._Ready().
/// </summary>
public partial class BaseClientMod : Node, IClientMod
{
    public string   ModName      => "base";
    public string[] Dependencies => Array.Empty<string>();

    public override void _Ready() => ModManager.Register(this);

    public void Initialize(Node sceneRoot)
    {
        BaseContent.RegisterAll();
        GD.Print("[BaseClientMod] Content registered.");
    }
}
```

- [ ] **Step 3: Verify client compiles**

```bash
cd client && dotnet build SandboxRPG.csproj
```

- [ ] **Step 4: Commit**

```bash
git add client/mods/base/BaseClientMod.cs client/mods/base/content/BaseContent.cs
git commit -m "feat(client): add BaseClientMod and BaseContent registration"
```

---

### Task 9: Move client content files to base mod

This is the largest single step. Move all game-specific scripts from `client/scripts/` to `client/mods/base/`, updating spawners to use registries.

**Files moved (contents unchanged unless noted):**
- `client/scripts/world/WorldManager.cs`       → `client/mods/base/world/WorldManager.cs`
- `client/scripts/world/InteractionSystem.cs`  → `client/mods/base/world/InteractionSystem.cs`
- `client/scripts/player/PlayerController.cs`  → `client/mods/base/player/PlayerController.cs`
- `client/scripts/player/RemotePlayer.cs`      → `client/mods/base/player/RemotePlayer.cs`
- `client/scripts/building/BuildSystem.cs`     → `client/mods/base/building/BuildSystem.cs` (updated)
- `client/scripts/ui/HUD.cs`                   → `client/mods/base/ui/HUD.cs`
- `client/scripts/ui/InventoryCraftingPanel.cs`→ `client/mods/base/ui/InventoryCraftingPanel.cs`
- `client/scripts/ui/ChatUI.cs`                → `client/mods/base/ui/ChatUI.cs`
- `client/scripts/ui/Hotbar.cs`                → `client/mods/base/ui/Hotbar.cs`

**Files updated (contents changed to use registries):**
- `client/scripts/world/WorldItemSpawner.cs`   → `client/mods/base/spawners/WorldItemSpawner.cs`
- `client/scripts/world/StructureSpawner.cs`   → `client/mods/base/spawners/StructureSpawner.cs`
- `client/scripts/world/WorldObjectSpawner.cs` → `client/mods/base/spawners/WorldObjectSpawner.cs`
- `client/scripts/player/PlayerSpawner.cs`     → `client/mods/base/spawners/PlayerSpawner.cs`

- [ ] **Step 1: Move unchanged files**

```bash
# Create directories
mkdir -p client/mods/base/world
mkdir -p client/mods/base/player
mkdir -p client/mods/base/building
mkdir -p client/mods/base/ui
mkdir -p client/mods/base/spawners

# Move files
cp client/scripts/world/WorldManager.cs        client/mods/base/world/WorldManager.cs
cp client/scripts/world/InteractionSystem.cs   client/mods/base/world/InteractionSystem.cs
cp client/scripts/player/PlayerController.cs   client/mods/base/player/PlayerController.cs
cp client/scripts/player/RemotePlayer.cs       client/mods/base/player/RemotePlayer.cs
cp client/scripts/ui/HUD.cs                    client/mods/base/ui/HUD.cs
cp client/scripts/ui/InventoryCraftingPanel.cs client/mods/base/ui/InventoryCraftingPanel.cs
cp client/scripts/ui/ChatUI.cs                 client/mods/base/ui/ChatUI.cs
cp client/scripts/ui/Hotbar.cs                 client/mods/base/ui/Hotbar.cs
cp client/scripts/player/PlayerSpawner.cs      client/mods/base/spawners/PlayerSpawner.cs
```

- [ ] **Step 2: Create `client/mods/base/spawners/WorldItemSpawner.cs`** — replace switch with registry

Replace `CreateWorldItemVisual` and the `ModelPath` switch with:

```csharp
// client/mods/base/spawners/WorldItemSpawner.cs
using Godot;
using SpacetimeDB.Types;
using System.Collections.Generic;

namespace SandboxRPG;

public class WorldItemSpawner
{
    private readonly Node3D      _parent;
    private readonly GameManager _gm;
    private readonly Dictionary<ulong, Node3D> _worldItems = new();

    public WorldItemSpawner(Node3D parent, GameManager gm)
    {
        _parent = parent;
        _gm     = gm;
    }

    public void Sync()
    {
        var currentIds = new HashSet<ulong>();
        foreach (var item in _gm.GetAllWorldItems())
        {
            currentIds.Add(item.Id);
            if (!_worldItems.ContainsKey(item.Id))
            {
                var visual = CreateWorldItemVisual(item);
                _parent.AddChild(visual);
                _worldItems[item.Id] = visual;
            }
        }
        var toRemove = new List<ulong>();
        foreach (var kvp in _worldItems)
            if (!currentIds.Contains(kvp.Key))
            { kvp.Value.QueueFree(); toRemove.Add(kvp.Key); }
        foreach (var id in toRemove) _worldItems.Remove(id);
    }

    private static Node3D CreateWorldItemVisual(WorldItem item)
    {
        var body = new StaticBody3D { Name = $"WorldItem_{item.Id}", CollisionLayer = 2, CollisionMask = 0 };
        var visual = ContentSpawner.SpawnVisual(ItemRegistry.Get(item.ItemType), item.ItemType);
        visual.Position = new Vector3(0, 0.1f, 0); // lift slightly off ground (matches original)
        body.AddChild(visual);
        body.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = 0.2f } });
        body.AddChild(new Label3D
        {
            Text = $"{item.ItemType} x{item.Quantity}", FontSize = 32,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true,
            Position  = new Vector3(0, 0.5f, 0),
        });
        float groundY = Terrain.HeightAt(item.PosX, item.PosZ);
        body.Position = new Vector3(item.PosX, groundY + 0.1f, item.PosZ);
        body.SetMeta("world_item_id", (long)item.Id);
        body.SetMeta("item_type", item.ItemType);
        return body;
    }
}
```

- [ ] **Step 3: Create `client/mods/base/spawners/ContentSpawner.cs`** — shared `SpawnVisual` helper

```csharp
// client/mods/base/spawners/ContentSpawner.cs
using Godot;

namespace SandboxRPG;

/// <summary>
/// Shared visual-spawning helper used by all spawners.
/// Priority: ScenePath → ModelPath → fallback coloured box.
/// </summary>
public static class ContentSpawner
{
    public static Node3D SpawnVisual(ContentDef? def, string typeFallback)
    {
        // Priority: ScenePath (full prefab) → ModelPath (generated) → coloured fallback box
        if (def is not null && !string.IsNullOrEmpty(def.ScenePath) && ResourceLoader.Exists(def.ScenePath))
            return ResourceLoader.Load<PackedScene>(def.ScenePath).Instantiate<Node3D>();

        if (def is not null && !string.IsNullOrEmpty(def.ModelPath) && ResourceLoader.Exists(def.ModelPath))
        {
            var model = ModelRegistry.Get(def.ModelPath)!.Instantiate<Node3D>();
            model.Scale = Vector3.One * def.Scale;
            Color? tint = def.TintColor != Colors.White ? def.TintColor : null;
            ModelRegistry.ApplyMaterials(model, tint);  // pass tint so rock/wood/stone tints apply
            return model;
        }

        return CreateFallbackMesh(def?.TintColor ?? new Color(0.8f, 0.8f, 0.2f), def?.Scale ?? 0.4f);
    }

    private static MeshInstance3D CreateFallbackMesh(Color color, float size)
    {
        var mesh = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(size, size, size) },
            Position = new Vector3(0, size * 0.5f, 0),
        };
        mesh.MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.9f };
        return mesh;
    }
}
```

- [ ] **Step 4: Update `client/mods/base/spawners/StructureSpawner.cs`** — use `StructureRegistry`

Read the existing `StructureSpawner.cs`, copy it to the new location, then replace the switch statements with `StructureRegistry.Get(structureType)`:

```bash
cp client/scripts/world/StructureSpawner.cs client/mods/base/spawners/StructureSpawner.cs
```

Then in `client/mods/base/spawners/StructureSpawner.cs`, replace the body of `CreateStructureVisual`:

```csharp
private static Node3D CreateStructureVisual(PlacedStructure s)
{
    var def  = StructureRegistry.Get(s.StructureType);
    var body = new StaticBody3D { Name = $"Structure_{s.Id}", CollisionLayer = 1, CollisionMask = 1 };

    var visual = ContentSpawner.SpawnVisual(def, s.StructureType);
    body.AddChild(visual);

    // CollisionCenter lifts the box shape so walls/floors sit correctly above placement point
    var collSize   = def?.CollisionSize   ?? Vector3.One;
    var collCenter = def?.CollisionCenter ?? Vector3.Zero;
    body.AddChild(new CollisionShape3D
    {
        Shape    = new BoxShape3D { Size = collSize },
        Position = collCenter,  // e.g. (0, 1.2, 0) for walls so box is above ground
    });

    body.Position = new Vector3(s.PosX, s.PosY, s.PosZ);
    body.Rotation = new Vector3(0, s.RotY, 0);
    body.SetMeta("structure_id",   (long)s.Id);
    body.SetMeta("structure_type", s.StructureType);
    body.SetMeta("owner_id",       s.OwnerId.ToString());
    return body;
}
```

(Keep all other methods — `Sync`, `OnUpdated`, etc. — identical to the original.)

- [ ] **Step 5: Update `client/mods/base/spawners/WorldObjectSpawner.cs`** — use `ObjectRegistry`

```bash
cp client/scripts/world/WorldObjectSpawner.cs client/mods/base/spawners/WorldObjectSpawner.cs
```

In `client/mods/base/spawners/WorldObjectSpawner.cs`, replace `CreateWorldObjectVisual` body and keep `BuildConvexShape` (it moves with the file):

```csharp
private static Node3D CreateWorldObjectVisual(WorldObject obj)
{
    var def   = ObjectRegistry.Get(obj.ObjectType);
    var body  = new StaticBody3D { Name = $"WorldObject_{obj.Id}" };
    float scale = def?.Scale ?? 1.0f;

    if (def is not null && !string.IsNullOrEmpty(def.ModelPath) && ResourceLoader.Exists(def.ModelPath))
    {
        var model = ModelRegistry.Get(def.ModelPath)!.Instantiate<Node3D>();
        model.Scale = Vector3.One * scale;
        Color? tint = def.TintColor != Colors.White ? def.TintColor : null;
        ModelRegistry.ApplyMaterials(model, tint);
        body.AddChild(model);

        if (def.UseConvexCollision)
            body.AddChild(new CollisionShape3D { Shape = BuildConvexShape(model, scale) });
        else
            body.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.5f * scale, Height = 1.5f * scale } });
    }
    else
    {
        // Fallback box — sizes match original WorldObjectSpawner fallback exactly
        body.AddChild(new MeshInstance3D
        {
            Mesh     = new BoxMesh { Size = new Vector3(0.8f, 1.5f, 0.8f) * scale },
            Position = new Vector3(0, 0.75f * scale, 0),
        });
        var shapeSize = obj.ObjectType switch
        {
            "tree_pine" or "tree_palm" => new Vector3(1.2f, 6.0f, 1.2f),
            "tree_dead"                => new Vector3(1.0f, 5.0f, 1.0f),
            "rock_large"               => new Vector3(2.4f, 1.6f, 2.4f),
            "rock_small"               => new Vector3(1.1f, 0.7f, 1.1f),
            "bush"                     => new Vector3(1.5f, 1.0f, 1.5f),
            _                          => new Vector3(0.8f, 1.0f, 0.8f),
        };
        body.AddChild(new CollisionShape3D
        {
            Shape    = new BoxShape3D { Size = shapeSize },
            Position = new Vector3(0, shapeSize.Y / 2f, 0),
        });
    }

    float groundY = Terrain.HeightAt(obj.PosX, obj.PosZ);
    body.Position = new Vector3(obj.PosX, groundY, obj.PosZ);
    body.Rotation = new Vector3(0, obj.RotY, 0);
    body.AddToGroup("world_object");
    body.SetMeta("world_object_id", (long)obj.Id);
    body.SetMeta("object_type", obj.ObjectType);
    return body;
}

// BuildConvexShape — copied unchanged from original WorldObjectSpawner
private static ConvexPolygonShape3D BuildConvexShape(Node3D model, float scale)
{
    var pts = new System.Collections.Generic.List<Vector3>();
    foreach (var mi in model.FindChildren("*", "MeshInstance3D", owned: false).OfType<MeshInstance3D>())
    {
        if (mi.Mesh is not ArrayMesh arrayMesh) continue;
        var shape = arrayMesh.CreateConvexShape(clean: true, simplify: false);
        pts.AddRange(shape.Points);
    }
    for (int i = 0; i < pts.Count; i++)
        pts[i] *= scale;
    return new ConvexPolygonShape3D { Points = pts.ToArray() };
}
```

Keep all other methods (`SyncAll`, `OnUpdated`) identical to the original.

- [ ] **Step 6: Update `client/mods/base/building/BuildSystem.cs`** — use `StructureRegistry`

```bash
cp client/scripts/building/BuildSystem.cs client/mods/base/building/BuildSystem.cs
```

In `client/mods/base/building/BuildSystem.cs`, replace the hard-coded `BuildableTypes` set:

Find:
```csharp
private static readonly HashSet<string> BuildableTypes = new() { "wood_wall", ... };
```

Replace with a property that reads from the registry:
```csharp
private static bool IsBuildable(string type)
    => StructureRegistry.Get(type)?.IsPlaceable ?? false;
```

Update any call sites that used `BuildableTypes.Contains(x)` to `IsBuildable(x)`.

When iterating available buildable types for the build menu, replace the hard-coded list with:
```csharp
foreach (var (type, def) in StructureRegistry.AllPlaceable())
    // ... populate build menu item using type and def.DisplayName
```

- [ ] **Step 7: Delete old engine content files**

```bash
git rm client/scripts/world/WorldManager.cs
git rm client/scripts/world/InteractionSystem.cs
git rm client/scripts/world/WorldItemSpawner.cs
git rm client/scripts/world/StructureSpawner.cs
git rm client/scripts/world/WorldObjectSpawner.cs
git rm client/scripts/player/PlayerController.cs
git rm client/scripts/player/RemotePlayer.cs
git rm client/scripts/player/PlayerSpawner.cs
git rm client/scripts/building/BuildSystem.cs
git rm client/scripts/ui/HUD.cs
git rm client/scripts/ui/InventoryCraftingPanel.cs
git rm client/scripts/ui/ChatUI.cs
git rm client/scripts/ui/Hotbar.cs
```

- [ ] **Step 8: Verify client compiles**

```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: 0 errors. All types are still in `namespace SandboxRPG`; no namespace changes occurred.

- [ ] **Step 9: Commit**

```bash
git add client/mods/base/
git commit -m "refactor(client): move all game content into client/mods/base"
```

---

## Chunk 4: Wiring + Scene Updates + Validation

### Task 10: Update `project.godot` and scene script paths

The Godot project file references script paths for autoloads, and scene `.tscn` files reference script paths for attached nodes. These must be updated to point to the new locations.

**Files:**
- Modify: `client/project.godot` — add `BaseClientMod` autoload; update any paths that moved
- Modify: `client/scenes/Main.tscn` — update `WorldManager` script path
- Modify: any `.tscn` files that reference moved scripts

- [ ] **Step 1: Add `BaseClientMod` autoload to `project.godot`**

In `client/project.godot`, find the `[autoload]` section and add `BaseClientMod` before `HelloWorldClientMod`:

```ini
[autoload]
ModManager="*res://scripts/mods/ModManager.cs"
BaseClientMod="*res://mods/base/BaseClientMod.cs"
HelloWorldClientMod="*res://mods/hello-world/HelloWorldClientMod.cs"
GameManager="*res://scripts/networking/GameManager.cs"
SceneRouter="*res://scripts/networking/SceneRouter.cs"
UIManager="*res://scripts/ui/UIManager.cs"
```

(Order matters — autoloads run `_Ready()` in the listed order, so `ModManager` must come first, `BaseClientMod` before `HelloWorldClientMod`.)

- [ ] **Step 2: Update `Main.tscn` script references**

Open `client/scenes/Main.tscn` in a text editor. Find any `script` properties that reference moved files and update their paths. For example:

- `res://scripts/world/WorldManager.cs` → `res://mods/base/world/WorldManager.cs`
- Any spawner or UI scripts in the scene tree that also moved

Search for all old paths:
```bash
grep -r "scripts/world\|scripts/player\|scripts/building\|scripts/ui/HUD\|scripts/ui/Hotbar\|scripts/ui/Chat\|scripts/ui/Inventory" client/scenes/
```

Update each occurrence in the `.tscn` file(s) to the new `mods/base/` path.

- [ ] **Step 3: Open Godot editor and verify no broken script references**

Launch Godot:
```
"C:\Users\Jonas\AppData\Local\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.6.1-stable_mono_win64\Godot_v4.6.1-stable_mono_win64.exe" --path client/ --editor
```

Check the scene tree for any red/broken script icons. Fix any remaining path issues.

- [ ] **Step 4: Verify client build from Godot (or CLI)**

```bash
cd client && dotnet build SandboxRPG.csproj
```

- [ ] **Step 5: Commit**

```bash
git add client/project.godot client/scenes/
git commit -m "fix(client): update scene script paths to mods/base locations"
```

---

### Task 11: Update `HelloWorldClientMod` — fix dependencies

**Files:**
- Modify: `client/mods/hello-world/HelloWorldClientMod.cs`

The hello-world mod currently declares `Dependencies = []`. It does not depend on base (it doesn't use the base registries). No changes required unless it uses `ItemRegistry` or similar — verify and leave as-is.

- [ ] **Step 1: Verify hello-world compiles and tests pass**

```bash
cd client && dotnet build SandboxRPG.csproj
cd client && dotnet test
```

---

### Task 12: Full smoke test

- [ ] **Step 1: Start SpacetimeDB and publish module**

```bash
# In a separate terminal (if not already running):
spacetime start --in-memory

# Re-authenticate if server was restarted:
spacetime logout && spacetime login --server-issued-login local --no-browser

# Build and publish:
cd server && spacetime build
spacetime publish -b bin/Release/net8.0/wasi-wasm/AppBundle/StdbModule.wasm
```

- [ ] **Step 2: Launch Godot and connect**

```
"C:\Users\Jonas\AppData\Local\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.6.1-stable_mono_win64\Godot_v4.6.1-stable_mono_win64.exe" --path client/
```

Verify:
- World loads (terrain, trees, rocks visible)
- Player spawns with correct position
- Inventory has wood_pickaxe + wood_axe
- Can pick up items
- Can craft recipes
- Can place structures
- Chat works
- Hello-world panel appears

- [ ] **Step 3: Run all tests**

```bash
# Server tests
cd server && dotnet test SandboxRPG.Server.Tests

# Client tests (GdUnit4 — run via Godot or CLI)
cd client && dotnet test
```

Expected: all passing.

- [ ] **Step 4: Final commit**

```bash
git add -A
git commit -m "refactor: complete base mod architecture extraction — all tests passing"
```

---

## File Change Summary

### Files deleted from engine
| Deleted from | Moved to |
|---|---|
| `server/Tables.cs` | `mods/base/server/Tables.cs` |
| `server/InventoryHelpers.cs` | `mods/base/server/InventoryHelpers.cs` |
| `server/PlayerReducers.cs` | `mods/base/server/PlayerReducers.cs` |
| `server/InventoryReducers.cs` | `mods/base/server/InventoryReducers.cs` |
| `server/CraftingReducers.cs` | `mods/base/server/CraftingReducers.cs` |
| `server/BuildingReducers.cs` | `mods/base/server/BuildingReducers.cs` (updated) |
| `server/WorldReducers.cs` | `mods/base/server/WorldReducers.cs` (updated) |
| `client/scripts/world/WorldManager.cs` | `client/mods/base/world/WorldManager.cs` |
| `client/scripts/world/InteractionSystem.cs` | `client/mods/base/world/InteractionSystem.cs` |
| `client/scripts/world/WorldItemSpawner.cs` | `client/mods/base/spawners/WorldItemSpawner.cs` (updated) |
| `client/scripts/world/StructureSpawner.cs` | `client/mods/base/spawners/StructureSpawner.cs` (updated) |
| `client/scripts/world/WorldObjectSpawner.cs` | `client/mods/base/spawners/WorldObjectSpawner.cs` (updated) |
| `client/scripts/player/PlayerController.cs` | `client/mods/base/player/PlayerController.cs` |
| `client/scripts/player/RemotePlayer.cs` | `client/mods/base/player/RemotePlayer.cs` |
| `client/scripts/player/PlayerSpawner.cs` | `client/mods/base/spawners/PlayerSpawner.cs` |
| `client/scripts/building/BuildSystem.cs` | `client/mods/base/building/BuildSystem.cs` (updated) |
| `client/scripts/ui/HUD.cs` | `client/mods/base/ui/HUD.cs` |
| `client/scripts/ui/InventoryCraftingPanel.cs` | `client/mods/base/ui/InventoryCraftingPanel.cs` |
| `client/scripts/ui/ChatUI.cs` | `client/mods/base/ui/ChatUI.cs` |
| `client/scripts/ui/Hotbar.cs` | `client/mods/base/ui/Hotbar.cs` |

### New files in engine
| File | Purpose |
|---|---|
| `server/mods/IMod.cs` | Updated — `OnClientConnected`/`OnClientDisconnected` hooks |
| `server/mods/ModLoader.cs` | Updated — forwarding methods |
| `client/scripts/mods/IClientMod.cs` | Updated — `Dependencies` property |
| `client/scripts/mods/ModManager.cs` | Updated — topological sort |

### New files in base mod
| File | Purpose |
|---|---|
| `mods/base/server/BaseMod.cs` | IMod implementation; wires all hooks |
| `mods/base/server/StructureConfig.cs` | Static registry: structure type → max health |
| `mods/base/server/HarvestConfig.cs` | Static registry: tool damage + drop tables |
| `mods/base/server/Seeding.cs` | Seed data + TerrainHeightAt() utility |
| `client/mods/base/BaseClientMod.cs` | IClientMod autoload; calls BaseContent.RegisterAll() |
| `client/mods/base/content/BaseContent.cs` | Programmatic registration of all base content |
| `client/mods/base/registries/ContentDef.cs` | ContentDef / ItemDef / StructureDef / ObjectDef |
| `client/mods/base/registries/ItemRegistry.cs` | Item lookup + LoadFolder |
| `client/mods/base/registries/StructureRegistry.cs` | Structure lookup + AllPlaceable() |
| `client/mods/base/registries/ObjectRegistry.cs` | Object lookup + LoadFolder |
| `client/mods/base/spawners/ContentSpawner.cs` | Shared ScenePath/ModelPath/fallback dispatch |
