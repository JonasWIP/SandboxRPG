# Modding System Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a compile-time mod system with `IMod`/`IClientMod` interfaces, a `ModLoader` with dependency-ordered seeding, and a fully working "Hello World" mod that adds a table, reducer, recipe seed, and HUD panel.

**Architecture:** Server mods are C# files glob-included into `server/StdbModule.csproj`; they register via a static field initializer on `partial class Module` which guarantees execution before `Init`. Client mods are Godot autoloads (one entry per mod in `project.godot`); they call `ModManager.Register(this)` in `_Ready`, and `WorldManager._Ready()` calls `ModManager.Instance.InitializeAll(this)` once the scene tree is ready.

**Tech Stack:** SpacetimeDB 2.0 C# WASM (server), Godot 4.6.1 C# (client), `spacetime` CLI for builds and bindings.

**Spec:** `docs/superpowers/specs/2026-03-16-modding-system-design.md`

---

## File Map

| Action | Path | Purpose |
|---|---|---|
| Create | `server/mods/IMod.cs` | Server mod interface |
| Create | `server/mods/ModLoader.cs` | Registration + topo-sort seeding |
| Modify | `server/Lifecycle.cs` | Add `ModLoader.RunAll(ctx)` at end of `Init` |
| Create | `client/scripts/mods/IClientMod.cs` | Client mod interface |
| Create | `client/scripts/mods/ModManager.cs` | Godot autoload — collects and initializes client mods |
| Modify | `client/project.godot` | Add `ModManager` autoload (once) |
| Modify | `client/scripts/world/WorldManager.cs` | Add `ModManager.Instance.InitializeAll(this)` at end of `_Ready` |
| Create | `mods/hello-world/mod.json` | Human-readable mod metadata |
| Create | `mods/hello-world/server/HelloWorldTables.cs` | `HelloWorldMessage` table |
| Create | `mods/hello-world/server/HelloWorldReducers.cs` | `SayHello` reducer |
| Create | `mods/hello-world/server/HelloWorldMod.cs` | `IMod` impl + static field registration + Seed |
| Modify | `server/StdbModule.csproj` | Add glob include for hello-world server files |
| Regen | `client/scripts/networking/SpacetimeDB/` | Regenerate after adding table + reducer |
| Create | `client/mods/hello-world/HelloWorldClientMod.cs` | Godot autoload: registers with ModManager, instantiates panel |
| Create | `client/mods/hello-world/ui/HelloWorldPanel.cs` | Control node: subscribes to table, shows greeting |
| Create | `client/mods/hello-world/ui/HelloWorldPanel.tscn` | Scene file for the HUD panel |
| Modify | `client/project.godot` | Add `HelloWorldClientMod` autoload |

---

## Chunk 1: Core Mod Infrastructure

### Task 1: Create server mod interface

**Files:**
- Create: `server/mods/IMod.cs`

