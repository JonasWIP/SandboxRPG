# SandboxRPG — Claude Context

Multiplayer 3D sandbox survival game. SpacetimeDB 2.0 server (C# WASM) + Godot 4.6.1 C# client.

---

## Key Paths

| What | Path |
|---|---|
| Godot project | `client/` |
| Server module | `server/` |
| GameManager (networking) | `client/scripts/networking/GameManager.cs` |
| Generated STDB bindings | `client/scripts/networking/SpacetimeDB/` |
| Entry scene | `client/scenes/Main.tscn` |
| Dev startup script | `start-dev.bat` |

**Godot executable:**
`C:\Users\Jonas\AppData\Local\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.6.1-stable_mono_win64\Godot_v4.6.1-stable_mono_win64.exe`

---

## Essential Commands

```bash
# Build client (check for errors)
cd client && dotnet build SandboxRPG.csproj

# Build + publish server module
cd server && spacetime build
spacetime publish -b bin/Release/net8.0/wasi-wasm/AppBundle/StdbModule.wasm

# Regenerate client bindings (after changing tables/reducers)
cd server && spacetime generate --lang csharp \
  --out-dir ../client/scripts/networking/SpacetimeDB \
  --bin-path bin/Release/net8.0/wasi-wasm/AppBundle/StdbModule.wasm

# Re-authenticate (needed after SpacetimeDB server restart)
spacetime logout && spacetime login --server-issued-login local --no-browser
```

---

## SpacetimeDB 2.0 API — Critical Notes

### Server (SpacetimeDB.Runtime)
- `ctx.Sender` (not `ctx.CallerIdentity`)
- All tables PascalCase: `ctx.Db.Player`, `ctx.Db.InventoryItem`, etc.
- Lifecycle reducers: names must NOT start with `On` (STDB0010 error)
  - Use `ClientConnected` / `ClientDisconnected`, not `OnClientConnected`
- Delete requires fetching the row first: `ctx.Db.Table.Delete(row)` (no delete-by-key)
- Timestamp: `(ulong)((DateTimeOffset)ctx.Timestamp).ToUnixTimeMilliseconds() * 1000`
- `global.json` pins .NET 8 SDK for WASM build (server requires net8.0/wasi-wasm)

### Client (SpacetimeDB.ClientSDK 2.0.1)
- Builder: `.WithDatabaseName(name)` (not `.WithModuleName`)
- Types (`Player`, `WorldItem`, etc.) are **classes**, not structs — no `.Value` needed on nullable returns
- `GetLocalPlayer()` returns `Player?` — access properties directly, no `.Value`
- Subscription: `.SubscribeToAllTables()` (no manual SQL queries needed)
- `IsConnected` property needs `new` keyword to shadow `GodotObject.IsConnected(StringName, Callable)`

---

## Server File Layout

```
server/
├── Tables.cs            All [Table] struct definitions
├── Lifecycle.cs         Init, ClientConnected, ClientDisconnected, seed data
├── PlayerReducers.cs    SetName, MovePlayer, SendChat
├── InventoryReducers.cs PickupItem, DropItem, MoveItemToSlot + ParseIngredients()
├── CraftingReducers.cs  CraftItem
├── BuildingReducers.cs  PlaceStructure, RemoveStructure
└── StdbModule.csproj    net8.0/wasi-wasm, SpacetimeDB.Runtime 2.0.*
```

All files use `public static partial class Module` — C# partial classes allow the split.

---

## Client Script Layout

```
client/scripts/
├── networking/
│   ├── GameManager.cs          Singleton autoload — connection, signals, reducer calls
│   └── SpacetimeDB/            Auto-generated (spacetime generate) — DO NOT hand-edit
├── player/
│   ├── PlayerController.cs     Local player movement + position sync
│   └── RemotePlayer.cs         Interpolated remote player visuals
├── world/
│   ├── WorldManager.cs         Spawns/despawns players, world items, structures
│   └── InteractionSystem.cs    Raycast-based pickup detection
├── building/
│   └── BuildSystem.cs          Ghost preview + grid snapping + placement
└── ui/
    ├── HUD.cs                   Health/stamina bars, connection status
    ├── InventoryUI.cs           Item list, drop buttons (toggle: I)
    ├── CraftingUI.cs            Recipe list, craft buttons (toggle: C)
    └── ChatUI.cs                Chat input/display
```

---

## Known Gotchas

- **In-memory server**: all data lost on restart. Re-login and re-publish after restarting SpacetimeDB.
- **WASI SDK**: `spacetime build` auto-installs it on first run. Can fail on slow networks — retry.
- **wasm-opt**: not installed (non-critical, just omits WASM optimisation pass).
- **Generated bindings**: `client/scripts/networking/SpacetimeDB/` is committed so the project builds from checkout. Regenerate after any table/reducer changes.
- **Godot autoload**: `GameManager` is registered as an autoload in `project.godot`. If it fails to compile, the whole project fails to open — check `dotnet build` first.
- **Ingredient format**: `"wood:4,stone:2"` — parsed by `InventoryReducers.ParseIngredients()`.
