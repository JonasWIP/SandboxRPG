# Casino Mod Design Spec
**Date:** 2026-03-15
**Status:** Approved
**Project:** SandboxRPG — Casino Expansion Pack

---

## Overview

A casino expansion mod for SandboxRPG introducing a currency system (Copper/Silver/Gold) and five casino minigames: slot machine, multiplayer blackjack, coin pusher, and two arcade machines (reaction + pattern). All game logic is server-authoritative. Game state renders directly on machine surfaces in 3D — no full-screen UI panels. This mod is the first use of a new mod/expansion framework that supports future content packs.

---

## 1. Mod Framework

### 1.1 Architecture: Folder + Compile Symbols + Runtime DB Toggle

Mods live in isolated folders with compile symbols gating their inclusion. A `ModConfig` DB table handles runtime enabling/disabling without rebuilding.

**Folder structure:**
```
server/
  mods/
    currency/
      Tables.cs
      Reducers.cs
      Lifecycle.cs
      mod.json
    casino/
      Tables.cs
      Reducers.cs
      Lifecycle.cs
      mod.json

client/
  mods/
    currency/
      CurrencyHUD.cs
      ExchangeUI.cs
      mod.json
    casino/
      CasinoUI.cs
      SlotMachineUI.cs
      BlackjackUI.cs
      CoinPusherUI.cs
      ArcadeUI.cs
      mod.json
      models/
        slot_machine.glb
        blackjack_table.glb
        coin_pusher.glb
        arcade_reaction.glb
        arcade_pattern.glb
        exchange_machine.glb
        casino_building.glb
```

### 1.2 Compile Symbols

```xml
<!-- server/server.csproj and client/SandboxRPG.csproj -->
<DefineConstants>MOD_CURRENCY;MOD_CASINO</DefineConstants>
```

Each mod file wraps its content in `#if MOD_CASINO ... #endif`. Removing the symbol fully excludes the mod from the binary.

### 1.3 mod.json Manifest

```json
{
  "id": "casino",
  "version": "1.0.0",
  "displayName": "Casino Pack",
  "dependencies": ["currency"]
}
```

### 1.4 ModConfig Table (Base Game — Always Compiled)

```
ModConfig: { ModId (PK string), Enabled bool, Version string, Dependencies string }
```

Seeded rows:
```
{ "currency", true, "1.0.0", "" }
{ "casino",   true, "1.0.0", "currency" }
```

`SetModEnabled(modId, enabled)` reducer — admin only, validates dependency graph before toggling. Cannot disable `currency` while `casino` is enabled. Cannot enable `casino` if `currency` is disabled.

### 1.5 Client Startup Flow

```
SubscriptionApplied
  → ModManager.Initialize()
  → Read ModConfig table, build dependency graph (topological sort)
  → For each enabled mod in order:
      → Register mod UI with UIManager
      → Subscribe to mod-specific table callbacks
  → If compile symbol missing but mod is marked enabled: log warning, skip gracefully
```

### 1.6 Adding Future Mods

1. Create `server/mods/<name>/` + `client/mods/<name>/` with `mod.json`
2. Add `#define MOD_<NAME>` to both `.csproj` files
3. Insert `ModConfig` row in server `Init`
4. No changes to base game required

---

## 2. Currency System

### 2.1 Design Principles

Only Copper is stored. Silver and Gold are display-only conversions:
- 1 Silver = 100 Copper
- 1 Gold = 10,000 Copper

Storing a single integer avoids rounding bugs and simplifies all arithmetic.

### 2.2 Server Tables

```
CurrencyBalance:
  PlayerId    Identity (PK)
  Copper      ulong

CurrencyTransaction:
  Id          ulong (AutoInc PK)
  PlayerId    Identity
  Amount      long  (positive = credit, negative = debit)
  Reason      string
  Timestamp   ulong
```

### 2.3 Physical Coin Items

Coins also exist as inventory items (tradeable, droppable, pickable):
```
"coin_copper"  → 1 Copper each
"coin_silver"  → 100 Copper each
"coin_gold"    → 10,000 Copper each
```

The Exchange Machine converts physical coins ↔ `CurrencyBalance`.

### 2.4 Reducers

```
GrantStartingBalance(identity)          → 500 Copper on first connect (idempotent)
ExchangeResources(resourceType, qty)    → burn inventory items, credit Copper
ConvertCurrency(fromType, amount)       → e.g. 100 copper → 1 silver item
CreditCoins(identity, amount, reason)   → internal, called by casino reducers
DebitCoins(identity, amount, reason)    → validates balance ≥ amount before deducting
```

### 2.5 Starting Economy

**Starting balance:** 500 Copper on first connect.

