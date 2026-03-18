# NPC System Design

**Date**: 2026-03-18
**Status**: Approved

## Overview

A modular NPC framework for SandboxRPG enabling modders to create NPCs with customizable behaviors, combat, dialogue, and trade. NPCs are driven by a state machine whose behaviors determine disposition — there is no fixed hostile/friendly enum.

## Architecture

Three-tier:
- **SpacetimeDB** — authoritative state (NPC rows, health, position, loot, damage events)
- **Game Service** — new .NET console app, connects as SpacetimeDB client, runs AI tick loop, calls reducers
- **Godot Client** — renders NPCs, handles player input (attack, interact), displays UI (dialogue, trade, damage numbers)

### Game Service Authorization

The Game Service connects as a regular SpacetimeDB client but is identified by its `Identity`. On first connect, `ClientConnected` stores its identity in a `service_identity` table. All NPC-mutating reducers (`NpcMove`, `NpcSetState`, `NpcDealDamage`, `SpawnNpc`, etc.) validate `ctx.Sender` against this table — regular player clients cannot call them.

The service identity is registered via a one-time `RegisterServiceIdentity` reducer that can only be called when no service identity exists yet (first-come-first-served). In production this would use a shared secret; for development, the first caller wins.

## Data Model (SpacetimeDB Tables)

All tables are C# `[Table]` structs within `public static partial class Module`, placed in `mods/npcs/server/NpcTables.cs`.

### `service_identity`
| Column | Type | Notes |
|---|---|---|
| Id | ulong (AutoInc PK) | |
| ServiceIdentity | Identity | The Game Service's SpacetimeDB identity |

### `npc_config`
| Column | Type | Notes |
|---|---|---|
| Id | ulong (AutoInc PK) | |
| NpcType | string | Unique key (e.g., `"wolf"`) |
| MaxHealth | int | |
| AttackDamage | int | Base melee damage |
| AttackRange | float | |
| AttackCooldownMs | ulong | |
| IsAttackable | bool | Whether players can attack this NPC |
| IsTrader | bool | Whether this NPC offers trade |
| HasDialogue | bool | Whether this NPC has dialogue |

### `npc`
| Column | Type | Notes |
|---|---|---|
| Id | ulong (AutoInc PK) | |
| NpcType | string | References npc_config |
| PosX, PosY, PosZ | float | World position |
| RotY | float | Facing direction |
| Health | int | Current health |
| MaxHealth | int | |
| CurrentState | string | Active state machine state (e.g., `"idle"`, `"combat"`) |
| TargetEntityId | ulong? | Nullable — who they're focused on |
| TargetEntityType | string? | `"player"` or `"npc"` |
| SpawnPosX, SpawnPosY, SpawnPosZ | float | Respawn / leash anchor |
| IsAlive | bool | Dead NPCs stay for respawn tracking |
| LastUpdateMs | ulong | Timestamp of last AI tick |

### `npc_loot_table`
| Column | Type | Notes |
|---|---|---|
| Id | ulong (AutoInc PK) | |
| NpcType | string | |
| ItemType | string | |
| Quantity | int | |
| DropChance | float | 0.0–1.0 |

### `npc_spawn_rule`
| Column | Type | Notes |
|---|---|---|
| Id | ulong (AutoInc PK) | |
| NpcType | string | |
| ZoneX, ZoneZ | float | Center of spawn zone |
| ZoneRadius | float | |
| MaxCount | int | Max alive in this zone |
| RespawnTimeSec | float | |

### `damage_event`
| Column | Type | Notes |
|---|---|---|
| Id | ulong (AutoInc PK) | |
| SourceId | ulong | |
| SourceType | string | `"player"` or `"npc"` |
| TargetId | ulong | |
| TargetType | string | `"player"` or `"npc"` |
| Amount | int | |
| DamageType | string | `"melee"`, `"ranged"`, `"fire"`, etc. |
| Timestamp | ulong | |

Cleanup: The Game Service calls `CleanupDamageEvents` periodically (every 30s) to delete events older than 30 seconds. Clients only need them briefly for damage number effects.

## Server Reducers

### File Layout
```
mods/npcs/server/
├── NpcsMod.cs              Server mod — seeds configs, spawn rules, loot tables
├── NpcTables.cs            All NPC-related [Table] structs
├── NpcReducers.cs          SpawnNpc, NpcMove, NpcSetState, NpcDealDamage, NpcDeath, NpcRespawn, DespawnNpc, CleanupDamageEvents
├── CombatReducers.cs       PlayerAttackNpc, PlayerAttackPlayer (stub)
└── TradeReducers.cs        NpcTrade
```

