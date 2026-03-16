# Adding a New Mod to SandboxRPG

Mods are compile-time content packages. They add new tables, reducers, and client UI without touching core game files. Enable/disable = include/exclude from the build.

---

## File Layout

```
mods/<mod-name>/
├── mod.json                          # metadata (not parsed at runtime)
└── server/
    ├── <PascalName>Tables.cs         # [Table] definitions
    ├── <PascalName>Reducers.cs       # [Reducer] definitions
    └── <PascalName>Mod.cs            # IMod registration + Seed()

client/mods/<mod-name>/
├── <PascalName>ClientMod.cs          # Godot autoload Node, IClientMod
└── ui/
    ├── <PascalName>Panel.tscn
    └── <PascalName>Panel.cs
```

---

## Step 1 — Copy the Template

```bash
cp -r mods/hello-world mods/<mod-name>
cp -r client/mods/hello-world client/mods/<mod-name>
```

---

## Step 2 — Update mod.json

`mods/<mod-name>/mod.json`:
```json
{
  "name": "<mod-name>",
  "version": "1.0.0",
  "description": "What this mod does.",
  "dependencies": [],
  "author": "internal"
}
```

`mod.json` is **human-readable metadata only** — not parsed at runtime. Dependencies are declared here for documentation and in the server `IMod` class for enforcement.

---

## Step 3 — Server Files

All server files **must** use:
```csharp
namespace SandboxRPG.Server;
public static partial class Module { ... }
```

### `<PascalName>Tables.cs` — Define your tables

```csharp
using SpacetimeDB;

namespace SandboxRPG.Server;

public static partial class Module
{
    [Table(Name = "player_currency", Public = true)]
    public partial struct PlayerCurrency
    {
        [PrimaryKey]
        public Identity PlayerId;
        public ulong Amount;
    }
}
```

Rules:
- Table name: `snake_case` string
- Primary key: usually `Identity PlayerId` (one row per player) or `[AutoInc][PrimaryKey] ulong Id`
- `Public = true` so clients can subscribe

### `<PascalName>Reducers.cs` — Define your reducers

```csharp
using SpacetimeDB;

namespace SandboxRPG.Server;

public static partial class Module
{
    [Reducer]
    public static void AwardCurrency(ReducerContext ctx, ulong amount)
    {
        var existing = ctx.Db.PlayerCurrency.PlayerId.Find(ctx.Sender);
        if (existing is not null)
        {
            var row = existing.Value;   // .Value is correct — server types are structs
            row.Amount += amount;
            ctx.Db.PlayerCurrency.PlayerId.Update(row);
        }
        else
        {
            ctx.Db.PlayerCurrency.Insert(new PlayerCurrency
            {
                PlayerId = ctx.Sender,
                Amount   = amount,
            });
        }
    }
}
```

### `<PascalName>Mod.cs` — Registration and seed data

```csharp
using SpacetimeDB;
using System;
using SandboxRPG.Server.Mods;

namespace SandboxRPG.Server;

public static partial class Module
{
    // Static field on Module guarantees registration fires before Init
    private static readonly CurrencyModImpl _currencyMod = new();

    private sealed class CurrencyModImpl : IMod
    {
        public CurrencyModImpl() => ModLoader.Register(this);

        public string Name    => "currency";
        public string Version => "1.0.0";

        // List mods that must seed BEFORE this one
        public string[] Dependencies => Array.Empty<string>();

        public void Seed(ReducerContext ctx)
        {
            // Insert any initial data here (recipes, config rows, etc.)
            Log.Info("[CurrencyMod] Seeded.");
        }
    }
}
```

---

## Step 4 — Enable Server Mod

`server/StdbModule.csproj` — add inside the `<!-- Mods -->` ItemGroup:

```xml
<!-- Mods: add one line per enabled mod -->
<ItemGroup>
  <Compile Include="../mods/hello-world/server/**/*.cs" />
  <Compile Include="../mods/currency/server/**/*.cs" />   <!-- add this -->
</ItemGroup>
```

**Disabling**: remove the line and rebuild.

---

## Step 5 — Build Server and Regenerate Bindings

Always do these two commands in order after changing server tables or reducers:

```bash
# 1. Build the WASM module
cd server && spacetime build

# 2. Regenerate client bindings
cd server && spacetime generate --lang csharp \
  --out-dir ../client/scripts/networking/SpacetimeDB \
  --bin-path bin/Release/net8.0/wasi-wasm/AppBundle/StdbModule.wasm
```

New files will appear in `client/scripts/networking/SpacetimeDB/`:
- `Types/PlayerCurrency.g.cs`
- `Tables/PlayerCurrency.g.cs`
- `Reducers/AwardCurrency.g.cs`