**Resource exchange rates:**
| Resource | Yield |
|---|---|
| 10 wood | 5 Copper |
| 5 stone | 5 Copper |
| 1 iron | 20 Copper |

**Future:** Coins drop from world events and loot chests (separate content pack).

### 2.6 Client UI

`CurrencyHUD.cs` — small overlay added to the existing HUD (top-right corner):
```
🟤 450  ⚪ 4  🟡 0
```
Updates live from `CurrencyBalance` table callbacks.

---

## 3. Casino Game Sessions

All game logic is server-authoritative. Clients send intents and react to table updates. Machine rows are keyed by `MachineId` = `PlacedStructure.Id`.

### 3.1 Slot Machine

```
SlotSession:
  MachineId   ulong (PK)
  PlayerId    Identity?
  Reels       string       ("🍒|🍋|⭐")
  State       enum         (Idle, Spinning, Result)
  Bet         ulong
  WinAmount   ulong
```

**Reducers:** `SpinSlot(machineId, betAmount)`
Server rolls RNG, applies payout table, debits bet, credits winnings atomically. Result is immediate (no persistent spinning state).

**Payout table (configurable):**
| Match | Multiplier |
|---|---|
| 3× Gold (💛) | 100× |
| 3× Star (⭐) | 20× |
| 3× Cherry (🍒) | 10× |
| 2× any match | 2× |
| No match | 0 |

### 3.2 Blackjack (Multiplayer)

```
BlackjackGame:
  MachineId   ulong (PK)
  State       enum  (WaitingForPlayers, Dealing, PlayerTurns, DealerTurn, Payout)
  DealerHand  string   ("AS,10H")
  Deck        string   (shuffled draw pile, comma-separated)
  RoundId     uint

BlackjackSeat:
  Id          ulong (AutoInc PK)
  MachineId   ulong
  SeatIndex   byte  (0–3)
  PlayerId    Identity
  Hand        string   ("7D,KS")
  Bet         ulong
  State       enum  (Waiting, Acting, Standing, Bust, Done)
```

**Reducers:** `JoinBlackjack(machineId, seatIndex)`, `PlaceBet(machineId, amount)`, `HitBlackjack(machineId)`, `StandBlackjack(machineId)`, `LeaveBlackjack(machineId)`

Round advances automatically when all seats reach Standing/Bust/Done. Dealer draws to 17+. Standard blackjack rules (no split/double-down in v1).

### 3.3 Coin Pusher

```
CoinPusherState:
  MachineId         ulong (PK)
  CoinCount         uint
  LastPusherId      Identity?
  LastPushTime      ulong
  JackpotThreshold  uint
```

**Reducer:** `PushCoin(machineId, amount)` — increments `CoinCount`. If `CoinCount ≥ JackpotThreshold`, pays out accumulated value to triggering player and resets.

**Client visuals:** `RigidBody3D` coin objects spawned client-side when `CoinCount` increases. Physics simulated locally. Coin pile rebuilt from `CoinCount` on join.

### 3.4 Arcade — Reaction Machine

```
ArcadeSession:
  MachineId      ulong (PK)
  PlayerId       Identity?
  GameType       enum  (Reaction, Pattern)
  State          enum  (Idle, Active, Judging)
  Bet            ulong
  ChallengeData  string
  StartTime      ulong
```

**Reaction game flow:**
1. `StartArcade(machineId, bet)` — server sets `StartTime`, random target window stored in `ChallengeData`
2. Client shows animated needle on machine screen
3. `ArcadeInput(machineId, clientTimestamp)` — server validates `clientTimestamp - StartTime` against window (±200ms latency tolerance)
4. Win → credits bet × multiplier; miss → loses bet

### 3.5 Arcade — Pattern Machine

Same `ArcadeSession` table, `GameType = Pattern`.

1. `StartArcade(machineId, bet)` — server generates sequence (e.g. `"RRBLG"`) stored in `ChallengeData`, sets countdown
2. Client shows sequence on machine's colored button display
3. `ArcadeInput(machineId, sequence)` — server compares against `ChallengeData`
4. Correct sequence within time limit → wins; wrong or timeout → loses

---

## 4. World Integration

### 4.1 New Structure Types

Extends the existing `PlacedStructure` system. New `StructureType` strings:

```
casino_building          (pre-seeded POI, ~20×10×20 units)
casino_slot_machine      (1×2×1 footprint)
casino_blackjack_table   (3×1×2 footprint, 4 seats)
casino_coin_pusher       (2×2×1 footprint)
casino_arcade_reaction   (1×2×1 footprint)
casino_arcade_pattern    (1×2×1 footprint)
casino_exchange          (1×2×1 footprint)
```