### NPC Reducers (called by Game Service — validated against service_identity)
- **`RegisterServiceIdentity()`** — Stores `ctx.Sender` as the service identity. Fails if one already exists.
- **`SpawnNpc(npcType, x, y, z, rotY)`** — Looks up `npc_config` for MaxHealth. Creates NPC row, initial state `"idle"`.
- **`NpcMove(npcId, x, y, z, rotY)`** — Updates position, sets LastUpdateMs.
- **`NpcSetState(npcId, state, targetEntityId?, targetEntityType?)`** — State transition.
- **`NpcDealDamage(npcId, targetId, targetType, amount, damageType)`** — NPC attacks target. Inserts damage_event, reduces target health. Triggers NpcDeath/player death if health ≤ 0.
- **`NpcDeath(npcId)`** — Rolls loot from `npc_loot_table`, spawns world_items at NPC position, sets IsAlive=false.
- **`NpcRespawn(npcId)`** — Resets health from `npc_config`, moves to SpawnPos, IsAlive=true, state=idle.
- **`DespawnNpc(npcId)`** — Removes NPC row.
- **`CleanupDamageEvents()`** — Deletes damage_events older than 30 seconds.

### Player Combat Reducers (called by clients)
- **`PlayerAttackNpc(npcId)`** — Validates range (using player position vs NPC position, max 3.0 units). Base damage is 10 (fists). If player has `iron_sword` in inventory, damage is 25. Inserts damage_event, reduces NPC health. Calls NpcDeath if ≤ 0. Validates NPC's `npc_config.IsAttackable` is true.
- **`PlayerAttackPlayer(targetIdentity)`** — PvP stub (logs warning, no-op for now).

### NPC Interaction Reducers (called by clients)
- **`NpcTrade(npcId, buyItemType, quantity)`** — Validates: NPC exists, is alive, `npc_config.IsTrader` is true, player is within range (5.0 units). Looks up `TradeOffer` in `npc_trade_offer` table for price. Checks player has enough `copper_coin` in inventory. Deducts coins, adds purchased item to player inventory. If insufficient funds: reducer fails with error, client handles gracefully.

### Additional Table for Trade
### `npc_trade_offer`
| Column | Type | Notes |
|---|---|---|
| Id | ulong (AutoInc PK) | |
| NpcType | string | |
| ItemType | string | Item being sold |
| Price | int | Cost in copper_coin |
| Currency | string | `"copper_coin"` (extensible later) |

## Game Service

### Project Structure
```
service/
├── GameService.cs              Entry point, connection, tick loop
├── NpcBrain.cs                 Per-NPC AI runner (ephemeral state)
├── SpawnManager.cs             Spawn rule evaluation
├── IServiceMod.cs              Service mod interface
├── ServiceModLoader.cs         Dependency resolution (same pattern as ModLoader)
├── GameService.csproj          .NET 8, SpacetimeDB.ClientSDK 2.0.*
└── mods/
    └── npcs/
        └── NpcServiceMod.cs    Registers NPC configs for AI
```

### IServiceMod Interface
```csharp
public interface IServiceMod
{
    string Name { get; }
    string Version { get; }
    string[] Dependencies { get; }
    void Initialize(ServiceContext ctx);  // Register configs, behaviors
}
```

Similar to `IMod` (server) and `IClientMod` (client) but runs in the Game Service process. `ServiceContext` provides access to the SpacetimeDB connection and NPC registries.

### Configuration
Tick rate and connection settings via command-line args or `service-config.json`:
```json
{
    "tickRate": 10,
    "spacetimeDbUrl": "http://127.0.0.1:3000",
    "databaseName": "sandbox-rpg"
}
```

### Tick Loop
1. Connect to SpacetimeDB, call `RegisterServiceIdentity`, subscribe to all tables
2. Run at configured tick rate (default 10 ticks/sec)
3. Each tick:
   - For each alive NPC: evaluate state machine, execute behaviors, call reducers
   - Evaluate spawn rules: count alive per zone, spawn/respawn as needed
   - Every 30s: call `CleanupDamageEvents`