---

## Step 6 — Client Files

### `<PascalName>ClientMod.cs` — Godot autoload

```csharp
using Godot;

namespace SandboxRPG;

public partial class CurrencyClientMod : Node, IClientMod
{
    public string ModName => "currency";

    public override void _Ready()
    {
        ModManager.Register(this);
    }

    public void Initialize(Node sceneRoot)
    {
        var panel = GD.Load<PackedScene>("res://mods/currency/ui/CurrencyPanel.tscn").Instantiate();
        sceneRoot.AddChild(panel);
    }
}
```

### `ui/<PascalName>Panel.cs` — HUD panel

```csharp
using Godot;
using SpacetimeDB.Types;

namespace SandboxRPG;

public partial class CurrencyPanel : Control
{
    private Label _label = null!;

    public override void _Ready()
    {
        _label = GetNode<Label>("Label");
        _label.Text = "0 coins";

        var conn = GameManager.Instance.Conn!;
        conn.Db.PlayerCurrency.OnInsert += OnInsert;
        conn.Db.PlayerCurrency.OnUpdate += OnUpdate;
    }

    public override void _ExitTree()
    {
        if (GameManager.Instance.Conn is { } conn)
        {
            conn.Db.PlayerCurrency.OnInsert -= OnInsert;
            conn.Db.PlayerCurrency.OnUpdate -= OnUpdate;
        }
    }

    private void OnInsert(EventContext _, PlayerCurrency row) => Refresh(row);
    private void OnUpdate(EventContext _, PlayerCurrency _old, PlayerCurrency row) => Refresh(row);

    private void Refresh(PlayerCurrency row)
    {
        if (row.PlayerId == GameManager.Instance.LocalIdentity)
            _label.Text = $"{row.Amount} coins";
    }
}
```

> **Important:** Always use named methods (not inline lambdas) for event subscriptions so you can unsubscribe them in `_ExitTree`.

### `ui/<PascalName>Panel.tscn`

Minimal scene file — Godot will assign a `uid` on first open:

```
[gd_scene load_steps=2 format=3]

[ext_resource type="Script" path="res://mods/currency/ui/CurrencyPanel.cs" id="1"]

[node name="CurrencyPanel" type="Control"]
offset_right = 200.0
offset_bottom = 40.0
script = ExtResource("1")

[node name="Label" type="Label" parent="."]
offset_left = 10.0
offset_top = 8.0
offset_right = 190.0
offset_bottom = 36.0
text = "0 coins"
```

---

## Step 7 — Register Client Mod as Autoload

`client/project.godot` — add in `[autoload]` section, after `ModManager`, before `GameManager`:

```ini
[autoload]

ModManager="*res://scripts/mods/ModManager.cs"
CurrencyClientMod="*res://mods/currency/CurrencyClientMod.cs"   ; add this
GameManager="*res://scripts/networking/GameManager.cs"
...
```

**Order matters:** `ModManager` must come before all mod autoloads.

---

## Step 8 — Verify Build

```bash
cd client && dotnet build SandboxRPG.csproj
```

Expected: `0 Error(s)`

---

## Step 9 — Commit

```bash
git add mods/<mod-name>/
git add server/StdbModule.csproj
git add client/mods/<mod-name>/
git add client/project.godot
git add client/scripts/networking/SpacetimeDB/
git commit -m "feat: add <mod-name> mod"
```

---

## Declaring Dependencies

If your mod depends on another (e.g. `shop` depends on `currency`):

**`mod.json`:**
```json
{ "dependencies": ["currency"] }
```

**`ShopMod.cs`:**
```csharp
public string[] Dependencies => new[] { "currency" };
```

`ModLoader` will seed `currency` before `shop` automatically.

---

## Troubleshooting

| Problem | Fix |
|---|---|
| `partial struct X` compile error | Check file uses `namespace SandboxRPG.Server;` + `public static partial class Module` |
| `conn.Db.MyTable does not exist` | Run `spacetime generate` after `spacetime build` |
| Mod panel not appearing | Check autoload entry in `project.godot` and that `ModManager` comes first |
| Mod not seeded on server | Check static field `private static readonly MyModImpl _mod = new()` is inside `partial class Module` |
| `existing.Value` error on client | On **client**, generated types are classes — access directly. On **server**, they're structs — use `.Value` |

---

## Planned Future Mods

| Mod | Depends On | Status |
|---|---|---|
| `currency` | — | planned |
| `shop` | `currency` | planned |
| `casino` | `currency` | planned |
