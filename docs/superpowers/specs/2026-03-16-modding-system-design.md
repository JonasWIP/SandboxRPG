# Modding System Design

**Date:** 2026-03-16
**Status:** Approved
**Scope:** Internal mod system for SandboxRPG — server (SpacetimeDB C# WASM) + client (Godot 4.6 C#)

---

## Overview

A compile-time modding system that allows new game content to be developed in self-contained modules. Mods live in a top-level `mods/` folder, register themselves with zero changes to core files, and are enabled by adding a single line to the server `.csproj` and ensuring client scripts are discoverable by Godot. Mods can declare dependencies on other mods; the loader seeds them in dependency order.

Target mods (future sessions): `currency`, `shop` (depends on currency), `casino` (depends on currency).

---

## Constraints

- **SpacetimeDB compiles to a single WASM module** — mods cannot be loaded/unloaded at runtime. Enable/disable is compile-time only (include/exclude files from the build).
- **No reflection in WASM** — mod discovery uses static constructor self-registration, not assembly scanning.
- **Godot scans all project files** — client mod scripts placed under `client/mods/` are automatically compiled into the game.

---

## Folder Structure

```
mods/
└── hello-world/
    ├── mod.json                          # metadata
    ├── server/
    │   ├── HelloWorldMod.cs              # IMod + self-registration
    │   ├── HelloWorldTables.cs           # [Table] definitions
    │   └── HelloWorldReducers.cs         # [Reducer] definitions
    └── client/
        ├── HelloWorldClientMod.cs        # IClientMod + self-registration
        └── ui/
            ├── HelloWorldPanel.tscn
            └── HelloWorldPanel.cs

server/mods/
    ├── IMod.cs                           # server mod interface
    └── ModLoader.cs                      # registration + dependency-ordered seeding

client/scripts/mods/
    ├── IClientMod.cs                     # client mod interface
    └── ModManager.cs                     # Godot autoload singleton
```

---

## Enabling a Mod

**Server** — add one line to `server/StdbModule.csproj`:
```xml
<Compile Include="../mods/hello-world/server/**/*.cs" />
```

**Client** — Godot automatically compiles scripts under `client/mods/`. The `ModManager` autoload must be registered once in `project.godot` (permanent, not per-mod).

**Disabling** — remove the `.csproj` line and rebuild. Client scripts under `client/mods/<mod>/` can be left in place (they do nothing if not registered) or deleted.

---

## Server: IMod Interface

```csharp
// server/mods/IMod.cs
namespace SandboxRPG.Server.Mods;

public interface IMod
{
    string Name { get; }
    string Version { get; }
    string[] Dependencies { get; }  // names of mods that must seed before this one
    void Seed(ReducerContext ctx);
}
```

---

## Server: ModLoader

```csharp
// server/mods/ModLoader.cs
namespace SandboxRPG.Server.Mods;

public static class ModLoader
{
    private static readonly List<IMod> _mods = new();

    public static void Register(IMod mod) => _mods.Add(mod);

    public static void RunAll(ReducerContext ctx)
    {
        foreach (var mod in TopoSort(_mods))
        {
            Log.Info($"[ModLoader] Seeding mod: {mod.Name} v{mod.Version}");
            mod.Seed(ctx);
        }
    }

    // Kahn's algorithm — throws if circular dependency detected
    private static IEnumerable<IMod> TopoSort(List<IMod> mods) { ... }
}
```

**One permanent addition to `Lifecycle.cs` Init:**
```csharp
ModLoader.RunAll(ctx);
```

---

## Server: Mod Self-Registration Pattern

```csharp
// mods/hello-world/server/HelloWorldMod.cs
namespace SandboxRPG.Server.Mods.HelloWorld;

public class HelloWorldMod : IMod
{
    static HelloWorldMod() => ModLoader.Register(new HelloWorldMod());

    public string Name => "hello-world";
    public string Version => "1.0.0";
    public string[] Dependencies => Array.Empty<string>();

    public void Seed(ReducerContext ctx)
    {
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe
        {
            ResultItemType = "hello_item",
            ResultQuantity = 1,
            Ingredients = "wood:1",
            CraftTimeSeconds = 1f,
        });

        Log.Info("[HelloWorldMod] Seeded.");
    }
}
```

The `static HelloWorldMod()` constructor fires when the class is first touched at startup, registering the mod before `Init` runs.

---

## Server: Hello World Table & Reducer

```csharp
// mods/hello-world/server/HelloWorldTables.cs
[Table(Name = "hello_world_message", Public = true)]
public partial struct HelloWorldMessage
{
    [PrimaryKey]
    public Identity PlayerId;
    public string Message;
}
```

```csharp
// mods/hello-world/server/HelloWorldReducers.cs
[Reducer]
public static void SayHello(ReducerContext ctx, string message)
{
    var existing = ctx.Db.HelloWorldMessage.PlayerId.Find(ctx.Sender);
    if (existing is not null)
    {
        var row = existing.Value;
        row.Message = message;
        ctx.Db.HelloWorldMessage.PlayerId.Update(row);
    }
    else
    {
        ctx.Db.HelloWorldMessage.Insert(new HelloWorldMessage
        {
            PlayerId = ctx.Sender,
            Message = message,
        });
    }
}
```

---

## Client: IClientMod Interface

```csharp
// client/scripts/mods/IClientMod.cs
public interface IClientMod
{
    string ModName { get; }
    void Initialize(Node sceneRoot);
}
```

---

## Client: ModManager Autoload

```csharp
// client/scripts/mods/ModManager.cs
public partial class ModManager : Node
{
    public static ModManager Instance { get; private set; }
    private static readonly List<IClientMod> _pending = new();

    public static void Register(IClientMod mod) => _pending.Add(mod);

    public override void _Ready() => Instance = this;

    public void InitializeAll(Node sceneRoot)
    {
        foreach (var mod in _pending)
        {
            GD.Print($"[ModManager] Initializing: {mod.ModName}");
            mod.Initialize(sceneRoot);
        }
    }
}
```

`InitializeAll(this)` is called from the main game scene (e.g. `WorldManager._Ready`) once the scene tree is ready.

---

## Client: Hello World Mod

```csharp
// mods/hello-world/client/HelloWorldClientMod.cs
public class HelloWorldClientMod : IClientMod
{
    static HelloWorldClientMod() => ModManager.Register(new HelloWorldClientMod());

    public string ModName => "hello-world";

    public void Initialize(Node sceneRoot)
    {
        var panel = GD.Load<PackedScene>("res://mods/hello-world/client/ui/HelloWorldPanel.tscn").Instantiate();
        sceneRoot.AddChild(panel);
    }
}
```

`HelloWorldPanel.cs` subscribes to the `HelloWorldMessage` table via `GameManager` signals and displays the greeting in a HUD label. On connect it calls `Reducers.SayHello("Hello from HelloWorld Mod!")`.

---

## mod.json Schema

```json
{
  "name": "hello-world",
  "version": "1.0.0",
  "description": "Template mod — copy and rename to create a new mod.",
  "dependencies": [],
  "author": "internal"
}
```

Future mods declare deps: `"dependencies": ["currency"]`.

---

## Bindings Regeneration

After enabling a new mod that adds tables or reducers, regenerate client bindings:

```bash
cd server && spacetime generate --lang csharp \
  --out-dir ../client/scripts/networking/SpacetimeDB \
  --bin-path bin/Release/net8.0/wasi-wasm/AppBundle/StdbModule.wasm
```

This is required any time a mod adds a `[Table]` or `[Reducer]`.

---

## Future Mods Sketch

| Mod | Depends On | Adds |
|---|---|---|
| `currency` | — | `Currency` table on Player, coin item types, mint/spend reducers |
| `shop` | `currency` | `ShopNpc`, `ShopInventory` tables, buy/sell reducers, NPC scene |
| `casino` | `currency` | `CasinoGame` table, bet reducers, slot machine scene |

---

## Out of Scope

- Runtime mod loading/unloading (not possible with WASM)
- Community/external modding (internal only for now)
- Inter-mod event buses (add when needed)
- Mod settings UI (add when needed)