### Scaling Note
At 10 ticks/sec with N NPCs, the service makes up to N `NpcMove` calls per tick. For idle/distant NPCs, the service skips movement updates (only moving NPCs generate reducer calls). This keeps call volume manageable for 50-100 NPCs. Beyond that, tick rate reduction for distant NPCs is a future optimization.

### NPC Brain
Per-NPC instance holding ephemeral state (cooldowns, aggro timer, wander target position). Reads NPC row from SpacetimeDB subscription cache, evaluates state machine via StateMachineRunner, calls reducers.

### State Machine Runner
Generic evaluator. Takes NPC config (states + transitions), current state, world context. Returns actions to execute and optional state transition.

### Behavior Library

Interface:
```csharp
public interface INpcBehavior
{
    string Name { get; }
    void Tick(NpcContext ctx, float delta);
}
```

Built-in behaviors:
- `IdleBehavior` — Stand still
- `WanderBehavior` — Random movement within leash radius
- `ChaseBehavior` — Move toward target
- `FleeBehavior` — Move away from target
- `MeleeAttackBehavior` — Attack on cooldown when in range
- `ReturnToSpawnBehavior` — Walk back to spawn point
- `PatrolBehavior` — Move between waypoints

### Transition Conditions

Interface: `ITransitionCondition`

Built-in:
- `player_in_range(range)` — Any player within distance
- `hostile_npc_in_range(range)` — Any hostile NPC within distance (for guards)
- `target_lost` — Target dead/disconnected/out of leash
- `leash_range(range)` — NPC too far from spawn
- `health_below(percent)` — HP threshold
- `target_in_range(range)` — Target within attack distance
- `no_target` — No valid target
- `was_attacked` — NPC received damage recently (for guard retaliation)

### NPC Config Registration (Game Service side)
```csharp
NpcConfigRegistry.Register("wolf", new NpcConfig {
    MaxHealth = 50,
    AttackDamage = 8,
    AttackRange = 2.0f,
    AttackCooldownMs = 1500,
    MoveSpeed = 4.0f,
    AggroRange = 10f,
    LeashRange = 30f,
    States = {
        ["idle"] = new NpcStateConfig {
            Behaviors = ["wander"],
            Transitions = [
                new("combat", "player_in_range", range: 10f)
            ]
        },
        ["combat"] = new NpcStateConfig {
            Behaviors = ["chase", "melee_attack"],
            Transitions = [
                new("idle", "target_lost"),
                new("idle", "leash_range", range: 30f)
            ]
        }
    }
});
```

Note: `NpcConfigRegistry` is Game Service in-memory only. The server has `npc_config` table for the subset of data it needs (MaxHealth, AttackDamage, IsAttackable, etc.). The full state machine definition lives only in the service since the server doesn't evaluate AI.

## Client Architecture

### File Layout
```
client/mods/npcs/
├── NpcsClientMod.cs            Registers visuals, dialogue, trades
├── NpcEntity.cs                Node3D — interpolation, health bar, IInteractable/IAttackable
├── NpcSpawner.cs               Signal-driven spawn/update/remove
├── registries/
│   ├── NpcVisualRegistry.cs
│   ├── DialogueRegistry.cs
│   └── TradeRegistry.cs
├── ui/
│   ├── NpcDialoguePanel.cs
│   ├── NpcTradePanel.cs
│   └── DamageNumberEffect.cs
└── content/
    └── NpcContent.cs           Registers example NPC visuals, dialogue, trades
```

### IAttackable Interface

Located at `client/scripts/interaction/IAttackable.cs` (framework-level, alongside `IInteractable`):

```csharp
public interface IAttackable
{
    string AttackHintText { get; }  // "[LMB] Attack Wolf"
    bool CanAttack(Player? player);
    void Attack(Player? player);    // Calls PlayerAttackNpc reducer
}
```

### InteractionSystem Integration

The existing `InteractionSystem` raycasts and checks for `IInteractable`. We extend it with a second check: if the raycast hit implements `IAttackable` and the `primary_attack` input is pressed, call `Attack()`. The two interfaces are independent — an NPC can implement both (merchant you can talk to AND attack), one, or neither.

Specifically in `_Process`:
1. Raycast as before
2. Check for `IInteractable` — show hint, handle `interact` input (existing)
3. Check for `IAttackable` — show attack hint, handle `primary_attack` input (new)
4. If both exist on same node, `IInteractable` hint takes priority in the HUD, attack hint shown as secondary