- [ ] **Step 1: Create the file**

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
}
```

---

### Task 2: Create ModLoader

**Files:**
- Create: `server/mods/ModLoader.cs`

- [ ] **Step 1: Create the file**

```csharp
// server/mods/ModLoader.cs
using SpacetimeDB;
using System.Collections.Generic;
using System.Linq;

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
    // Unknown dependency names (mods not registered) are silently ignored.
    private static IEnumerable<IMod> TopoSort(List<IMod> mods)
    {
        var nameToMod = mods.ToDictionary(m => m.Name);
        var inDegree   = mods.ToDictionary(m => m.Name, _ => 0);

        foreach (var mod in mods)
            foreach (var dep in mod.Dependencies)
                if (nameToMod.ContainsKey(dep))
                    inDegree[mod.Name]++;

        var queue  = new Queue<IMod>(mods.Where(m => inDegree[m.Name] == 0));
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

---

### Task 3: Wire ModLoader into server Init

**Files:**
- Modify: `server/Lifecycle.cs`

The `Init` reducer calls seed methods then needs to call `ModLoader.RunAll(ctx)` at the end. Add `using SandboxRPG.Server.Mods;` at the top and the call at the bottom of `Init`.

- [ ] **Step 1: Add the using and the call**

Open `server/Lifecycle.cs`. At the top, there is already `using SpacetimeDB;` and `using System;`. Add after those:
```csharp
using SandboxRPG.Server.Mods;
```

At the end of the `Init` reducer body (after `SeedWorldObjects(ctx);`), add:
```csharp
        ModLoader.RunAll(ctx);
```

The `Init` method should end like:
```csharp
    [Reducer(ReducerKind.Init)]
    public static void Init(ReducerContext ctx)
    {
        Log.Info("SandboxRPG server module initialized!");
        SeedTerrainConfig(ctx);
        SeedRecipes(ctx);
        SeedWorldItems(ctx);
        SeedWorldObjects(ctx);
        ModLoader.RunAll(ctx);
    }
```

---

### Task 4: Verify server builds cleanly

- [ ] **Step 1: Build the server**

```bash
cd server && dotnet build
```

Expected: `Build succeeded. 0 Error(s)` (warnings about unused usings are fine)

If it fails, check that `IMod.cs` and `ModLoader.cs` are in `server/mods/` and that the `using` was added to `Lifecycle.cs`.

---

### Task 5: Create client mod interface

**Files:**
- Create: `client/scripts/mods/IClientMod.cs`

- [ ] **Step 1: Create the file**

```csharp
// client/scripts/mods/IClientMod.cs
using Godot;

namespace SandboxRPG;

public interface IClientMod
{
    string ModName { get; }
    void Initialize(Node sceneRoot);
}
```

---

### Task 6: Create ModManager autoload

**Files:**
- Create: `client/scripts/mods/ModManager.cs`

- [ ] **Step 1: Create the file**

```csharp
// client/scripts/mods/ModManager.cs
using Godot;
using System.Collections.Generic;

namespace SandboxRPG;

/// <summary>
/// Autoload singleton. Client mods call ModManager.Register(this) from their _Ready.
/// WorldManager._Ready() calls InitializeAll(this) once the game scene is fully loaded.
/// Autoloads execute _Ready before scene nodes, so all mods are registered before
/// WorldManager.InitializeAll is called.
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

        foreach (var mod in _pending)
        {
            GD.Print($"[ModManager] Initializing: {mod.ModName}");
            mod.Initialize(sceneRoot);
        }
    }
}
```

---

### Task 7: Register ModManager as Godot autoload

**Files:**
- Modify: `client/project.godot`

- [ ] **Step 1: Add the autoload entry**

Open `client/project.godot`. Find the `[autoload]` section:
```ini
[autoload]

GameManager="*res://scripts/networking/GameManager.cs"
SceneRouter="*res://scripts/networking/SceneRouter.cs"
UIManager="*res://scripts/ui/UIManager.cs"
```

Add `ModManager` **before** `GameManager` (autoloads initialize in order — ModManager must be ready before any mod autoload that calls `Register`):
```ini
[autoload]

ModManager="*res://scripts/mods/ModManager.cs"
GameManager="*res://scripts/networking/GameManager.cs"
SceneRouter="*res://scripts/networking/SceneRouter.cs"
UIManager="*res://scripts/ui/UIManager.cs"
```

---

### Task 8: Call InitializeAll from WorldManager

**Files:**
- Modify: `client/scripts/world/WorldManager.cs`

- [ ] **Step 1: Add the call at the end of `_Ready`**

Open `client/scripts/world/WorldManager.cs`. At the end of the `_Ready` method (after the `if (gm.IsConnected ...)` block), add:
```csharp
        ModManager.Instance.InitializeAll(this);
```

The end of `_Ready` should look like:
```csharp
        if (gm.IsConnected && gm.GetLocalPlayer() != null)
            OnSubscriptionApplied();

        ModManager.Instance.InitializeAll(this);
    }
```

---

### Task 9: Verify client builds cleanly

- [ ] **Step 1: Build the client**

```bash
cd client && dotnet build SandboxRPG.csproj
```

Expected: `Build succeeded. 0 Error(s)`

---

### Task 10: Commit core infrastructure

- [ ] **Step 1: Commit**

```bash
git add server/mods/IMod.cs server/mods/ModLoader.cs server/Lifecycle.cs
git add client/scripts/mods/IClientMod.cs client/scripts/mods/ModManager.cs
git add client/project.godot client/scripts/world/WorldManager.cs
git commit -m "feat: add core mod system infrastructure (IMod, ModLoader, IClientMod, ModManager)"
```

---

## Chunk 2: Hello World Server Mod

### Task 11: Create mod.json

**Files:**
- Create: `mods/hello-world/mod.json`

- [ ] **Step 1: Create the file**

```json
{
  "name": "hello-world",
  "version": "1.0.0",
  "description": "Template mod — copy and rename to create a new mod.",
  "dependencies": [],
  "author": "internal"
}
```

> Note: `mod.json` is human-readable metadata only. It is not parsed at runtime.

---

### Task 12: Create HelloWorldMessage table

**Files:**
- Create: `mods/hello-world/server/HelloWorldTables.cs`

- [ ] **Step 1: Create the file**

```csharp
// mods/hello-world/server/HelloWorldTables.cs
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

> Important: Must use `namespace SandboxRPG.Server` and `public static partial class Module` — all SpacetimeDB tables must be nested inside `Module`.

---

### Task 13: Create SayHello reducer

**Files:**
- Create: `mods/hello-world/server/HelloWorldReducers.cs`

- [ ] **Step 1: Create the file**

```csharp
// mods/hello-world/server/HelloWorldReducers.cs
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
                Message  = message,
            });
        }
    }
}
```

---

### Task 14: Create HelloWorldMod registration + Seed

**Files:**
- Create: `mods/hello-world/server/HelloWorldMod.cs`

- [ ] **Step 1: Create the file**

```csharp
// mods/hello-world/server/HelloWorldMod.cs
using SpacetimeDB;
using SandboxRPG.Server.Mods;

