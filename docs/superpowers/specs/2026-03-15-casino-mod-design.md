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
AdminList: { PlayerId (PK Identity) }
```

Seeded rows:
```
{ "currency", true, "1.0.0", "" }
{ "casino",   true, "1.0.0", "currency" }
```

`SetModEnabled(modId, enabled)` reducer — checks `ctx.Sender` exists in `AdminList` table. Validates dependency graph before toggling: cannot disable `currency` while `casino` is enabled; cannot enable `casino` if `currency` is disabled. The server owner populates `AdminList` via a `GrantAdmin(identity)` reducer that only succeeds when `AdminList` is empty (first-run bootstrap).

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

The Exchange Machine converts physical coin inventory items ↔ `CurrencyBalance`.

### 2.4 Reducers

**Public reducers (callable by clients):**
```
ExchangeResources(resourceType, qty)
  → Burns qty raw inventory items of resourceType.
  → qty is raw item count; batch sizes are hardcoded server-side:
      "wood":  batch=10, yield=5 Copper
      "stone": batch=5,  yield=5 Copper
      "iron":  batch=1,  yield=20 Copper
  → Must be a multiple of batch size; rejects otherwise.
  → Credits (qty / batchSize) × yield Copper via internal CreditCoins().

WithdrawCoins(denomination, amount)
  → denomination: "copper" | "silver" | "gold"
  → Moves value from wallet balance to physical inventory coin items (one-way).
  → "copper": debits amount Copper, grants amount coin_copper inventory items
  → "silver": debits amount×100 Copper, grants amount coin_silver inventory items
  → "gold":   debits amount×10000 Copper, grants amount coin_gold inventory items
  → Validates CurrencyBalance ≥ required Copper before acting.
  → Reverse direction (items → balance): use DepositCoins at the Exchange Machine.

DepositCoins(denomination, amount)
  → denomination: "copper" | "silver" | "gold"
  → Consumes amount coin_<denomination> items from caller's inventory.
  → Credits equivalent Copper to CurrencyBalance.
  → "copper": consumes amount coin_copper, credits amount Copper
  → "silver": consumes amount coin_silver, credits amount×100 Copper
  → "gold":   consumes amount coin_gold, credits amount×10000 Copper
  → Validates inventory has sufficient coin items before acting.
```

**Internal helper methods (no [Reducer] attribute — not callable by clients):**
```
static void GrantStartingBalance(ReducerContext ctx, Identity identity)
  → Called from ClientConnected lifecycle reducer.
  → Idempotent: inserts CurrencyBalance row only if none exists for identity.
  → Credits 500 Copper.

static void CreditCoins(ReducerContext ctx, Identity identity, ulong amount, string reason)
  → Inserts or updates CurrencyBalance (upsert pattern).
  → Appends CurrencyTransaction row.

static void DebitCoins(ReducerContext ctx, Identity identity, ulong amount, string reason)
  → Reads CurrencyBalance; throws if balance < amount.
  → Deducts amount, appends CurrencyTransaction row.
```

### 2.5 Starting Economy

**Starting balance:** 500 Copper on first connect (via `GrantStartingBalance` in `ClientConnected`).

**Resource exchange rates:**
| Resource | Batch size | Yield per batch |
|---|---|---|
| wood | 10 | 5 Copper |
| stone | 5 | 5 Copper |
| iron | 1 | 20 Copper |

**Future:** Coins drop from world events and loot chests (separate content pack).

### 2.6 Client UI

`CurrencyHUD.cs` — small overlay added to the existing HUD (top-right corner):
```
🟤 450  ⚪ 4  🟡 0
```
Updates live from `CurrencyBalance` table callbacks. Gold/Silver counts are derived client-side from the `Copper` value.

---

## 3. Casino Game Sessions

All game logic is server-authoritative. Clients send intents and react to table updates.

### 3.1 MachineId Convention

All session tables use `MachineId ulong` as PK, which equals the corresponding `PlacedStructure.Id`. The client discovers the `MachineId` for an interacted machine by reading the `"structure_id"` metadata set on the scene node by `WorldManager` (same pattern as existing world item/structure nodes). The `InteractionSystem` passes this ID to the casino mod handler.

### 3.2 Slot Machine

```
SlotSession:
  MachineId   ulong (PK)
  PlayerId    Identity?      (null = unoccupied)
  Reels       string         ("🍒|🍋|⭐")
  State       enum           (Idle, Result)
  Bet         ulong
  WinAmount   ulong
  ExpiresAt   ulong          (server timestamp; auto-release if player disconnects)