### NPC Entity
- Loads model from NpcVisualRegistry or falls back to colored capsule
- Name label above head
- Health bar (shown when damaged, hidden at full HP)
- Conditionally implements `IInteractable` (if `npc_config.HasDialogue` or `npc_config.IsTrader`)
- Conditionally implements `IAttackable` (if `npc_config.IsAttackable`)
- Interpolates position between server updates (same approach as RemotePlayer)

### Dialogue System
- Client-side only — no reducer needed. NPC type is known from the `npc` table row via subscription. Client looks up dialogue in `DialogueRegistry`.
- `NpcDialoguePanel.cs` — pushed via UIManager, shows NPC name + lines sequentially
- `DialogueRegistry.cs` — maps NPC type → list of dialogue strings
- Pressing E on a dialogue NPC opens the panel directly, no server round-trip

### Trade System
- `NpcTradePanel.cs` — reads `npc_trade_offer` table rows for the NPC's type, displays item + price + player's copper_coin count, buy button calls `NpcTrade` reducer
- `TradeRegistry.cs` — client-side display names/icons for trade items (supplements server trade_offer data)

### Combat UI
- Damage numbers: float up from target position when new `damage_event` rows appear in subscription
- NPC health bar: small bar above NPC, only visible when Health < MaxHealth
- Death effect: fade-out when NPC's IsAlive transitions to false

### GameManager Additions
- Signals: `NpcUpdated(npcId)`, `NpcRemoved(npcId)`, `DamageEventReceived(eventId)`
- Accessors: `GetAllNpcs()`, `GetNpc(id)`, `GetRecentDamageEvents()`, `GetNpcConfig(npcType)`, `GetTradeOffers(npcType)`

## Example NPCs

### Wolf (hostile, melee)
- States: `idle` (wander) → `combat` (chase + melee_attack) → `idle` (target lost/leash)
- 50 HP, 8 damage, attack range 2.0, aggro range 10, leash 30
- Drops: raw_meat x2 (100%), wolf_pelt x1 (50%)
- Visual: gray capsule, scale 0.8
- IsAttackable: true, IsTrader: false, HasDialogue: false

### Merchant (friendly, trades)
- States: `idle` (stand still)
- 100 HP, not attackable
- IInteractable → opens trade panel
- Sells: bread (5 copper), iron_sword (20 copper), health_potion (10 copper)
- Visual: green capsule, scale 1.0
- IsAttackable: false, IsTrader: true, HasDialogue: true
- Dialogue: ["Welcome, traveler!", "Browse my wares."]

### Guard (friendly, retaliates)
- States: `idle` (patrol) → `combat` (chase + melee_attack) → `return_to_spawn`
- 150 HP, 15 damage, attack range 2.5
- Transitions to combat via `was_attacked` or `hostile_npc_in_range(15)`
- No loot drops
- Visual: blue capsule, scale 1.1
- IsAttackable: true, IsTrader: false, HasDialogue: true
- Dialogue: ["Move along, citizen.", "The town is safe under my watch."]

## New Items
- `iron_sword` — melee weapon, player attack damage 25 (vs 10 fists). Detected by checking inventory for item type.
- `health_potion` — consumable (stub, no use mechanic yet)
- `raw_meat`, `wolf_pelt`, `bread` — trade/loot items

## Out of Scope
- Ranged/projectile combat (framework supports via DamageType, no implementation)
- PvP (stub reducer only)
- Quest system
- Pathfinding/navmesh (straight-line movement)
- NPC animations
- Dialogue branching (sequential only)
- Persistent NPC inventory (traders have infinite stock from trade_offer table)
- Weapon/equipment slots (sword detected by inventory presence, not equip slot)

## Mod Extension Pattern

A modder creating a "bandit" NPC:
1. **Server** (`mods/bandits/server/`): `IMod.Seed()` inserts rows into `npc_config`, `npc_loot_table`, `npc_spawn_rule`, and optionally `npc_trade_offer`
2. **Service** (`service/mods/bandits/`): `IServiceMod.Initialize()` calls `NpcConfigRegistry.Register("bandit", ...)` with state machine and behaviors
3. **Client** (`client/mods/bandits/`): `IClientMod.Initialize()` registers visual in `NpcVisualRegistry`, dialogue in `DialogueRegistry`

No framework code touched — registration calls and table inserts only.