namespace SandboxRPG.Server;

// Placing the registration inside partial class Module guarantees the static field
// initializer fires before the Init reducer, which is when ModLoader.RunAll is called.
public static partial class Module
{
    private static readonly HelloWorldModImpl _helloWorldMod = new();

    private sealed class HelloWorldModImpl : IMod
    {
        public HelloWorldModImpl() => ModLoader.Register(this);

        public string Name    => "hello-world";
        public string Version => "1.0.0";
        public string[] Dependencies => System.Array.Empty<string>();

        public void Seed(ReducerContext ctx)
        {
            ctx.Db.CraftingRecipe.Insert(new CraftingRecipe
            {
                ResultItemType   = "hello_item",
                ResultQuantity   = 1,
                Ingredients      = "wood:1",
                CraftTimeSeconds = 1f,
            });
            Log.Info("[HelloWorldMod] Seeded.");
        }
    }
}
```

---

### Task 15: Enable hello-world in StdbModule.csproj

**Files:**
- Modify: `server/StdbModule.csproj`

- [ ] **Step 1: Add the glob include**

Open `server/StdbModule.csproj`. After the `<PackageReference>` ItemGroup, add a new ItemGroup:
```xml
  <!-- Mods: add one line per enabled mod -->
  <ItemGroup>
    <Compile Include="../mods/hello-world/server/**/*.cs" />
  </ItemGroup>
```

The full file should look like:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <RuntimeIdentifier>wasi-wasm</RuntimeIdentifier>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>SandboxRPG.Server</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="SpacetimeDB.Runtime" Version="2.0.*" />
  </ItemGroup>
  <!-- Mods: add one line per enabled mod -->
  <ItemGroup>
    <Compile Include="../mods/hello-world/server/**/*.cs" />
  </ItemGroup>
</Project>
```

---

### Task 16: Build and publish server

- [ ] **Step 1: Build server with the mod included**

```bash
cd server && spacetime build
```

Expected: `Build succeeded.` No errors. The WASM output is at `server/bin/Release/net8.0/wasi-wasm/AppBundle/StdbModule.wasm`.

If it fails with "partial struct HelloWorldMessage" errors, double-check that all three mod `.cs` files use `namespace SandboxRPG.Server` and `public static partial class Module`.

---

### Task 17: Regenerate client bindings

After adding a new `[Table]` and `[Reducer]`, the generated client code must be updated so `conn.Db.HelloWorldMessage` and `Reducers.SayHello` exist.

- [ ] **Step 1: Regenerate**