### 4.2 Pre-Seeded Casino POI

`server/mods/casino/Lifecycle.cs` seeds on `Init` (if casino mod enabled):
- One `casino_building` at world position (50, 0, 50)
- One of each machine type placed inside as `PlacedStructure` rows

### 4.3 Craftable Recipes

Seeded via `SeedRecipes()` in casino `Lifecycle.cs`:
```
casino_slot_machine:     10 iron + 5 stone
casino_blackjack_table:  8 wood + 4 iron
casino_coin_pusher:      6 iron + 10 stone
casino_arcade_reaction:  8 iron + 2 stone
casino_arcade_pattern:   8 iron + 2 stone
casino_exchange:         4 iron + 4 stone
```

### 4.4 Interaction

`InteractionSystem` extended with an `IInteractable` interface. Casino mod registers handlers per structure type string. Player looks at machine + presses `E` → handler invoked → triggers in-world UI or reducer call.

---

## 5. Client UI Architecture

### 5.1 Core Principle: World-Space Rendering

Game state renders directly on machine surfaces. No full-screen panels. Minimal 2D overlays only for input that cannot be shown in-world (bet amounts, seat selection).

### 5.2 Per-Machine Rendering

| Machine | In-World Display | 2D Overlay |
|---|---|---|
| Slot machine | Reels on screen mesh via SubViewport texture | Bet selector (small popup) |
| Blackjack table | 3D card `MeshInstance3D` objects on felt surface | Bet input + seat prompt |
| Coin pusher | `RigidBody3D` coin physics objects | None |
| Arcade reaction | Animated needle on screen mesh | None |
| Arcade pattern | Lit colored button geometry | None |
| Exchange machine | Balance + rates on screen mesh | Amount input |

### 5.3 SubViewport Screen Texture

Machines with display screens use:
```
MachineNode
  ├── MeshInstance3D (body)
  ├── MeshInstance3D (screen) ← ViewportTexture applied
  └── SubViewport
        └── Control (game state UI rendered here)
```

The `Control` inside `SubViewport` is a lightweight scene — no UIManager involvement. It subscribes directly to table callbacks and updates its own display.

### 5.4 3D Card Objects (Blackjack)

Cards are `MeshInstance3D` nodes with a `StandardMaterial3D` using a card sprite sheet texture. Server pushes hand updates → client spawns/moves card meshes on the table surface. All players see the same card positions in 3D.

### 5.5 Physics Coins (Coin Pusher)

On `CoinPusherState.CoinCount` increase:
- Client spawns `RigidBody3D` coin at the push entry point
- Godot physics handles the rest — coins stack, fall, jostle naturally
- On join, client rebuilds approximate coin pile from current `CoinCount`
- No coin physics state is synced — it is purely local visual simulation

### 5.6 Model Requirements for 3D Artists

Casino machine `.glb` models must include:
- A dedicated **screen mesh surface** (named `Screen`) for SubViewport texture application
- **Physical button geometry** as separate mesh regions for raycast interaction
- Blackjack table must have a flat **felt zone** mesh for card placement
- Coin pusher must have an open **platform area** for coin physics

Placeholder models (colored primitive boxes) are used until final `.glb` files are delivered.

---

## 6. Data Flow Summary

```
Player presses E on machine
  → InteractionSystem raycast hits machine Area3D
  → Casino mod handler invoked
  → Small 2D input popup shown (bet/seat) if needed
  → Client calls reducer (SpinSlot, JoinBlackjack, etc.)
  → Server validates, updates session table
  → Table OnUpdate callback fires on all clients
  → Machine's SubViewport Control updates display
  → (Blackjack) Card meshes spawn/move on table
  → (Coin pusher) RigidBody3D coins spawn
```

---

## 7. Implementation Phases

| Phase | Scope |
|---|---|
| 1 | Mod framework: folder structure, compile symbols, ModConfig table, ModManager client singleton |
| 2 | Currency mod: tables, reducers, CurrencyHUD, ExchangeUI |
| 3 | Casino building + exchange machine (world placement, placeholder models) |
| 4 | Slot machine (server session, SubViewport reels UI) |
| 5 | Blackjack (server session, 3D card objects, multiplayer seat flow) |
| 6 | Coin pusher (server session, RigidBody3D coin physics) |
| 7 | Arcade machines — reaction + pattern (server session, screen animation) |
| 8 | Swap placeholder models for final `.glb` assets |

---

## 8. Out of Scope (v1)

- Blackjack split / double-down
- Poker or other card games
- Real-money or external payment integration
- Persistent leaderboards
- Coin physics sync across players (local simulation only)
- Mobile/controller input for casino UI