```

**Note on State:** `State` has only `Idle` and `Result`. There is no `Spinning` server state — the spin result is computed atomically in one transaction. Client-side spinning animation plays for a fixed duration after receiving the `Result` update, then displays the outcome. The animation is purely cosmetic.

**Reducers:**
```
SpinSlot(machineId, betAmount)
  → Rejects if SlotSession.PlayerId != null && != ctx.Sender (machine occupied).
  → Sets PlayerId = ctx.Sender, State = Result.
  → Rolls RNG server-side, sets Reels string.
  → Calls DebitCoins(bet), then CreditCoins(winAmount).
  → Sets ExpiresAt = now + 30s.

ReleaseSlot(machineId)
  → Rejects if ctx.Sender != SlotSession.PlayerId.
  → Sets PlayerId = null, State = Idle, clears Reels/Bet/WinAmount.
```

Client calls `ReleaseSlot` after displaying the result animation. `SpinSlot` checks `ExpiresAt` at the start of each call and force-releases any session past the threshold before processing the new spin — no server-side timer exists in SpacetimeDB.

**Payout table:**
| Match | Multiplier |
|---|---|
| 3× Gold (💛) | 100× |
| 3× Star (⭐) | 20× |
| 3× Cherry (🍒) | 10× |
| 2× any match | 2× |
| No match | 0 |

### 3.3 Blackjack (Multiplayer)

```
BlackjackGame:
  MachineId   ulong (PK)
  State       enum  (WaitingForPlayers, Dealing, PlayerTurns, DealerTurn, Payout)
  DealerHand  string    ("AS,10H")
  DealerHandHidden string ("??,10H")  ← shown to players during PlayerTurns
  Deck        string    (shuffled draw pile, comma-separated card codes)
  RoundId     uint      (increments each round)

