# SandboxRPG

A multiplayer 3D sandbox survival game built with **Godot 4.6** and **SpacetimeDB 2.0**.

Players share a persistent world: gather resources, craft items, and build structures in real time with full server-authoritative logic.

---

## Tech Stack

| Layer | Technology |
|---|---|
| Game engine | Godot 4.6.1 Mono (C#) |
| Multiplayer backend | SpacetimeDB 2.0 (local / cloud) |
| Server module | C# compiled to WASM (net8.0/wasi-wasm) |
| Client SDK | SpacetimeDB.ClientSDK 2.0.1 |

---

## Project Structure

```
GodotGame/
├── client/                  Godot project
│   ├── scenes/
│   │   └── Main.tscn        Entry scene
│   ├── scripts/
│   │   ├── networking/      GameManager (STDB connection) + generated bindings
│   │   ├── player/          PlayerController, RemotePlayer
│   │   ├── world/           WorldManager, InteractionSystem
│   │   ├── building/        BuildSystem
│   │   └── ui/              HUD, InventoryUI, CraftingUI, ChatUI
│   └── assets/              Models, textures, materials, shaders (WIP)
│
├── server/                  SpacetimeDB module (compiled to WASM)
│   ├── Tables.cs            Table definitions (Player, Item, Structure…)
│   ├── Lifecycle.cs         Init, ClientConnected, ClientDisconnected + seed data
│   ├── PlayerReducers.cs    SetName, MovePlayer, SendChat
│   ├── InventoryReducers.cs PickupItem, DropItem, MoveItemToSlot
│   ├── CraftingReducers.cs  CraftItem
│   ├── BuildingReducers.cs  PlaceStructure, RemoveStructure
│   └── StdbModule.csproj
│
└── start-dev.bat            One-click dev environment startup (Windows)
```

---

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8) (`dotnet --version` → 8.x)
- [SpacetimeDB CLI 2.0+](https://spacetimedb.com/install) (`spacetime --version`)
- [Godot 4.6.1 Mono](https://godotengine.org/download)

> The WASI workload is installed automatically by `spacetime build` on first run.

---

## Quick Start

### 1. Start the dev environment

Double-click **`start-dev.bat`**, or run each step manually:

```bat
:: Start SpacetimeDB (in-memory, resets on restart)
spacetime start --in-memory

:: Authenticate with local server
spacetime logout
spacetime login --server-issued-login local --no-browser

:: Build and publish the server module
cd server
spacetime build
spacetime publish -b bin\Release\net8.0\wasi-wasm\AppBundle\StdbModule.wasm
cd ..

:: Open the Godot editor
"<path-to-godot>" --path client --editor
```

### 2. Run the game

Press **F5** in the Godot editor (or Project → Run).

The game auto-connects to `http://127.0.0.1:3000` on startup.

---

## Development Workflow

### After changing server code

```bat
cd server
spacetime build
spacetime publish -b bin\Release\net8.0\wasi-wasm\AppBundle\StdbModule.wasm
```

> **Note:** The server runs in-memory by default. All data is lost when the SpacetimeDB process restarts. Re-publish the module after restarting to re-seed recipes and world items.

### After changing table definitions

Regenerate the C# client bindings:

```bat
cd server
spacetime generate --lang csharp ^
  --out-dir ..\client\scripts\networking\SpacetimeDB ^
  --bin-path bin\Release\net8.0\wasi-wasm\AppBundle\StdbModule.wasm
```

Then rebuild in Godot (**Ctrl+Shift+B**).

---

## Controls

| Key | Action |
|---|---|
| WASD | Move |
| Mouse | Look |
| Shift | Sprint |
| Space | Jump |
| E | Interact / Pick up |
| I | Inventory |
| C | Crafting |
| B | Build mode |
| [ / ] | Cycle structure (build mode) |
| R | Rotate structure (build mode) |
| LMB | Place structure (build mode) |
| Escape | Release mouse cursor |

---

## Architecture Notes

- **Server-authoritative**: all game state lives in SpacetimeDB. The client sends actions (reducers) and displays what the server tells it.
- **No scenes for players**: players and world items are spawned entirely in code by `WorldManager` in response to subscription updates.
- **Ingredient format**: crafting ingredients are stored as `"wood:4,stone:2"` strings and parsed server-side.
- **Auth token**: stored in `user://spacetimedb_auth.token` and reused across sessions.
