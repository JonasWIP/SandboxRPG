---
name: add-mod
description: Use when adding a new internal mod to SandboxRPG — creating mod files, enabling the mod in the build, regenerating bindings, and verifying the build.
user-invocable: true
---

# Add a New Mod to SandboxRPG

Ask the user for the mod name (e.g. `currency`) and any dependencies (e.g. `["hello-world"]`) if not already specified.

Then follow these steps exactly:

## 1. Copy the hello-world template

```bash
# From repo root
cp -r mods/hello-world mods/<mod-name>
```

Update `mods/<mod-name>/mod.json`:
- `"name"`: `"<mod-name>"`
- `"description"`: brief description
- `"dependencies"`: array of mod names this depends on (e.g. `["currency"]`), or `[]`

## 2. Rename server classes

In all three files under `mods/<mod-name>/server/`:

| Old name | New name |
|---|---|
| `HelloWorldTables.cs` | `<PascalName>Tables.cs` |
| `HelloWorldReducers.cs` | `<PascalName>Reducers.cs` |
| `HelloWorldMod.cs` | `<PascalName>Mod.cs` |

Inside each file, replace:
- `HelloWorldMessage` → your table struct name (e.g. `PlayerCurrency`)
- `HelloWorldModImpl` → `<PascalName>ModImpl`
- `hello_world_message` table name → your table name (e.g. `player_currency`)
- `"hello-world"` → `"<mod-name>"`
- `"Hello from HelloWorld Mod!"` → appropriate seed/log messages
- `hello_item` recipe → your seeded content (or remove the recipe seed entirely)
- `_helloWorldMod` field → `_<camelName>Mod`
- `[HelloWorldMod] Seeded.` log → `[<PascalName>Mod] Seeded.`

> All server files MUST stay inside `namespace SandboxRPG.Server;` and `public static partial class Module { }`.

## 3. Enable server mod in StdbModule.csproj

Open `server/StdbModule.csproj`. Inside the `<!-- Mods -->` ItemGroup, add:

```xml
<Compile Include="../mods/<mod-name>/server/**/*.cs" />
```

## 4. Build and regenerate bindings

```bash
cd server && spacetime build
cd server && spacetime generate --lang csharp \
  --out-dir ../client/scripts/networking/SpacetimeDB \
  --bin-path bin/Release/net8.0/wasi-wasm/AppBundle/StdbModule.wasm
```

## 5. Rename client files

In `client/mods/hello-world/` → copy to `client/mods/<mod-name>/`.

| Old name | New name |
|---|---|
| `HelloWorldClientMod.cs` | `<PascalName>ClientMod.cs` |
| `ui/HelloWorldPanel.cs` | `ui/<PascalName>Panel.cs` |
| `ui/HelloWorldPanel.tscn` | `ui/<PascalName>Panel.tscn` |

Inside the client files, replace:
- Class names, `ModName` property → `"<mod-name>"`
- `res://mods/hello-world/ui/HelloWorldPanel.tscn` → `res://mods/<mod-name>/ui/<PascalName>Panel.tscn`
- Table subscriptions → your new table type from generated bindings
- Reducer call → `conn.Reducers.<YourReducer>(...)`

## 6. Register client mod as autoload

Open `client/project.godot`. In the `[autoload]` section, add after `ModManager` and before `GameManager`:

```ini
<PascalName>ClientMod="*res://mods/<mod-name>/<PascalName>ClientMod.cs"
```

## 7. Verify build

```bash
cd client && dotnet build SandboxRPG.csproj
```

Expected: `0 Error(s)`

## 8. Commit

```bash
git add mods/<mod-name>/ server/StdbModule.csproj
git add client/mods/<mod-name>/ client/project.godot
git add client/scripts/networking/SpacetimeDB/
git commit -m "feat: add <mod-name> mod"
```

---

## Key facts (don't guess these)

- **Server files** live at `mods/<mod-name>/server/` — included via `.csproj` glob
- **Client files** live at `client/mods/<mod-name>/` — auto-compiled by Godot
- **Client mod** is a `partial class : Node, IClientMod` Godot autoload (not a plain class)
- **Reducers** are called via `conn.Reducers.<Name>(...)` (instance on DbConnection, not static)
- **Find() returns nullable struct** on server — use `existing.Value` to unwrap
- **Dependencies** declared in `mod.json` AND in `public string[] Dependencies => new[] { "currency" };` inside the server `IMod` impl — ModLoader seeds deps first