BlackjackSeat:
  Id          ulong (AutoInc PK)
  MachineId   ulong
  SeatIndex   byte  (0–3)
  PlayerId    Identity
  Hand        string    ("7D,KS")
  Bet         ulong
  State       enum  (Waiting, Acting, Standing, Bust, Done)
  RoundId     uint      (matches BlackjackGame.RoundId for this round's seats)
```

**Uniqueness:** `JoinBlackjack` rejects if any `BlackjackSeat` row exists with matching `(MachineId, SeatIndex, RoundId)`.

**Seat cleanup between rounds:** At round start (`StartBlackjackRound`), all `BlackjackSeat` rows for that `MachineId` with the previous `RoundId` are deleted. New seat rows are inserted for players who placed bets.

**Round lifecycle:**
```
WaitingForPlayers
  → Players call JoinBlackjack(machineId, seatIndex) — creates BlackjackSeat row
  → Players call PlaceBet(machineId, amount) — sets seat Bet
  → Any seated player calls StartBlackjackRound(machineId) (min 1 player with bet > 0)
  → Server increments RoundId, deletes old seats, deals cards → State = PlayerTurns

PlayerTurns
  → Each seated player (in SeatIndex order) calls HitBlackjack or StandBlackjack
  → Server advances to next seat when current player Stands or Busts
  → When all seats Done/Standing/Bust → State = DealerTurn

DealerTurn
  → Triggered automatically inside StandBlackjack / HitBlackjack when all seats
    reach Standing/Bust/Done — no separate client call needed.
  → Server draws cards until dealer total ≥ 17.
  → Computes winners vs dealer hand, calls CreditCoins/DebitCoins per seat.
  → State = Payout (brief display state).

Payout
  → Any seated player (or new player) calling JoinBlackjack or StartBlackjackRound
    while State == Payout implicitly resets the game to WaitingForPlayers first.
  → No server-side timer — reset is always triggered by a client reducer call.
```

**Reducers:**
```
JoinBlackjack(machineId, seatIndex)
PlaceBet(machineId, amount)
StartBlackjackRound(machineId)
HitBlackjack(machineId)
StandBlackjack(machineId)
LeaveBlackjack(machineId)   ← removes seat, refunds bet if WaitingForPlayers
SkipSeat(machineId, seatIndex)  ← force-stands a disconnected player's seat; rejects if player still connected
```

**Turn advancement:** `HitBlackjack` and `StandBlackjack` take no seat argument. The server identifies the active seat by finding the `BlackjackSeat` row where `MachineId` matches, `RoundId` matches, and `PlayerId == ctx.Sender`. If no such row exists or the seat's `State != Acting`, the reducer rejects. Turn order: the lowest `SeatIndex` whose State is `Acting` is considered active.

**Disconnect during PlayerTurns:** If a player disconnects mid-round, their seat remains. Any other seated player can call `SkipSeat(machineId, seatIndex)` to force-stand the disconnected player's hand, advancing the round. `SkipSeat` rejects if the target player is still connected.

**Deck:** Single standard 52-card deck (no jokers), reshuffled at the start of every round inside `StartBlackjackRound`. Card codes: rank (A,2–9,10,J,Q,K) + suit (S,H,D,C), e.g. `"AS"`, `"10H"`, `"KD"`.

**Rules:** Dealer draws to 17+. Blackjack (Ace + 10-value) pays 1.5×. No split or double-down in v1.

### 3.4 Coin Pusher

```
CoinPusherState:
  MachineId         ulong (PK)
  CoinCount         uint
  CopperPool        ulong    (total Copper bet by all players since last jackpot)
  LastPusherId      Identity?
  LastPushTime      ulong
  JackpotThreshold  uint     (default: 200 coins)
```

**Reducer:**
```
PushCoin(machineId, copperAmount)
  → Validates copperAmount > 0.
  → Calls DebitCoins(copperAmount).
  → Increments CoinCount by 1 (one push = one coin regardless of amount).
  → Adds copperAmount to CopperPool.
  → Sets LastPusherId = ctx.Sender, LastPushTime = now.
  → If CoinCount >= JackpotThreshold:
      → Calls CreditCoins(LastPusherId, CopperPool, "coin_pusher_jackpot").
      → Resets CoinCount = 0, CopperPool = 0.
```

**Client visuals:** On `CoinPusherState.CoinCount` increase, client spawns one `RigidBody3D` coin at the push entry point. Physics simulated locally — coins stack, fall, jostle naturally. On join, client scatters `CoinCount` coins using `MachineId` as the random seed for a deterministic-enough initial layout (exact positions will differ per client, which is acceptable and by design — coin physics is local-only).

### 3.5 Arcade — Reaction Machine

```
ArcadeSession:
  MachineId      ulong (PK)
  PlayerId       Identity?
  GameType       enum  (Reaction, Pattern)
  State          enum  (Idle, Active, Judging)
  Bet            ulong
  ChallengeData  string   (Reaction: "targetMs:windowMs" e.g. "1500:200";
                           Pattern: sequence string e.g. "RRBLG")
  StartTime      ulong    (server microsecond timestamp at game start)
  ExpiresAt      ulong    (auto-release timeout)
```

**Reaction game flow:**
```
StartArcade(machineId, bet)
  → If ArcadeSession.PlayerId != null && != ctx.Sender:
      → If ExpiresAt < ctx.Timestamp: force-clears the session (same piggyback pattern as slots).
      → Else: rejects (machine occupied).
  → Sets PlayerId, State = Active, GameType = Reaction.
  → Generates random targetMs (1000–3000ms) and windowMs (150ms).
  → Sets ChallengeData = "targetMs:windowMs", StartTime = now, ExpiresAt = now + 10s.
  → Debits bet.

ArcadeInputReaction(machineId)
  → Rejects if State != Active or PlayerId != ctx.Sender.
  → Parses ChallengeData for targetMs, windowMs.
  → Uses ctx.Timestamp exclusively (server-assigned) — no client timestamp parameter.
  → elapsed = (ctx.Timestamp - StartTime) / 1000  [both in microseconds → ms]
  → hit = abs(elapsed - targetMs) <= windowMs + 200  [200ms latency tolerance]
  → If hit: CreditCoins(bet × 3). Else: no credit.
  → State = Judging, then auto-resets to Idle (PlayerId = null) after writing result.
```

### 3.6 Arcade — Pattern Machine

Same `ArcadeSession` table, `GameType = Pattern`.

```
StartArcade(machineId, bet)
  → Same occupancy check as Reaction.
  → Generates random 5-character sequence from {R, G, B, Y} (e.g. "RRGBY").
  → ChallengeData = sequence, StartTime = now, ExpiresAt = now + 15s.
  → Debits bet. State = Active.

ArcadeInputPattern(machineId, playerSequence)
  → Rejects if State != Active or PlayerId != ctx.Sender.
  → Compares playerSequence == ChallengeData (exact match).
  → If match: CreditCoins(bet × 2). Else: no credit.
  → Resets State = Idle, PlayerId = null.
```

Note: `ArcadeInputReaction` and `ArcadeInputPattern` are two separate reducers with different signatures. They share the `ArcadeSession` table but are not interchangeable.

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
- One of each machine type placed inside as `PlacedStructure` rows at fixed relative positions

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

### 4.4 Interaction System Extension

`InteractionSystem` is extended with a static handler registry — a `Dictionary<string, Action<ulong>>` mapping `StructureType` strings to interaction callbacks. The casino mod registers its handlers at `ModManager.Initialize()` time:

```csharp
InteractionSystem.RegisterStructureHandler("casino_slot_machine", id => SlotMachineUI.Open(id));
InteractionSystem.RegisterStructureHandler("casino_blackjack_table", id => BlackjackUI.Open(id));
// etc.
```

`InteractionSystem` is modified minimally: its existing raycast is extended to also hit `Area3D` collision shapes on structure nodes. `WorldManager` (or the casino mod's structure spawner) attaches an `Area3D` + `CollisionShape3D` to each casino machine node and stores the `PlacedStructure.Id` as `"structure_id"` node metadata — the same pattern already used for world item and structure nodes. When the raycast hits a structure `Area3D`, `InteractionSystem` looks up the registered handler by `StructureType` and invokes it with the `MachineId`.

---

## 5. Client UI Architecture

### 5.1 Core Principle: World-Space Rendering

Game state renders directly on machine surfaces. No full-screen panels. Minimal 2D overlays only for input that cannot be shown in-world (bet amounts, seat selection).

### 5.2 Per-Machine Rendering

| Machine | In-World Display | 2D Overlay |
|---|---|---|
| Slot machine | Reels on screen mesh via SubViewport texture | Bet selector + release button (small popup) |
| Blackjack table | 3D card `MeshInstance3D` objects on felt surface | Bet input + seat prompt |
| Coin pusher | `RigidBody3D` coin physics objects on platform | None |
| Arcade reaction | Animated needle on screen mesh | None |
| Arcade pattern | Lit colored button geometry | None |
| Exchange machine | Balance + rates on screen mesh | Amount input |

### 5.3 SubViewport Screen Texture

Machines with display screens use:
```
MachineNode
  ├── MeshInstance3D (body)
  ├── MeshInstance3D (screen) ← ViewportTexture applied to material
  ├── Area3D + CollisionShape3D  ← interaction raycast target
  └── SubViewport
        └── Control (lightweight game state scene, no UIManager)
```

The `Control` inside `SubViewport` subscribes directly to table `OnUpdate` callbacks filtered by `MachineId` and redraws on change.

### 5.4 3D Card Objects (Blackjack)

Cards are `MeshInstance3D` nodes (thin box mesh) with a `StandardMaterial3D` using a card sprite sheet texture atlas. Server pushes `BlackjackSeat.Hand` updates → client parses hand string → spawns/repositions card meshes on the table felt surface. All nearby players see the same cards in 3D via normal Godot scene rendering (no sync needed — each client rebuilds from the same server state).

### 5.5 Physics Coins (Coin Pusher)

On `CoinPusherState.CoinCount` increase:
- Client spawns one `RigidBody3D` coin `MeshInstance3D` at the push entry point
- Godot physics handles stacking, falling, jostling locally
- On join, client scatters `CoinCount` coin objects using `MachineId` as random seed for initial placement — layout is non-deterministic across clients by design (physics-only simulation, no sync)
- Server is authoritative only on count and jackpot; visual fidelity is local

### 5.6 Model Requirements for 3D Artists

Casino machine `.glb` models must include:
- A dedicated **screen mesh surface** (named `Screen`) for SubViewport texture application
- **Physical button geometry** as separate named mesh surfaces for `Area3D` attachment
- Blackjack table: a flat **felt zone** surface (named `FeltZone`) for card placement anchor
- Coin pusher: an open **platform area** surface (named `Platform`) as coin spawn/physics bounds

Placeholder models (colored primitive boxes matching the specified footprints) are used until final `.glb` files are delivered.

---

## 6. Data Flow Summary

```
Player presses E near machine
  → InteractionSystem raycast hits machine Area3D
  → Looks up StructureType → registered casino handler invoked with MachineId
  → Small 2D input popup shown (bet/seat) if needed
  → Client calls reducer (SpinSlot, JoinBlackjack, PushCoin, etc.)
  → Server validates, updates session table row
  → Table OnUpdate callback fires on all subscribed clients
  → Machine's SubViewport Control re-renders from new state
  → (Blackjack) Card MeshInstance3D nodes spawn/reposition on table
  → (Coin pusher) RigidBody3D coin spawns at push point, physics takes over
  → (Slot) Client plays spin animation for fixed duration, then shows Result state
```

---

## 7. Implementation Phases

| Phase | Scope |
|---|---|
| 1 | Mod framework: folder structure, compile symbols, ModConfig + AdminList tables, ModManager client singleton, InteractionSystem registry extension |
| 2 | Currency mod: tables, internal helpers, ExchangeResources + WithdrawCoins reducers, CurrencyHUD, GrantStartingBalance in ClientConnected |
| 3 | Casino building + exchange machine (world placement, placeholder models, Area3D interaction) |
| 4 | Slot machine (SlotSession, SpinSlot + ReleaseSlot, SubViewport reels UI) |
| 5 | Blackjack (BlackjackGame + BlackjackSeat, full round lifecycle, 3D card objects) |
| 6 | Coin pusher (CoinPusherState, PushCoin, RigidBody3D coin physics) |
| 7 | Arcade machines — reaction + pattern (ArcadeSession, ArcadeInputReaction + ArcadeInputPattern, screen animations) |
| 8 | Swap placeholder models for final `.glb` assets |

---

## 8. Out of Scope (v1)

- Blackjack split / double-down
- Poker or other card games
- Real-money or external payment integration
- Persistent leaderboards
- Coin physics sync across players (local simulation only)
- Mobile/controller input for casino UI
- Arcade machine leaderboards or high-score tracking