```bash
cd server && spacetime generate --lang csharp \
  --out-dir ../client/scripts/networking/SpacetimeDB \
  --bin-path bin/Release/net8.0/wasi-wasm/AppBundle/StdbModule.wasm
```

Expected: Files updated in `client/scripts/networking/SpacetimeDB/`. You should see new files like `Tables/HelloWorldMessage.g.cs`, `Types/HelloWorldMessage.g.cs`, and `Reducers/SayHello.g.cs`.

- [ ] **Step 2: Verify client still builds**

```bash
cd client && dotnet build SandboxRPG.csproj
```

Expected: `Build succeeded. 0 Error(s)`

---

### Task 18: Commit server mod

- [ ] **Step 1: Commit**

```bash
git add mods/hello-world/mod.json
git add mods/hello-world/server/HelloWorldTables.cs
git add mods/hello-world/server/HelloWorldReducers.cs
git add mods/hello-world/server/HelloWorldMod.cs
git add server/StdbModule.csproj
git add client/scripts/networking/SpacetimeDB/
git commit -m "feat: add hello-world mod (server) — table, reducer, recipe seed, bindings"
```

---

## Chunk 3: Hello World Client Mod + Smoke Test

### Task 19: Create HelloWorldClientMod autoload

This is a Godot Node autoload. Its `_Ready` calls `ModManager.Register(this)`. When `WorldManager` calls `InitializeAll`, this mod instantiates the `HelloWorldPanel` and adds it to the scene.

**Files:**
- Create: `client/mods/hello-world/HelloWorldClientMod.cs`

- [ ] **Step 1: Create the file**

```csharp
// client/mods/hello-world/HelloWorldClientMod.cs
using Godot;

namespace SandboxRPG;

/// <summary>
/// Godot autoload for the hello-world client mod.
/// Registers with ModManager in _Ready (autoloads run before scene nodes).
/// WorldManager._Ready() then calls ModManager.InitializeAll, which triggers Initialize().
/// </summary>
public partial class HelloWorldClientMod : Node, IClientMod
{
    public string ModName => "hello-world";

    public override void _Ready()
    {
        ModManager.Register(this);
    }

    public void Initialize(Node sceneRoot)
    {
        var panel = GD.Load<PackedScene>("res://mods/hello-world/ui/HelloWorldPanel.tscn").Instantiate();
        sceneRoot.AddChild(panel);
    }
}
```

---

### Task 20: Create HelloWorldPanel script

**Files:**
- Create: `client/mods/hello-world/ui/HelloWorldPanel.cs`

- [ ] **Step 1: Create the file**

```csharp
// client/mods/hello-world/ui/HelloWorldPanel.cs
using Godot;

namespace SandboxRPG;

/// <summary>
/// Small HUD label showing a greeting from the server.
/// Calls SayHello on connect; updates the label when the server row arrives.
/// </summary>
public partial class HelloWorldPanel : Control
{
    private Label _label = null!;

    public override void _Ready()
    {
        _label = GetNode<Label>("Label");
        _label.Text = "Hello from HelloWorld Mod!";

        // Subscribe to the hello_world_message table
        var conn = GameManager.Instance.Conn!;
        conn.Db.HelloWorldMessage.OnInsert += (_, row)    => OnMessageChanged(row);
        conn.Db.HelloWorldMessage.OnUpdate += (_, _, row) => OnMessageChanged(row);

        // Call reducer once connected
        if (GameManager.Instance.IsConnected)
            Reducers.SayHello("Hello from HelloWorld Mod!");
        else
            GameManager.Instance.Connected += OnConnected;
    }

    private void OnConnected() => Reducers.SayHello("Hello from HelloWorld Mod!");

    public override void _ExitTree()
    {
        GameManager.Instance.Connected -= OnConnected;
    }

    private void OnMessageChanged(HelloWorldMessage row)
    {
        if (row.PlayerId == GameManager.Instance.LocalIdentity)
            _label.Text = row.Message;
    }
}
```

---

### Task 21: Create HelloWorldPanel scene

**Files:**
- Create: `client/mods/hello-world/ui/HelloWorldPanel.tscn`

- [ ] **Step 1: Create the scene file**

