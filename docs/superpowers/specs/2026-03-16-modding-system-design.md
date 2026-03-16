# Modding System Design

**Date:** 2026-03-16
**Status:** Approved
**Scope:** Internal mod system for SandboxRPG — server (SpacetimeDB C# WASM) + client (Godot 4.6 C#)

---

## Overview

A compile-time modding system that allows new game content to be developed in self-contained modules. Mods are enabled by adding a single `.csproj` glob include for the server; client mod scripts live under `client/mods/` and are auto-discovered by Godot. Mods register themselves with zero changes to core game files. They can declare dependencies on other mods; the loader seeds them in dependency order.

Target mods (future sessions): `currency`, `shop` (depends on currency), `casino` (depends on currency).

---

## Constraints

- **SpacetimeDB compiles to a single WASM module** — mods cannot be loaded/unloaded at runtime. Enable/disable is compile-time only.
- **All SpacetimeDB tables and reducers must be nested inside `public static partial class Module`** in `namespace SandboxRPG.Server`. Mod table and reducer files follow this same pattern.
- **No reflection in WASM** — mod registration uses a static field initializer on `Module`, which is guaranteed to run before the `Init` reducer.
- **Godot scans all scripts under `client/`** — client mod scripts placed under `client/mods/<mod-name>/` are automatically compiled.

---

## Folder Structure

```
mods/
└── hello-world/
    ├── mod.json                          # human-readable metadata only
    └── server/
        ├── HelloWorldMod.cs              # IMod nested class + static field registration
        ├── HelloWorldTables.cs           # [Table] definitions (inside partial class Module)
        └── HelloWorldReducers.cs         # [Reducer] definitions (inside partial class Module)

client/
└── mods/
    └── hello-world/
        ├── HelloWorldClientMod.cs        # IClientMod + static field registration
        └── ui/
            ├── HelloWorldPanel.tscn
            └── HelloWorldPanel.cs
```

Core mod system files (part of the base game, not a mod):
```
server/mods/
    ├── IMod.cs         # server mod interface
    └── ModLoader.cs    # Register(), RunAll(ctx) with dependency sort

client/scripts/mods/
    ├── IClientMod.cs   # client mod interface
    └── ModManager.cs   # Godot autoload singleton
```

---

## Enabling a Mod

**Server** — add one line to `server/StdbModule.csproj` inside the `<ItemGroup>`:
```xml
<Compile Include="../mods/hello-world/server/**/*.cs" />
```

**Client** — no `.csproj` change needed. Godot auto-compiles all `.cs` files under `client/`. Client mod scripts at `client/mods/hello-world/` are discovered automatically.

**After enabling** — if the mod adds tables or reducers, regenerate client bindings (see Bindings section).

**Disabling** — remove the `.csproj` line, remove or leave the `client/mods/<mod>/` folder, rebuild.

---

## Server: IMod Interface

```csharp
// server/mods/IMod.cs
using SpacetimeDB;

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
using SpacetimeDB;
using System.Collections.Generic;
using System.Linq;
using SandboxRPG.Server.Mods;

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

    // Kahn's algorithm. Throws InvalidOperationException on circular dependency.
    // Unknown dependency names (mods not in the registered list) are ignored.
    private static IEnumerable<IMod> TopoSort(List<IMod> mods)
    {
        var nameToMod = mods.ToDictionary(m => m.Name);
        var inDegree = mods.ToDictionary(m => m.Name, _ => 0);

        foreach (var mod in mods)
            foreach (var dep in mod.Dependencies)
                if (nameToMod.ContainsKey(dep))
                    inDegree[mod.Name]++;

        var queue = new Queue<IMod>(mods.Where(m => inDegree[m.Name] == 0));
        var result = new List<IMod>();

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            result.Add(current);
            foreach (var dependent in mods.Where(m => m.Dependencies.Contains(current.Name)))
            {
                inDegree[dependent.Name]--;
                if (inDegree[dependent.Name] == 0)
                    queue.Enqueue(dependent);
            }
        }

        if (result.Count != mods.Count)
            throw new InvalidOperationException("[ModLoader] Circular dependency detected in mods.");

        return result;
    }
}
```

**One permanent addition to `Lifecycle.cs` Init:**
```csharp
[Reducer(ReducerKind.Init)]
public static void Init(ReducerContext ctx)
{
    // ... existing seed calls ...
    ModLoader.RunAll(ctx);  // ← added once, never touched again
}
```

---

## Server: Registration Pattern

SpacetimeDB calls into `Module` before running any reducer. This guarantees that any static field initializer on `partial class Module` fires before `Init`. Mods exploit this by declaring a `private static readonly` field on `Module` that instantiates their `IMod` class — whose constructor calls `ModLoader.Register(this)`.

**`HelloWorldMod.cs`:**
```csharp
using SpacetimeDB;
using SandboxRPG.Server.Mods;

namespace SandboxRPG.Server;

// partial class Module ensures registration fires before Init
public static partial class Module
{
    private static readonly HelloWorldModImpl _helloWorldMod = new();

    private sealed class HelloWorldModImpl : IMod
    {
        public HelloWorldModImpl() => ModLoader.Register(this);

        public string Name => "hello-world";
        public string Version => "1.0.0";
        public string[] Dependencies => System.Array.Empty<string>();

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
}
```

---

## Server: Hello World Table & Reducer

All table and reducer definitions must be nested inside `partial class Module` in `namespace SandboxRPG.Server` — the same pattern as every other server file.

**`HelloWorldTables.cs`:**
```csharp
using SpacetimeDB;

namespace SandboxRPG.Server;

public static partial class Module
{
    /// <summary>One greeting row per player. Owned by hello-world mod.</summary>
    [Table(Name = "hello_world_message", Public = true)]
    public partial struct HelloWorldMessage
    {
        [PrimaryKey]
        public Identity PlayerId;
        public string Message;
    }
}
```

**`HelloWorldReducers.cs`:**
```csharp
using SpacetimeDB;

namespace SandboxRPG.Server;

public static partial class Module
{
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
}
```

Note: `HelloWorldMessage` uses `Identity PlayerId` as `[PrimaryKey]` (not `[AutoInc]`) because there is exactly one greeting per player identity.

---

## Client: IClientMod Interface

```csharp
// client/scripts/mods/IClientMod.cs
namespace SandboxRPG;

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
using Godot;
using System.Collections.Generic;

namespace SandboxRPG;

public partial class ModManager : Node
{
    public static ModManager Instance { get; private set; }
    private static readonly List<IClientMod> _pending = new();

    // Called before _Ready — safe for mods to register here
    public static void Register(IClientMod mod) => _pending.Add(mod);

    public override void _Ready() => Instance = this;

    /// <summary>Called from WorldManager._Ready once the game scene is fully loaded.</summary>
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

`ModManager` is registered as a Godot autoload in `project.godot` (one-time setup). `InitializeAll(this)` is called from `WorldManager._Ready()` — this is the earliest point where the full game scene tree exists and HUD panels can be safely added as children.

---

## Client: Hello World Mod

**`HelloWorldClientMod.cs`** — placed at `client/mods/hello-world/HelloWorldClientMod.cs`:
```csharp
using Godot;

namespace SandboxRPG;

public class HelloWorldClientMod : IClientMod
{
    // Runs when the type is loaded by Godot's C# compiler at startup
    static HelloWorldClientMod() => ModManager.Register(new HelloWorldClientMod());

    public string ModName => "hello-world";

    public void Initialize(Node sceneRoot)
    {
        var panel = GD.Load<PackedScene>("res://mods/hello-world/ui/HelloWorldPanel.tscn").Instantiate();
        sceneRoot.AddChild(panel);
    }
}
```

Note: `res://` is relative to `client/` (Godot's project root), so `client/mods/hello-world/ui/HelloWorldPanel.tscn` maps to `res://mods/hello-world/ui/HelloWorldPanel.tscn`.

**`HelloWorldPanel.cs`** — code-behind for the `.tscn` scene, attached as the root script:
```csharp
using Godot;

namespace SandboxRPG;

public partial class HelloWorldPanel : Control
{
    private Label _label;

    public override void _Ready()
    {
        _label = GetNode<Label>("Label");
        _label.Text = "Hello from HelloWorld Mod!";

        // Subscribe to table updates via GameManager's Conn
        var conn = GameManager.Instance.Conn;
        conn.Db.HelloWorldMessage.OnInsert += (_, row) => OnMessageInsertOrUpdate(row);
        conn.Db.HelloWorldMessage.OnUpdate += (_, _, row) => OnMessageInsertOrUpdate(row);

        // Send greeting once connected
        if (GameManager.Instance.IsConnected)
            Reducers.SayHello("Hello from HelloWorld Mod!");
        else
            GameManager.Instance.Connected += () => Reducers.SayHello("Hello from HelloWorld Mod!");
    }

    private void OnMessageInsertOrUpdate(HelloWorldMessage row)
    {
        if (row.PlayerId == GameManager.Instance.LocalIdentity)
            _label.Text = row.Message;
    }
}
```

Client mods subscribe directly to `GameManager.Instance.Conn.Db.<Table>` events — no changes to `GameManager.cs` required.

---

## mod.json Schema

`mod.json` is **human-readable metadata only** — it is not parsed at runtime. The authoritative registration is the static field/constructor pattern above. It exists to document intent and dependency relationships.

```json
{
  "name": "hello-world",
  "version": "1.0.0",
  "description": "Template mod — copy and rename to create a new mod.",
  "dependencies": [],
  "author": "internal"
}
```

Future mods with dependencies: `"dependencies": ["currency"]`.

---

## Bindings Regeneration

After enabling a mod that adds `[Table]` or `[Reducer]` definitions, regenerate client bindings:

```bash
cd server && spacetime generate --lang csharp \
  --out-dir ../client/scripts/networking/SpacetimeDB \
  --bin-path bin/Release/net8.0/wasi-wasm/AppBundle/StdbModule.wasm
```

This is required for `conn.Db.HelloWorldMessage` and `Reducers.SayHello` to exist in the generated code.

---

## Future Mods Sketch

| Mod | Depends On | Adds |
|---|---|---|
| `currency` | — | `Currency` fields on Player, coin item types, earn/spend reducers |
| `shop` | `currency` | `ShopNpc`, `ShopInventory` tables, buy/sell reducers, NPC scene |
| `casino` | `currency` | `CasinoGame` table, bet reducers, slot machine scene |

---

## Out of Scope

- Runtime mod loading/unloading (not possible with WASM)
- Community/external modding (internal only for now)
- Inter-mod event buses (add when needed)
- Mod settings UI (add when needed)
- `mod.json` validation tooling (add if mod count grows large)