```
[gd_scene load_steps=2 format=3]

[ext_resource type="Script" path="res://mods/hello-world/ui/HelloWorldPanel.cs" id="1"]

[node name="HelloWorldPanel" type="Control"]
layout_mode = 3
anchors_preset = 0
offset_right = 320.0
offset_bottom = 40.0
script = ExtResource("1")

[node name="Label" type="Label" parent="."]
layout_mode = 0
offset_left = 10.0
offset_top = 8.0
offset_right = 310.0
offset_bottom = 36.0
text = "Hello from HelloWorld Mod!"
```

> Note: Godot will assign a `uid` the first time it opens this file. That is normal — commit whatever Godot generates.

---

### Task 22: Register HelloWorldClientMod as Godot autoload

**Files:**
- Modify: `client/project.godot`

- [ ] **Step 1: Add after ModManager in the `[autoload]` section**

```ini
[autoload]

ModManager="*res://scripts/mods/ModManager.cs"
HelloWorldClientMod="*res://mods/hello-world/HelloWorldClientMod.cs"
GameManager="*res://scripts/networking/GameManager.cs"
SceneRouter="*res://scripts/networking/SceneRouter.cs"
UIManager="*res://scripts/ui/UIManager.cs"
```

Order matters: `ModManager` must come before `HelloWorldClientMod` so `Instance` is set when `Register` is called.

---

### Task 23: Verify client builds

- [ ] **Step 1: Build**

```bash
cd client && dotnet build SandboxRPG.csproj
```

Expected: `Build succeeded. 0 Error(s)`

If you get `HelloWorldMessage does not exist` errors, bindings were not regenerated in Task 17 — re-run the `spacetime generate` command.

---

### Task 24: End-to-end smoke test

- [ ] **Step 1: Start SpacetimeDB (if not running)**

```bash
spacetime start --in-memory
```

- [ ] **Step 2: Re-login (required after restart)**

```bash
spacetime logout && spacetime login --server-issued-login local --no-browser
```

- [ ] **Step 3: Publish the server module**

```bash
cd server && spacetime publish -b bin/Release/net8.0/wasi-wasm/AppBundle/StdbModule.wasm
```

Expected: `Published successfully.`

- [ ] **Step 3b: Verify server seed logs**

```bash
spacetime logs sandbox-rpg
```

Look for these lines:
```
[ModLoader] Seeding mod: hello-world v1.0.0
[HelloWorldMod] Seeded.
```

- [ ] **Step 4: Launch the game**

```bash
"C:\Users\Jonas\AppData\Local\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.6.1-stable_mono_win64\Godot_v4.6.1-stable_mono_win64.exe" --path client/
```

- [ ] **Step 5: Verify**

After connecting and entering the game, a small label should appear in the top-left corner reading **"Hello from HelloWorld Mod!"**. The Godot output panel should show `[ModManager] Initializing: hello-world`.

In the Crafting UI (`C` key), there should be a recipe for `hello_item` requiring 1 wood.

---

### Task 25: Commit client mod

- [ ] **Step 1: Commit**

```bash
git add client/mods/hello-world/HelloWorldClientMod.cs
git add client/mods/hello-world/ui/HelloWorldPanel.cs
git add client/mods/hello-world/ui/HelloWorldPanel.tscn
git add client/project.godot
git commit -m "feat: add hello-world mod (client) — HUD panel, SayHello on connect"
```

---

## Adding Future Mods (Reference)

To create a new mod (e.g. `currency`):

1. Copy `mods/hello-world/` to `mods/currency/`
2. Rename classes (`HelloWorldModImpl` → `CurrencyModImpl`, etc.)
3. Update `mod.json` name, version, description
4. Add server line to `server/StdbModule.csproj`: `<Compile Include="../mods/currency/server/**/*.cs" />`
5. Run `spacetime build` + `spacetime generate` (if tables/reducers changed)
6. Add client autoload to `client/project.godot`: `CurrencyClientMod="*res://mods/currency/CurrencyClientMod.cs"`
7. Build client: `cd client && dotnet build SandboxRPG.csproj`

For a mod that depends on `currency`, set in its `HelloWorldModImpl` (or equivalent):
```csharp
public string[] Dependencies => new[] { "currency" };
```
`ModLoader` will seed `currency` before the dependent mod.
