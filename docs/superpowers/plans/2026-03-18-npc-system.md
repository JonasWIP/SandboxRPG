# NPC System Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a modular NPC framework across SpacetimeDB, a new Game Service, and the Godot client — enabling hostile, friendly, and trader NPCs driven by state-machine AI.

**Architecture:** Three-tier: SpacetimeDB holds authoritative NPC state (tables), a new .NET Game Service connects as a client to run AI tick loops and call reducers, and the Godot client renders NPCs and handles player combat/interaction input. The NPC mod (`mods/npcs/`) follows the existing mod pattern with server, service, and client components.

**Tech Stack:** SpacetimeDB 2.0 (C# WASM), .NET 8 console app (Game Service), Godot 4.6.1 C#, SpacetimeDB.ClientSDK 2.0.1

**Spec:** `docs/superpowers/specs/2026-03-18-npc-system-design.md`

---

## File Map

### Server (SpacetimeDB WASM module)
| Action | Path | Responsibility |
|--------|------|---------------|
| Create | `mods/npcs/server/NpcTables.cs` | All NPC-related `[Table]` structs: Npc, NpcConfig, NpcLootTable, NpcSpawnRule, NpcTradeOffer, DamageEvent, ServiceIdentity |
| Create | `mods/npcs/server/NpcsMod.cs` | Server mod (IMod) — seeds configs, spawn rules, loot tables, trade offers for example NPCs |
| Create | `mods/npcs/server/NpcReducers.cs` | Service-only reducers: RegisterServiceIdentity, SpawnNpc, NpcMove, NpcSetState, NpcDealDamage, NpcDeath, NpcRespawn, DespawnNpc, CleanupDamageEvents |
| Create | `mods/npcs/server/CombatReducers.cs` | Player combat: PlayerAttackNpc, PlayerAttackPlayer (stub) |
| Create | `mods/npcs/server/TradeReducers.cs` | NpcTrade reducer |
| Modify | `server/StdbModule.csproj` | Add `<Compile Include>` for `mods/npcs/server/` |
| Modify | `server/Lifecycle.cs` | Add `_ = _npcsMod;` to Init for WASM static init |

### Game Service (new .NET console app)
| Action | Path | Responsibility |
|--------|------|---------------|
| Create | `service/GameService.csproj` | .NET 8 console app, SpacetimeDB.ClientSDK 2.0.* |
| Create | `service/GameService.cs` | Entry point: connect to STDB, register service identity, tick loop |
| Create | `service/IServiceMod.cs` | Service mod interface |
| Create | `service/ServiceModLoader.cs` | Topo-sort + init service mods |
| Create | `service/NpcBrain.cs` | Per-NPC AI runner with ephemeral state |
| Create | `service/SpawnManager.cs` | Reads spawn rules, spawns/respawns NPCs |
| Create | `service/StateMachineRunner.cs` | Generic state machine evaluator |
| Create | `service/behaviors/INpcBehavior.cs` | Behavior interface |
| Create | `service/behaviors/BehaviorRegistry.cs` | Maps behavior name → instance |
| Create | `service/behaviors/IdleBehavior.cs` | Stand still |
| Create | `service/behaviors/WanderBehavior.cs` | Random movement within leash radius |
| Create | `service/behaviors/ChaseBehavior.cs` | Move toward target |
| Create | `service/behaviors/FleeBehavior.cs` | Move away from target |
| Create | `service/behaviors/MeleeAttackBehavior.cs` | Attack on cooldown when in range |
| Create | `service/behaviors/ReturnToSpawnBehavior.cs` | Walk back to spawn point |
| Create | `service/behaviors/PatrolBehavior.cs` | Move between waypoints |
| Create | `service/conditions/ITransitionCondition.cs` | Condition interface |
| Create | `service/conditions/ConditionRegistry.cs` | Maps condition name → factory |
| Create | `service/conditions/BuiltInConditions.cs` | player_in_range, target_lost, leash_range, health_below, target_in_range, no_target, was_attacked, hostile_npc_in_range |
| Create | `service/NpcConfig.cs` | NpcConfig, NpcStateConfig, NpcConfigRegistry |
| Create | `service/NpcContext.cs` | Context object passed to behaviors (NPC data, world state, reducer calls) |
| Create | `service/mods/npcs/NpcServiceMod.cs` | Registers wolf, merchant, guard configs |

### Client (Godot)
| Action | Path | Responsibility |
|--------|------|---------------|
| Create | `client/scripts/interaction/IAttackable.cs` | IAttackable interface (framework-level) |
| Create | `client/mods/npcs/NpcsClientMod.cs` | Client mod — registers NPC content |
| Create | `client/mods/npcs/NpcEntity.cs` | Node3D: visual, interpolation, health bar, IInteractable + IAttackable |
| Create | `client/mods/npcs/NpcSpawner.cs` | Signal-driven NPC spawn/update/remove |
| Create | `client/mods/npcs/registries/NpcVisualRegistry.cs` | NpcType → NpcVisualDef |
| Create | `client/mods/npcs/registries/DialogueRegistry.cs` | NpcType → dialogue lines |
| Create | `client/mods/npcs/registries/TradeRegistry.cs` | NpcType → display info for trade |
| Create | `client/mods/npcs/ui/NpcDialoguePanel.cs` | Dialogue UI panel |
| Create | `client/mods/npcs/ui/NpcTradePanel.cs` | Trade UI panel |
| Create | `client/mods/npcs/ui/DamageNumberEffect.cs` | Floating damage numbers |
| Create | `client/mods/npcs/content/NpcContent.cs` | Registers example NPC visuals, dialogue, trades, items |
| Modify | `client/scripts/networking/GameManager.cs` | Add NPC/DamageEvent signals, accessors, reducer calls |
| Modify | `client/mods/base/world/InteractionSystem.cs` | Add IAttackable check on primary_attack |
| Modify | `client/mods/base/world/WorldManager.cs` | Wire NPC signals to NpcSpawner |
| Modify | `client/project.godot` | Add NpcsClientMod autoload |

---

## Chunk 1: Server Tables & NPC Mod

### Task 1: Create NPC server tables

**Files:**
- Create: `mods/npcs/server/NpcTables.cs`

- [ ] **Step 1: Create the NPC tables file**

```csharp
// mods/npcs/server/NpcTables.cs
using SpacetimeDB;

namespace SandboxRPG.Server;

public static partial class Module
{
    [Table(Name = "service_identity", Public = true)]
    public partial struct ServiceIdentity
    {
        [AutoInc][PrimaryKey]
        public ulong Id;
        public Identity ServiceId;
    }

    [Table(Name = "npc_config", Public = true)]
    public partial struct NpcConfig
    {
        [AutoInc][PrimaryKey]
        public ulong Id;
        public string NpcType;
        public int MaxHealth;
        public int AttackDamage;
        public float AttackRange;
        public ulong AttackCooldownMs;
        public bool IsAttackable;
        public bool IsTrader;
        public bool HasDialogue;
    }

    [Table(Name = "npc", Public = true)]
    public partial struct Npc
    {
        [AutoInc][PrimaryKey]
        public ulong Id;
        public string NpcType;
        public float PosX;
        public float PosY;
        public float PosZ;
        public float RotY;
        public int Health;
        public int MaxHealth;
        public string CurrentState;
        public ulong TargetEntityId;
        public string TargetEntityType;
        public float SpawnPosX;
        public float SpawnPosY;
        public float SpawnPosZ;
        public bool IsAlive;
        public ulong LastUpdateMs;
    }

    [Table(Name = "npc_loot_table", Public = true)]
    public partial struct NpcLootTable
    {
        [AutoInc][PrimaryKey]
        public ulong Id;
        public string NpcType;
        public string ItemType;
        public int Quantity;
        public float DropChance;
    }

    [Table(Name = "npc_spawn_rule", Public = true)]
    public partial struct NpcSpawnRule
    {
        [AutoInc][PrimaryKey]
        public ulong Id;
        public string NpcType;
        public float ZoneX;
        public float ZoneZ;
        public float ZoneRadius;
        public int MaxCount;
        public float RespawnTimeSec;
    }

    [Table(Name = "npc_trade_offer", Public = true)]
    public partial struct NpcTradeOffer
    {
        [AutoInc][PrimaryKey]
        public ulong Id;
        public string NpcType;
        public string ItemType;
        public int Price;
        public string Currency;
    }

    [Table(Name = "damage_event", Public = true)]
    public partial struct DamageEvent
    {
        [AutoInc][PrimaryKey]
        public ulong Id;
        public ulong SourceId;
        public string SourceType;
        public ulong TargetId;
        public string TargetType;
        public int Amount;
        public string DamageType;
        public ulong Timestamp;
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add mods/npcs/server/NpcTables.cs
git commit -m "feat(npcs): add SpacetimeDB table definitions for NPC system"
```

### Task 2: Create NPC server mod with seed data

**Files:**
- Create: `mods/npcs/server/NpcsMod.cs`

- [ ] **Step 1: Create the NPC server mod**

```csharp
// mods/npcs/server/NpcsMod.cs
using SpacetimeDB;
using System;
using SandboxRPG.Server.Mods;

namespace SandboxRPG.Server;

public static partial class Module
{
    private static readonly NpcsModImpl _npcsMod = new();

    private sealed class NpcsModImpl : IMod
    {
        public NpcsModImpl() => ModLoader.Register(this);

        public string   Name         => "npcs";
        public string   Version      => "1.0.0";
        public string[] Dependencies => new[] { "base" };

        public void Seed(ReducerContext ctx)
        {
            SeedNpcConfigs(ctx);
            SeedLootTables(ctx);
            SeedSpawnRules(ctx);
            SeedTradeOffers(ctx);
            SeedNpcItems(ctx);
            Log.Info("[NpcsMod] Seeded.");
        }
    }

    private static void SeedNpcConfigs(ReducerContext ctx)
    {
        ctx.Db.NpcConfig.Insert(new NpcConfig
        {
            NpcType = "wolf", MaxHealth = 50, AttackDamage = 8,
            AttackRange = 2.0f, AttackCooldownMs = 1500,
            IsAttackable = true, IsTrader = false, HasDialogue = false,
        });
        ctx.Db.NpcConfig.Insert(new NpcConfig
        {
            NpcType = "merchant", MaxHealth = 100, AttackDamage = 0,
            AttackRange = 0f, AttackCooldownMs = 0,
            IsAttackable = false, IsTrader = true, HasDialogue = true,
        });
        ctx.Db.NpcConfig.Insert(new NpcConfig
        {
            NpcType = "guard", MaxHealth = 150, AttackDamage = 15,
            AttackRange = 2.5f, AttackCooldownMs = 1200,
            IsAttackable = true, IsTrader = false, HasDialogue = true,
        });
    }

    private static void SeedLootTables(ReducerContext ctx)
    {
        ctx.Db.NpcLootTable.Insert(new NpcLootTable { NpcType = "wolf", ItemType = "raw_meat",  Quantity = 2, DropChance = 1.0f });
        ctx.Db.NpcLootTable.Insert(new NpcLootTable { NpcType = "wolf", ItemType = "wolf_pelt", Quantity = 1, DropChance = 0.5f });
    }

    private static void SeedSpawnRules(ReducerContext ctx)
    {
        // Wolves spawn in a zone 30 units from center
        ctx.Db.NpcSpawnRule.Insert(new NpcSpawnRule
        {
            NpcType = "wolf", ZoneX = 30f, ZoneZ = 30f,
            ZoneRadius = 15f, MaxCount = 3, RespawnTimeSec = 30f,
        });
        // Merchant near spawn
        ctx.Db.NpcSpawnRule.Insert(new NpcSpawnRule
        {
            NpcType = "merchant", ZoneX = 5f, ZoneZ = -5f,
            ZoneRadius = 0f, MaxCount = 1, RespawnTimeSec = 10f,
        });
        // Guard near spawn
        ctx.Db.NpcSpawnRule.Insert(new NpcSpawnRule
        {
            NpcType = "guard", ZoneX = -5f, ZoneZ = -5f,
            ZoneRadius = 0f, MaxCount = 1, RespawnTimeSec = 10f,
        });
    }

    private static void SeedTradeOffers(ReducerContext ctx)
    {
        ctx.Db.NpcTradeOffer.Insert(new NpcTradeOffer { NpcType = "merchant", ItemType = "bread",          Price = 5,  Currency = "copper_coin" });
        ctx.Db.NpcTradeOffer.Insert(new NpcTradeOffer { NpcType = "merchant", ItemType = "iron_sword",     Price = 20, Currency = "copper_coin" });
        ctx.Db.NpcTradeOffer.Insert(new NpcTradeOffer { NpcType = "merchant", ItemType = "health_potion",  Price = 10, Currency = "copper_coin" });
    }

    private static void SeedNpcItems(ReducerContext ctx)
    {
        // Register new item recipes so they can exist in inventory
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe
        {
            ResultItemType = "iron_sword", ResultQuantity = 1,
            Ingredients = "iron:5,wood:2", CraftTimeSeconds = 4f, Station = "crafting_table",
        });
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add mods/npcs/server/NpcsMod.cs
git commit -m "feat(npcs): add NPC server mod with seed data for wolf, merchant, guard"
```

### Task 3: Create NPC reducers (service-only)

**Files:**
- Create: `mods/npcs/server/NpcReducers.cs`

- [ ] **Step 1: Create the NPC reducers**

```csharp
// mods/npcs/server/NpcReducers.cs
using SpacetimeDB;
using System;

namespace SandboxRPG.Server;

public static partial class Module
{
    // ---- helpers ----

    private static bool IsService(ReducerContext ctx)
    {
        foreach (var si in ctx.Db.ServiceIdentity.Iter())
            if (si.ServiceId == ctx.Sender) return true;
        return false;
    }

    private static NpcConfig? FindNpcConfig(ReducerContext ctx, string npcType)
    {
        foreach (var cfg in ctx.Db.NpcConfig.Iter())
            if (cfg.NpcType == npcType) return cfg;
        return null;
    }

    private static ulong NowMs(ReducerContext ctx) =>
        (ulong)((DateTimeOffset)ctx.Timestamp).ToUnixTimeMilliseconds();

    // ---- service identity ----

    [Reducer]
    public static void RegisterServiceIdentity(ReducerContext ctx)
    {
        // First-come-first-served: only one service identity allowed
        foreach (var _ in ctx.Db.ServiceIdentity.Iter())
        {
            Log.Warn("[NpcReducers] Service identity already registered.");
            return;
        }
        ctx.Db.ServiceIdentity.Insert(new ServiceIdentity { ServiceId = ctx.Sender });
        Log.Info($"[NpcReducers] Service identity registered: {ctx.Sender}");
    }

    // ---- NPC lifecycle (service-only) ----

    [Reducer]
    public static void SpawnNpc(ReducerContext ctx, string npcType, float x, float y, float z, float rotY)
    {
        if (!IsService(ctx)) return;
        var cfg = FindNpcConfig(ctx, npcType);
        if (cfg is null) { Log.Warn($"[NpcReducers] Unknown NpcType: {npcType}"); return; }

        ctx.Db.Npc.Insert(new Npc
        {
            NpcType = npcType,
            PosX = x, PosY = y, PosZ = z, RotY = rotY,
            Health = cfg.Value.MaxHealth, MaxHealth = cfg.Value.MaxHealth,
            CurrentState = "idle",
            TargetEntityId = 0, TargetEntityType = "",
            SpawnPosX = x, SpawnPosY = y, SpawnPosZ = z,
            IsAlive = true,
            LastUpdateMs = NowMs(ctx),
        });
    }

    [Reducer]
    public static void NpcMove(ReducerContext ctx, ulong npcId, float x, float y, float z, float rotY)
    {
        if (!IsService(ctx)) return;
        var row = ctx.Db.Npc.Id.Find(npcId);
        if (row is null) return;
        var npc = row.Value;
        ctx.Db.Npc.Delete(npc);
        npc.PosX = x; npc.PosY = y; npc.PosZ = z; npc.RotY = rotY;
        npc.LastUpdateMs = NowMs(ctx);
        ctx.Db.Npc.Insert(npc);
    }

    [Reducer]
    public static void NpcSetState(ReducerContext ctx, ulong npcId, string state, ulong targetEntityId, string targetEntityType)
    {
        if (!IsService(ctx)) return;
        var row = ctx.Db.Npc.Id.Find(npcId);
        if (row is null) return;
        var npc = row.Value;
        ctx.Db.Npc.Delete(npc);
        npc.CurrentState = state;
        npc.TargetEntityId = targetEntityId;
        npc.TargetEntityType = targetEntityType;
        npc.LastUpdateMs = NowMs(ctx);
        ctx.Db.Npc.Insert(npc);
    }

    [Reducer]
    public static void NpcDealDamage(ReducerContext ctx, ulong npcId, ulong targetId, string targetType, int amount, string damageType)
    {
        if (!IsService(ctx)) return;

        // Insert damage event
        ctx.Db.DamageEvent.Insert(new DamageEvent
        {
            SourceId = npcId, SourceType = "npc",
            TargetId = targetId, TargetType = targetType,
            Amount = amount, DamageType = damageType,
            Timestamp = NowMs(ctx),
        });

        // Player damage is handled by NpcDealDamageToPlayer (players are keyed by Identity, not ulong)
        if (targetType == "player") return;

        if (targetType == "npc")
        {
            var target = ctx.Db.Npc.Id.Find(targetId);
            if (target is null) return;
            var t = target.Value;
            ctx.Db.Npc.Delete(t);
            t.Health = Math.Max(0, t.Health - amount);
            if (t.Health <= 0)
            {
                t.IsAlive = false;
                ctx.Db.Npc.Insert(t);
                NpcDeathInternal(ctx, t);
            }
            else
            {
                ctx.Db.Npc.Insert(t);
            }
        }
    }

    [Reducer]
    public static void NpcDealDamageToPlayer(ReducerContext ctx, ulong npcId, string targetIdentityHex, int amount, string damageType)
    {
        if (!IsService(ctx)) return;

        // Find player by identity
        foreach (var player in ctx.Db.Player.Iter())
        {
            if (player.Identity.ToString() != targetIdentityHex) continue;

            ctx.Db.DamageEvent.Insert(new DamageEvent
            {
                SourceId = npcId, SourceType = "npc",
                TargetId = 0, TargetType = "player",
                Amount = amount, DamageType = damageType,
                Timestamp = NowMs(ctx),
            });

            var updated = player;
            updated.Health = Math.Max(0f, player.Health - amount);
            // TODO: player death handling (respawn at spawn, etc.)
            ctx.Db.Player.Identity.Update(updated);
            return;
        }
    }

    private static void NpcDeathInternal(ReducerContext ctx, Npc npc)
    {
        // Roll loot table
        var rng = new Random((int)NowMs(ctx) ^ (int)npc.Id);
        foreach (var loot in ctx.Db.NpcLootTable.Iter())
        {
            if (loot.NpcType != npc.NpcType) continue;
            if (rng.NextDouble() > loot.DropChance) continue;
            ctx.Db.WorldItem.Insert(new WorldItem
            {
                ItemType = loot.ItemType,
                Quantity = (uint)loot.Quantity,
                PosX = npc.PosX, PosY = npc.PosY, PosZ = npc.PosZ,
            });
        }
        Log.Info($"[NpcReducers] NPC {npc.NpcType} (id={npc.Id}) died. Loot dropped.");
    }

    [Reducer]
    public static void NpcRespawn(ReducerContext ctx, ulong npcId)
    {
        if (!IsService(ctx)) return;
        var row = ctx.Db.Npc.Id.Find(npcId);
        if (row is null) return;
        var npc = row.Value;
        var cfg = FindNpcConfig(ctx, npc.NpcType);
        if (cfg is null) return;

        ctx.Db.Npc.Delete(npc);
        npc.Health = cfg.Value.MaxHealth;
        npc.PosX = npc.SpawnPosX; npc.PosY = npc.SpawnPosY; npc.PosZ = npc.SpawnPosZ;
        npc.IsAlive = true;
        npc.CurrentState = "idle";
        npc.TargetEntityId = 0;
        npc.TargetEntityType = "";
        npc.LastUpdateMs = NowMs(ctx);
        ctx.Db.Npc.Insert(npc);
    }

    [Reducer]
    public static void DespawnNpc(ReducerContext ctx, ulong npcId)
    {
        if (!IsService(ctx)) return;
        var row = ctx.Db.Npc.Id.Find(npcId);
        if (row is null) return;
        ctx.Db.Npc.Delete(row.Value);
    }

    [Reducer]
    public static void CleanupDamageEvents(ReducerContext ctx)
    {
        if (!IsService(ctx)) return;
        ulong cutoff = NowMs(ctx) - 30_000; // 30 seconds ago
        var toDelete = new System.Collections.Generic.List<DamageEvent>();
        foreach (var e in ctx.Db.DamageEvent.Iter())
            if (e.Timestamp < cutoff) toDelete.Add(e);
        foreach (var e in toDelete)
            ctx.Db.DamageEvent.Delete(e);
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add mods/npcs/server/NpcReducers.cs
git commit -m "feat(npcs): add NPC lifecycle reducers (service-authorized)"
```

### Task 4: Create combat reducers (player-facing)

**Files:**
- Create: `mods/npcs/server/CombatReducers.cs`

- [ ] **Step 1: Create player combat reducers**

```csharp
// mods/npcs/server/CombatReducers.cs
using SpacetimeDB;
using System;

namespace SandboxRPG.Server;

public static partial class Module
{
    [Reducer]
    public static void PlayerAttackNpc(ReducerContext ctx, ulong npcId)
    {
        var player = ctx.Db.Player.Identity.Find(ctx.Sender);
        if (player is null) return;
        var p = player.Value;

        var npcRow = ctx.Db.Npc.Id.Find(npcId);
        if (npcRow is null) return;
        var npc = npcRow.Value;

        if (!npc.IsAlive) return;

        // Check NPC is attackable
        var cfg = FindNpcConfig(ctx, npc.NpcType);
        if (cfg is null || !cfg.Value.IsAttackable) return;

        // Range check (3.0 units)
        float dx = p.PosX - npc.PosX;
        float dz = p.PosZ - npc.PosZ;
        float distSq = dx * dx + dz * dz;
        if (distSq > 3.0f * 3.0f) return;

        // Damage: 10 base (fists), 25 if player has iron_sword
        int damage = 10;
        foreach (var item in ctx.Db.InventoryItem.Iter())
        {
            if (item.OwnerId == ctx.Sender && item.ItemType == "iron_sword")
            { damage = 25; break; }
        }

        // Insert damage event
        ctx.Db.DamageEvent.Insert(new DamageEvent
        {
            SourceId = 0, SourceType = "player",
            TargetId = npcId, TargetType = "npc",
            Amount = damage, DamageType = "melee",
            Timestamp = NowMs(ctx),
        });

        // Apply damage
        ctx.Db.Npc.Delete(npc);
        npc.Health = Math.Max(0, npc.Health - damage);
        if (npc.Health <= 0)
        {
            npc.IsAlive = false;
            ctx.Db.Npc.Insert(npc);
            NpcDeathInternal(ctx, npc);
        }
        else
        {
            ctx.Db.Npc.Insert(npc);
        }
    }

    [Reducer]
    public static void PlayerAttackPlayer(ReducerContext ctx, string targetIdentityHex)
    {
        // PvP stub — not implemented yet
        Log.Warn($"[CombatReducers] PvP not implemented. {ctx.Sender} tried to attack {targetIdentityHex}");
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add mods/npcs/server/CombatReducers.cs
git commit -m "feat(npcs): add PlayerAttackNpc combat reducer"
```

### Task 5: Create trade reducer

**Files:**
- Create: `mods/npcs/server/TradeReducers.cs`

- [ ] **Step 1: Create trade reducer**

```csharp
// mods/npcs/server/TradeReducers.cs
using SpacetimeDB;

namespace SandboxRPG.Server;

public static partial class Module
{
    [Reducer]
    public static void NpcTrade(ReducerContext ctx, ulong npcId, string buyItemType, uint quantity)
    {
        var player = ctx.Db.Player.Identity.Find(ctx.Sender);
        if (player is null) return;
        var p = player.Value;

        var npcRow = ctx.Db.Npc.Id.Find(npcId);
        if (npcRow is null) return;
        var npc = npcRow.Value;

        if (!npc.IsAlive) return;

        // Check NPC is a trader
        var cfg = FindNpcConfig(ctx, npc.NpcType);
        if (cfg is null || !cfg.Value.IsTrader) return;

        // Range check (5.0 units)
        float dx = p.PosX - npc.PosX;
        float dz = p.PosZ - npc.PosZ;
        if (dx * dx + dz * dz > 5.0f * 5.0f) return;

        // Find trade offer
        NpcTradeOffer? offer = null;
        foreach (var o in ctx.Db.NpcTradeOffer.Iter())
        {
            if (o.NpcType == npc.NpcType && o.ItemType == buyItemType)
            { offer = o; break; }
        }
        if (offer is null) return;

        int totalCost = offer.Value.Price * (int)quantity;
        string currency = offer.Value.Currency;

        // Check player has enough currency
        uint currencyHeld = 0;
        foreach (var item in ctx.Db.InventoryItem.Iter())
            if (item.OwnerId == ctx.Sender && item.ItemType == currency)
                currencyHeld += item.Quantity;

        if (currencyHeld < (uint)totalCost)
        {
            Log.Warn($"[TradeReducers] Player lacks {currency}: has {currencyHeld}, needs {totalCost}");
            return;
        }

        // Deduct currency (consume from first matching stacks)
        int remaining = totalCost;
        var toDelete = new System.Collections.Generic.List<InventoryItem>();
        var toUpdate = new System.Collections.Generic.List<(InventoryItem old, uint newQty)>();
        foreach (var item in ctx.Db.InventoryItem.Iter())
        {
            if (remaining <= 0) break;
            if (item.OwnerId != ctx.Sender || item.ItemType != currency) continue;

            if (item.Quantity <= (uint)remaining)
            {
                remaining -= (int)item.Quantity;
                toDelete.Add(item);
            }
            else
            {
                toUpdate.Add((item, item.Quantity - (uint)remaining));
                remaining = 0;
            }
        }
        foreach (var item in toDelete) ctx.Db.InventoryItem.Delete(item);
        foreach (var (old, newQty) in toUpdate)
        {
            ctx.Db.InventoryItem.Delete(old);
            var updated = old;
            updated.Quantity = newQty;
            ctx.Db.InventoryItem.Insert(updated);
        }

        // Give purchased item
        ctx.Db.InventoryItem.Insert(new InventoryItem
        {
            OwnerId = ctx.Sender,
            ItemType = buyItemType,
            Quantity = quantity,
            Slot = -1,
        });

        Log.Info($"[TradeReducers] Player bought {quantity}x {buyItemType} for {totalCost} {currency}");
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add mods/npcs/server/TradeReducers.cs
git commit -m "feat(npcs): add NpcTrade reducer for NPC shops"
```

### Task 6: Wire NPC mod into server build

**Files:**
- Modify: `server/StdbModule.csproj`
- Modify: `server/Lifecycle.cs`

- [ ] **Step 1: Add NPC mod to server build**

In `server/StdbModule.csproj`, add this line in the mods `<ItemGroup>`:
```xml
    <Compile Include="../mods/npcs/server/**/*.cs" Exclude="../mods/npcs/server/tests/**/*.cs" />
```

- [ ] **Step 2: Add WASM static init to Lifecycle.cs**

In `server/Lifecycle.cs`, add `_ = _npcsMod;` inside Init, after `_ = _currencyMod;`:
```csharp
        _ = _npcsMod;
```

- [ ] **Step 3: Build the server to verify**

Run: `cd server && spacetime build`
Expected: Build succeeds

- [ ] **Step 4: Commit**

```bash
git add server/StdbModule.csproj server/Lifecycle.cs
git commit -m "feat(npcs): wire NPC mod into server build"
```

### Task 7: Build & publish server, regenerate bindings

- [ ] **Step 1: Re-authenticate (if server was restarted)**

```bash
spacetime logout && spacetime login --server-issued-login local --no-browser
```

- [ ] **Step 2: Build and publish**

```bash
cd server && spacetime build && spacetime publish -b bin/Release/net8.0/wasi-wasm/AppBundle/StdbModule.wasm
```

- [ ] **Step 3: Regenerate client bindings**

```bash
cd server && spacetime generate --lang csharp --out-dir ../client/scripts/networking/SpacetimeDB --bin-path bin/Release/net8.0/wasi-wasm/AppBundle/StdbModule.wasm
```

- [ ] **Step 4: Build client to verify bindings**

```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: Build succeeds (0 errors)

- [ ] **Step 5: Commit regenerated bindings**

```bash
git add client/scripts/networking/SpacetimeDB/
git commit -m "chore: regenerate STDB client bindings with NPC tables"
```

---

## Chunk 2: Client — GameManager, IAttackable, InteractionSystem

### Task 8: Add NPC signals and accessors to GameManager

**Files:**
- Modify: `client/scripts/networking/GameManager.cs`

- [ ] **Step 1: Add NPC signals**

After the existing `ContainerSlotChangedEventHandler` signal (~line 59), add:

```csharp
    [Signal] public delegate void NpcUpdatedEventHandler(long npcId, bool removed);
    [Signal] public delegate void DamageEventReceivedEventHandler(long eventId);
```

- [ ] **Step 2: Add NPC data accessors**

After the existing `GetAccessControl` method (~line 170), add:

```csharp
    public IEnumerable<Npc> GetAllNpcs() { if (Conn != null) foreach (var n in Conn.Db.Npc.Iter()) yield return n; }
    public Npc? GetNpc(ulong id) => Conn?.Db.Npc.Id.Find(id);
    public IEnumerable<DamageEvent> GetRecentDamageEvents() { if (Conn != null) foreach (var e in Conn.Db.DamageEvent.Iter()) yield return e; }
    public NpcConfig? GetNpcConfig(string npcType)
    {
        if (Conn == null) return null;
        foreach (var c in Conn.Db.NpcConfig.Iter())
            if (c.NpcType == npcType) return c;
        return null;
    }
    public IEnumerable<NpcTradeOffer> GetTradeOffers(string npcType)
    {
        if (Conn == null) yield break;
        foreach (var o in Conn.Db.NpcTradeOffer.Iter())
            if (o.NpcType == npcType) yield return o;
    }
```

- [ ] **Step 3: Add NPC reducer calls**

After the existing `ContainerTransfer` method (~line 125), add:

```csharp
    public void AttackNpc(ulong npcId) => Conn?.Reducers.PlayerAttackNpc(npcId);
    public void TradeWithNpc(ulong npcId, string itemType, uint qty) => Conn?.Reducers.NpcTrade(npcId, itemType, qty);
```

- [ ] **Step 4: Add NPC table callbacks in RegisterCallbacks**

After the `ContainerSlot` callbacks (~line 299), add:

```csharp
        conn.Db.Npc.OnInsert += (ctx, n) => CallDeferred(nameof(EmitNpcUpdated), (long)n.Id, false);
        conn.Db.Npc.OnUpdate += (ctx, _, n) => CallDeferred(nameof(EmitNpcUpdated), (long)n.Id, false);
        conn.Db.Npc.OnDelete += (ctx, n) => CallDeferred(nameof(EmitNpcUpdated), (long)n.Id, true);
        conn.Db.DamageEvent.OnInsert += (ctx, e) => CallDeferred(nameof(EmitDamageEventReceived), (long)e.Id);
```

- [ ] **Step 5: Add deferred signal emitters**

After the existing `EmitContainerSlotChanged` method (~line 313), add:

```csharp
    private void EmitNpcUpdated(long id, bool removed) => EmitSignal(SignalName.NpcUpdated, id, removed);
    private void EmitDamageEventReceived(long id) => EmitSignal(SignalName.DamageEventReceived, id);
```

- [ ] **Step 6: Build client to verify**

```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: 0 errors

- [ ] **Step 7: Commit**

```bash
git add client/scripts/networking/GameManager.cs
git commit -m "feat(npcs): add NPC signals, accessors, and reducer calls to GameManager"
```

### Task 9: Create IAttackable interface

**Files:**
- Create: `client/scripts/interaction/IAttackable.cs`

- [ ] **Step 1: Create IAttackable**

```csharp
// client/scripts/interaction/IAttackable.cs
using SpacetimeDB.Types;

namespace SandboxRPG;

public interface IAttackable
{
    string AttackHintText { get; }
    bool CanAttack(Player? player);
    void Attack(Player? player);
}
```

- [ ] **Step 2: Commit**

```bash
git add client/scripts/interaction/IAttackable.cs
git commit -m "feat(npcs): add IAttackable interface"
```

### Task 10: Extend InteractionSystem for IAttackable

**Files:**
- Modify: `client/mods/base/world/InteractionSystem.cs`

- [ ] **Step 1: Add IAttackable detection alongside IInteractable**

Replace the `_Process` method body (lines 28-62) with:

```csharp
    public override void _Process(double delta)
    {
        _camera ??= GetViewport()?.GetCamera3D();
        if (_camera == null) return;

        var spaceState = _camera.GetWorld3D()?.DirectSpaceState;
        if (spaceState == null) return;

        var screenCenter = GetViewport().GetVisibleRect().Size / 2;
        var from = _camera.ProjectRayOrigin(screenCenter);
        var to = from + _camera.ProjectRayNormal(screenCenter) * InteractionRange;

        var query = PhysicsRayQueryParameters3D.Create(from, to);
        query.CollisionMask = 3;
        var result = spaceState.IntersectRay(query);

        if (result.Count == 0 || !result.ContainsKey("collider"))
        { HideHint(); return; }

        var collider = result["collider"].As<Node>();
        if (collider == null) { HideHint(); return; }

        var interactable = FindInteractable(collider);
        var attackable = FindAttackable(collider);

        if (interactable == null && attackable == null) { HideHint(); return; }

        var player = GameManager.Instance.GetLocalPlayer();

        // Interaction takes priority for hint display
        if (interactable != null && interactable.CanInteract(player))
        {
            string hint = interactable.HintText;
            if (attackable != null && attackable.CanAttack(player))
                hint += $"\n{attackable.AttackHintText}";
            ShowHint(hint);

            if (Input.IsActionJustPressed(interactable.InteractAction))
                interactable.Interact(player);
            if (attackable != null && Input.IsActionJustPressed("primary_attack") && attackable.CanAttack(player))
                attackable.Attack(player);
        }
        else if (attackable != null && attackable.CanAttack(player))
        {
            ShowHint(attackable.AttackHintText);
            if (Input.IsActionJustPressed("primary_attack"))
                attackable.Attack(player);
        }
        else if (interactable != null)
        {
            ShowHint("[Private]");
        }
        else
        {
            HideHint();
        }
    }
```

- [ ] **Step 2: Add FindAttackable helper**

After the existing `FindInteractable` method, add:

```csharp
    private static IAttackable? FindAttackable(Node node)
    {
        Node? current = node;
        for (int i = 0; i < 4 && current != null; i++)
        {
            if (current is IAttackable attackable)
                return attackable;
            current = current.GetParent();
        }
        return null;
    }
```

- [ ] **Step 3: Build client to verify**

```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: 0 errors

- [ ] **Step 4: Commit**

```bash
git add client/mods/base/world/InteractionSystem.cs
git commit -m "feat(npcs): extend InteractionSystem with IAttackable support"
```

---

## Chunk 3: Client — NPC Rendering & Spawning

### Task 11: Create NPC visual registry

**Files:**
- Create: `client/mods/npcs/registries/NpcVisualRegistry.cs`

- [ ] **Step 1: Create registry**

```csharp
// client/mods/npcs/registries/NpcVisualRegistry.cs
using Godot;
using System.Collections.Generic;

namespace SandboxRPG;

public class NpcVisualDef
{
    public string? ModelPath { get; set; }
    public float Scale { get; set; } = 1.0f;
    public Color TintColor { get; set; } = Colors.White;
    public Color HealthBarColor { get; set; } = Colors.Red;
    public string DisplayName { get; set; } = "NPC";
}

public static class NpcVisualRegistry
{
    private static readonly Dictionary<string, NpcVisualDef> _defs = new();

    public static void Register(string npcType, NpcVisualDef def) => _defs[npcType] = def;
    public static NpcVisualDef? Get(string npcType) => _defs.TryGetValue(npcType, out var d) ? d : null;
}
```

- [ ] **Step 2: Commit**

```bash
git add client/mods/npcs/registries/NpcVisualRegistry.cs
git commit -m "feat(npcs): add NpcVisualRegistry"
```

### Task 12: Create DialogueRegistry and TradeRegistry

**Files:**
- Create: `client/mods/npcs/registries/DialogueRegistry.cs`
- Create: `client/mods/npcs/registries/TradeRegistry.cs`

- [ ] **Step 1: Create DialogueRegistry**

```csharp
// client/mods/npcs/registries/DialogueRegistry.cs
using System.Collections.Generic;

namespace SandboxRPG;

public static class DialogueRegistry
{
    private static readonly Dictionary<string, string[]> _dialogues = new();

    public static void Register(string npcType, string[] lines) => _dialogues[npcType] = lines;
    public static string[]? Get(string npcType) => _dialogues.TryGetValue(npcType, out var d) ? d : null;
}
```

- [ ] **Step 2: Create TradeRegistry**

```csharp
// client/mods/npcs/registries/TradeRegistry.cs
using System.Collections.Generic;

namespace SandboxRPG;

public class TradeDisplayInfo
{
    public string DisplayName { get; set; } = "";
}

public static class TradeRegistry
{
    private static readonly Dictionary<string, TradeDisplayInfo> _info = new();

    public static void Register(string itemType, TradeDisplayInfo info) => _info[itemType] = info;
    public static TradeDisplayInfo? Get(string itemType) => _info.TryGetValue(itemType, out var d) ? d : null;
}
```

- [ ] **Step 3: Commit**

```bash
git add client/mods/npcs/registries/DialogueRegistry.cs client/mods/npcs/registries/TradeRegistry.cs
git commit -m "feat(npcs): add DialogueRegistry and TradeRegistry"
```

### Task 13: Create NpcEntity (visual + interpolation + health bar)

**Files:**
- Create: `client/mods/npcs/NpcEntity.cs`

- [ ] **Step 1: Create NpcEntity**

```csharp
// client/mods/npcs/NpcEntity.cs
using Godot;
using SpacetimeDB.Types;

namespace SandboxRPG;

public partial class NpcEntity : StaticBody3D, IInteractable, IAttackable
{
    [Export] public float InterpolationSpeed = 10.0f;

    public ulong NpcId { get; set; }
    public string NpcType { get; set; } = "";
    public bool NpcIsAlive { get; set; } = true;
    public int NpcHealth { get; set; }
    public int NpcMaxHealth { get; set; }

    private Vector3 _targetPosition;
    private float _targetRotY;
    private Label3D _nameLabel = null!;
    private MeshInstance3D _mesh = null!;
    private StandardMaterial3D _material = null!;
    private ProgressBar? _healthBar;
    private SubViewport? _healthBarViewport;

    // IInteractable — for dialogue/trade NPCs
    public string HintText
    {
        get
        {
            var cfg = GameManager.Instance.GetNpcConfig(NpcType);
            if (cfg is null) return "";
            var visual = NpcVisualRegistry.Get(NpcType);
            string name = visual?.DisplayName ?? NpcType;
            if (cfg.IsTrader) return $"[E] Trade with {name}";
            if (cfg.HasDialogue) return $"[E] Talk to {name}";
            return "";
        }
    }

    public bool CanInteract(Player? player)
    {
        if (!NpcIsAlive) return false;
        var cfg = GameManager.Instance.GetNpcConfig(NpcType);
        return cfg is not null && (cfg.IsTrader || cfg.HasDialogue);
    }

    public void Interact(Player? player)
    {
        var cfg = GameManager.Instance.GetNpcConfig(NpcType);
        if (cfg is null) return;
        if (cfg.IsTrader)
            UIManager.Instance.Push(new NpcTradePanel(NpcId, NpcType));
        else if (cfg.HasDialogue)
            UIManager.Instance.Push(new NpcDialoguePanel(NpcType));
    }

    // IAttackable — for attackable NPCs
    public string AttackHintText
    {
        get
        {
            var visual = NpcVisualRegistry.Get(NpcType);
            string name = visual?.DisplayName ?? NpcType;
            return $"[LMB] Attack {name}";
        }
    }

    public bool CanAttack(Player? player)
    {
        if (!NpcIsAlive) return false;
        var cfg = GameManager.Instance.GetNpcConfig(NpcType);
        return cfg is not null && cfg.IsAttackable;
    }

    public void Attack(Player? player)
    {
        GameManager.Instance.AttackNpc(NpcId);
    }

    public override void _Ready()
    {
        var visual = NpcVisualRegistry.Get(NpcType);
        float scale = visual?.Scale ?? 1.0f;
        var tint = visual?.TintColor ?? Colors.Gray;

        // Mesh (capsule, like players)
        _mesh = new MeshInstance3D { Name = "NpcMesh" };
        _mesh.Mesh = new CapsuleMesh { Radius = 0.35f * scale, Height = 1.8f * scale };
        _mesh.Position = new Vector3(0, 0.9f * scale, 0);
        _material = new StandardMaterial3D { AlbedoColor = tint, Roughness = 0.8f };
        _mesh.MaterialOverride = _material;
        AddChild(_mesh);

        // Collision
        var collision = new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = 0.35f * scale, Height = 1.8f * scale },
            Position = new Vector3(0, 0.9f * scale, 0),
        };
        AddChild(collision);

        // Name label
        string displayName = visual?.DisplayName ?? NpcType;
        _nameLabel = new Label3D
        {
            Name = "NameLabel",
            Text = displayName,
            FontSize = 48,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Position = new Vector3(0, 2.2f * scale, 0),
        };
        AddChild(_nameLabel);

        // Health bar (3D billboard sprite using SubViewport)
        CreateHealthBar(scale);

        _targetPosition = GlobalPosition;
        AddToGroup("npc");
    }

    private void CreateHealthBar(float scale)
    {
        _healthBarViewport = new SubViewport
        {
            Size = new Vector2I(100, 12),
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        _healthBar = new ProgressBar
        {
            MinValue = 0, MaxValue = NpcMaxHealth, Value = NpcHealth,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(100, 12),
            Position = Vector2.Zero,
        };
        // Style the bar
        var bgStyle = new StyleBoxFlat { BgColor = new Color(0.2f, 0.2f, 0.2f, 0.8f) };
        var fillStyle = new StyleBoxFlat { BgColor = NpcVisualRegistry.Get(NpcType)?.HealthBarColor ?? Colors.Red };
        _healthBar.AddThemeStyleboxOverride("background", bgStyle);
        _healthBar.AddThemeStyleboxOverride("fill", fillStyle);
        _healthBarViewport.AddChild(_healthBar);
        AddChild(_healthBarViewport);

        var healthSprite = new Sprite3D
        {
            Name = "HealthBarSprite",
            Texture = _healthBarViewport.GetTexture(),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            PixelSize = 0.01f,
            Position = new Vector3(0, 2.0f * scale, 0),
            Visible = false, // hidden until damaged
        };
        AddChild(healthSprite);
    }

    public override void _Process(double delta)
    {
        if (!NpcIsAlive)
        {
            // Fade out on death
            if (_material.AlbedoColor.A > 0.01f)
            {
                var c = _material.AlbedoColor;
                c.A = Mathf.MoveToward(c.A, 0f, (float)delta * 2f);
                _material.AlbedoColor = c;
                _material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            }
            return;
        }

        GlobalPosition = GlobalPosition.Lerp(_targetPosition, (float)delta * InterpolationSpeed);
        float currentY = Rotation.Y;
        float diff = Mathf.Wrap(_targetRotY - currentY, -Mathf.Pi, Mathf.Pi);
        Rotation = new Vector3(0, currentY + diff * (float)delta * InterpolationSpeed, 0);
    }

    public void UpdateFromServer(Npc npc)
    {
        _targetPosition = new Vector3(npc.PosX, npc.PosY, npc.PosZ);
        _targetRotY = npc.RotY;
        NpcIsAlive = npc.IsAlive;
        NpcHealth = npc.Health;
        NpcMaxHealth = npc.MaxHealth;

        // Update health bar
        if (_healthBar != null)
        {
            _healthBar.MaxValue = npc.MaxHealth;
            _healthBar.Value = npc.Health;
        }
        // Show health bar only when damaged
        var healthSprite = GetNodeOrNull<Sprite3D>("HealthBarSprite");
        if (healthSprite != null)
            healthSprite.Visible = npc.IsAlive && npc.Health < npc.MaxHealth;
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add client/mods/npcs/NpcEntity.cs
git commit -m "feat(npcs): add NpcEntity with visual, interpolation, health bar, IInteractable/IAttackable"
```

### Task 14: Create NpcSpawner

**Files:**
- Create: `client/mods/npcs/NpcSpawner.cs`

- [ ] **Step 1: Create NpcSpawner**

```csharp
// client/mods/npcs/NpcSpawner.cs
using Godot;
using SpacetimeDB.Types;
using System.Collections.Generic;

namespace SandboxRPG;

public class NpcSpawner
{
    private readonly Node3D _parent;
    private readonly GameManager _gm;
    private readonly Dictionary<ulong, NpcEntity> _npcs = new();

    public NpcSpawner(Node3D parent, GameManager gm)
    {
        _parent = parent;
        _gm = gm;
    }

    public void SpawnAll()
    {
        foreach (var npc in _gm.GetAllNpcs())
        {
            if (!_npcs.ContainsKey(npc.Id))
                Spawn(npc);
        }
    }

    public void OnUpdated(long id, bool removed)
    {
        ulong uid = (ulong)id;
        if (removed)
        {
            Remove(uid);
            return;
        }

        var npc = _gm.GetNpc(uid);
        if (npc is null) return;

        if (_npcs.TryGetValue(uid, out var existing))
        {
            existing.UpdateFromServer(npc.Value);
        }
        else
        {
            Spawn(npc.Value);
        }
    }

    private void Spawn(Npc npc)
    {
        var entity = new NpcEntity
        {
            Name = $"Npc_{npc.NpcType}_{npc.Id}",
            NpcId = npc.Id,
            NpcType = npc.NpcType,
            NpcIsAlive = npc.IsAlive,
            NpcHealth = npc.Health,
            NpcMaxHealth = npc.MaxHealth,
        };
        _parent.AddChild(entity);

        float groundY = Terrain.HeightAt(npc.PosX, npc.PosZ);
        float y = npc.PosY > 0.01f ? npc.PosY : groundY;
        entity.GlobalPosition = new Vector3(npc.PosX, y, npc.PosZ);
        entity.Rotation = new Vector3(0, npc.RotY, 0);

        _npcs[npc.Id] = entity;
        GD.Print($"[NpcSpawner] Spawned {npc.NpcType} (id={npc.Id})");
    }

    private void Remove(ulong id)
    {
        if (_npcs.TryGetValue(id, out var entity))
        {
            entity.QueueFree();
            _npcs.Remove(id);
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add client/mods/npcs/NpcSpawner.cs
git commit -m "feat(npcs): add NpcSpawner for signal-driven NPC lifecycle"
```

### Task 15: Wire NpcSpawner into WorldManager

**Files:**
- Modify: `client/mods/base/world/WorldManager.cs`

- [ ] **Step 1: Add NpcSpawner field and wiring**

Add `_npcs` field after `_worldObjects`:
```csharp
    private NpcSpawner _npcs = null!;
```

In `_Ready()`, after `_worldObjects = new WorldObjectSpawner(this, gm);`:
```csharp
        _npcs = new NpcSpawner(this, gm);
```

After `gm.WorldObjectUpdated += _worldObjects.OnUpdated;`:
```csharp
        gm.NpcUpdated += _npcs.OnUpdated;
```

In `OnSubscriptionApplied()`, after `_worldObjects.SyncAll();`:
```csharp
        _npcs.SpawnAll();
```

- [ ] **Step 2: Build client to verify**

```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: 0 errors

- [ ] **Step 3: Commit**

```bash
git add client/mods/base/world/WorldManager.cs
git commit -m "feat(npcs): wire NpcSpawner into WorldManager"
```

---

## Chunk 4: Client — UI Panels & Content Registration

### Task 16: Create NpcDialoguePanel

**Files:**
- Create: `client/mods/npcs/ui/NpcDialoguePanel.cs`

- [ ] **Step 1: Create dialogue panel**

```csharp
// client/mods/npcs/ui/NpcDialoguePanel.cs
using Godot;

namespace SandboxRPG;

public partial class NpcDialoguePanel : BasePanel
{
    private readonly string _npcType;
    private int _currentLine;
    private Label _dialogueLabel = null!;
    private string[] _lines = System.Array.Empty<string>();

    public NpcDialoguePanel(string npcType)
    {
        _npcType = npcType;
    }

    protected override void BuildUI()
    {
        _lines = DialogueRegistry.Get(_npcType) ?? new[] { "..." };
        var visual = NpcVisualRegistry.Get(_npcType);
        string name = visual?.DisplayName ?? _npcType;

        var backdrop = new ColorRect { Color = new Color(0f, 0f, 0f, 0.55f) };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = UIFactory.MakePanel(new Vector2(500, 250));
        center.AddChild(panel);

        var vbox = UIFactory.MakeVBox(10);
        panel.AddChild(vbox);

        vbox.AddChild(UIFactory.MakeTitle(name, 20));
        vbox.AddChild(UIFactory.MakeSeparator());

        _dialogueLabel = UIFactory.MakeLabel(_lines[0], 16);
        _dialogueLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _dialogueLabel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        vbox.AddChild(_dialogueLabel);

        var btnRow = UIFactory.MakeHBox(10);
        btnRow.Alignment = BoxContainer.AlignmentMode.End;
        vbox.AddChild(btnRow);

        var nextBtn = UIFactory.MakeButton("Next", 14, new Vector2(80, 32));
        nextBtn.Pressed += OnNext;
        btnRow.AddChild(nextBtn);

        var closeBtn = UIFactory.MakeButton("Close", 14, new Vector2(80, 32));
        closeBtn.Pressed += () => UIManager.Instance.Pop();
        btnRow.AddChild(closeBtn);
    }

    private void OnNext()
    {
        _currentLine++;
        if (_currentLine >= _lines.Length)
            _currentLine = 0; // Loop back
        _dialogueLabel.Text = _lines[_currentLine];
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add client/mods/npcs/ui/NpcDialoguePanel.cs
git commit -m "feat(npcs): add NpcDialoguePanel"
```

### Task 17: Create NpcTradePanel

**Files:**
- Create: `client/mods/npcs/ui/NpcTradePanel.cs`

- [ ] **Step 1: Create trade panel**

```csharp
// client/mods/npcs/ui/NpcTradePanel.cs
using Godot;
using SpacetimeDB.Types;
using System.Linq;

namespace SandboxRPG;

public partial class NpcTradePanel : BasePanel
{
    private readonly ulong _npcId;
    private readonly string _npcType;
    private VBoxContainer _offerList = null!;
    private Label _currencyLabel = null!;

    public NpcTradePanel(ulong npcId, string npcType)
    {
        _npcId = npcId;
        _npcType = npcType;
    }

    public override void OnPushed()
    {
        base.OnPushed();
        GameManager.Instance.InventoryChanged += RefreshCurrency;
    }

    public override void OnPopped()
    {
        GameManager.Instance.InventoryChanged -= RefreshCurrency;
        base.OnPopped();
    }

    protected override void BuildUI()
    {
        var visual = NpcVisualRegistry.Get(_npcType);
        string name = visual?.DisplayName ?? _npcType;

        var backdrop = new ColorRect { Color = new Color(0f, 0f, 0f, 0.55f) };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = UIFactory.MakePanel(new Vector2(500, 400));
        center.AddChild(panel);

        var vbox = UIFactory.MakeVBox(10);
        panel.AddChild(vbox);

        var titleRow = UIFactory.MakeHBox(16);
        titleRow.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(titleRow);
        titleRow.AddChild(UIFactory.MakeTitle($"{name} - Shop", 20));
        var closeBtn = UIFactory.MakeButton("\u2715", 14, new Vector2(32, 32));
        closeBtn.Pressed += () => UIManager.Instance.Pop();
        titleRow.AddChild(closeBtn);

        vbox.AddChild(UIFactory.MakeSeparator());

        // Currency display
        _currencyLabel = UIFactory.MakeLabel("", 14, UIFactory.ColourMuted);
        vbox.AddChild(_currencyLabel);
        RefreshCurrency();

        // Offers scroll
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        vbox.AddChild(scroll);

        _offerList = UIFactory.MakeVBox(6);
        scroll.AddChild(_offerList);

        PopulateOffers();
    }

    private void PopulateOffers()
    {
        foreach (var child in _offerList.GetChildren())
            child.QueueFree();

        foreach (var offer in GameManager.Instance.GetTradeOffers(_npcType))
        {
            var row = UIFactory.MakeHBox(10);
            _offerList.AddChild(row);

            var itemDef = ItemRegistry.Get(offer.ItemType);
            string displayName = itemDef?.DisplayName ?? offer.ItemType.Replace('_', ' ');

            row.AddChild(UIFactory.MakeLabel(displayName, 14));
            row.AddChild(UIFactory.MakeLabel($"{offer.Price} {offer.Currency.Replace('_', ' ')}", 14, UIFactory.ColourMuted));

            var buyBtn = UIFactory.MakeButton("Buy", 12, new Vector2(60, 28));
            string itemType = offer.ItemType;
            buyBtn.Pressed += () => GameManager.Instance.TradeWithNpc(_npcId, itemType, 1);
            row.AddChild(buyBtn);
        }
    }

    private void RefreshCurrency()
    {
        uint coins = 0;
        foreach (var item in GameManager.Instance.GetMyInventory())
            if (item.ItemType == "copper_coin") coins += item.Quantity;
        if (_currencyLabel != null)
            _currencyLabel.Text = $"Your copper coins: {coins}";
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add client/mods/npcs/ui/NpcTradePanel.cs
git commit -m "feat(npcs): add NpcTradePanel"
```

### Task 18: Create DamageNumberEffect

**Files:**
- Create: `client/mods/npcs/ui/DamageNumberEffect.cs`

- [ ] **Step 1: Create floating damage numbers**

```csharp
// client/mods/npcs/ui/DamageNumberEffect.cs
using Godot;

namespace SandboxRPG;

public partial class DamageNumberEffect : Label3D
{
    private float _age;
    private Vector3 _velocity;

    public static DamageNumberEffect Create(int amount, Vector3 worldPos)
    {
        var effect = new DamageNumberEffect
        {
            Text = amount.ToString(),
            FontSize = 36,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            GlobalPosition = worldPos + new Vector3(0, 2.5f, 0),
            Modulate = amount > 15 ? Colors.Orange : Colors.White,
        };
        effect._velocity = new Vector3(
            (float)GD.RandRange(-0.5, 0.5),
            2.0f,
            (float)GD.RandRange(-0.5, 0.5)
        );
        return effect;
    }

    public override void _Process(double delta)
    {
        _age += (float)delta;
        GlobalPosition += _velocity * (float)delta;
        _velocity.Y -= 3.0f * (float)delta; // gravity

        // Fade out
        var c = Modulate;
        c.A = Mathf.MoveToward(c.A, 0f, (float)delta * 1.5f);
        Modulate = c;

        if (_age > 1.5f)
            QueueFree();
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add client/mods/npcs/ui/DamageNumberEffect.cs
git commit -m "feat(npcs): add DamageNumberEffect floating numbers"
```

### Task 19: Create NpcContent and NpcsClientMod

**Files:**
- Create: `client/mods/npcs/content/NpcContent.cs`
- Create: `client/mods/npcs/NpcsClientMod.cs`

- [ ] **Step 1: Create NpcContent**

```csharp
// client/mods/npcs/content/NpcContent.cs
using Godot;

namespace SandboxRPG;

public static class NpcContent
{
    public static void RegisterAll()
    {
        RegisterVisuals();
        RegisterDialogues();
        RegisterItems();
    }

    private static void RegisterVisuals()
    {
        NpcVisualRegistry.Register("wolf", new NpcVisualDef
        {
            DisplayName = "Wolf", Scale = 0.8f,
            TintColor = new Color(0.5f, 0.5f, 0.5f),
            HealthBarColor = Colors.Red,
        });
        NpcVisualRegistry.Register("merchant", new NpcVisualDef
        {
            DisplayName = "Merchant", Scale = 1.0f,
            TintColor = new Color(0.2f, 0.7f, 0.3f),
            HealthBarColor = Colors.Green,
        });
        NpcVisualRegistry.Register("guard", new NpcVisualDef
        {
            DisplayName = "Guard", Scale = 1.1f,
            TintColor = new Color(0.3f, 0.4f, 0.8f),
            HealthBarColor = Colors.Blue,
        });
    }

    private static void RegisterDialogues()
    {
        DialogueRegistry.Register("merchant", new[]
        {
            "Welcome, traveler!",
            "Browse my wares.",
            "Only the finest goods here.",
        });
        DialogueRegistry.Register("guard", new[]
        {
            "Move along, citizen.",
            "The town is safe under my watch.",
            "Report any suspicious activity.",
        });
    }

    private static void RegisterItems()
    {
        // New items for NPC system
        ItemRegistry.Register("iron_sword",     new ItemDef { DisplayName = "Iron Sword",     MaxStack = 1 });
        ItemRegistry.Register("health_potion",  new ItemDef { DisplayName = "Health Potion",  MaxStack = 10, TintColor = new Color(0.9f, 0.2f, 0.2f) });
        ItemRegistry.Register("raw_meat",       new ItemDef { DisplayName = "Raw Meat",       MaxStack = 20, TintColor = new Color(0.8f, 0.3f, 0.3f) });
        ItemRegistry.Register("wolf_pelt",      new ItemDef { DisplayName = "Wolf Pelt",      MaxStack = 10, TintColor = new Color(0.6f, 0.5f, 0.4f) });
        ItemRegistry.Register("bread",          new ItemDef { DisplayName = "Bread",          MaxStack = 20, TintColor = new Color(0.9f, 0.8f, 0.5f) });
    }
}
```

- [ ] **Step 2: Create NpcsClientMod**

```csharp
// client/mods/npcs/NpcsClientMod.cs
using Godot;

namespace SandboxRPG;

public partial class NpcsClientMod : Node, IClientMod
{
    public string ModName => "npcs";
    public string[] Dependencies => new[] { "base" };

    public override void _Ready() => ModManager.Register(this);

    public void Initialize(Node sceneRoot)
    {
        NpcContent.RegisterAll();
        SetupDamageNumbers(sceneRoot);
        GD.Print("[NpcsClientMod] NPC content registered.");
    }

    private void SetupDamageNumbers(Node sceneRoot)
    {
        GameManager.Instance.DamageEventReceived += (long eventId) =>
        {
            var events = GameManager.Instance.GetRecentDamageEvents();
            foreach (var evt in events)
            {
                if ((long)evt.Id != eventId) continue;

                Vector3 pos;
                if (evt.TargetType == "npc")
                {
                    var npc = GameManager.Instance.GetNpc(evt.TargetId);
                    if (npc is null) break;
                    pos = new Vector3(npc.Value.PosX, npc.Value.PosY, npc.Value.PosZ);
                }
                else
                {
                    // Player damage — find player position
                    // For now, just skip (player health bars show damage)
                    break;
                }

                var effect = DamageNumberEffect.Create(evt.Amount, pos);
                sceneRoot.AddChild(effect);
                break;
            }
        };
    }
}
```

- [ ] **Step 3: Add NpcsClientMod as autoload in project.godot**

In `client/project.godot`, in the `[autoload]` section, add after `GameManager` (NpcsClientMod needs GameManager to be initialized first):
```ini
NpcsClientMod="*res://mods/npcs/NpcsClientMod.cs"
```

- [ ] **Step 4: Build client to verify**

```bash
cd client && dotnet build SandboxRPG.csproj
```
Expected: 0 errors

- [ ] **Step 5: Commit**

```bash
git add client/mods/npcs/ client/project.godot
git commit -m "feat(npcs): add NpcsClientMod, NpcContent, DamageNumberEffect, and autoload"
```

---

## Chunk 5: Game Service

### Task 20: Create Game Service project

**Files:**
- Create: `service/GameService.csproj`
- Create: `service/IServiceMod.cs`
- Create: `service/ServiceModLoader.cs`

- [ ] **Step 1: Create the project file**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>SandboxRPG.Service</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="SpacetimeDB.ClientSDK" Version="2.0.*" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create IServiceMod**

```csharp
// service/IServiceMod.cs
namespace SandboxRPG.Service;

public interface IServiceMod
{
    string Name { get; }
    string Version { get; }
    string[] Dependencies { get; }
    void Initialize(ServiceContext ctx);
}
```

- [ ] **Step 3: Create ServiceModLoader**

```csharp
// service/ServiceModLoader.cs
namespace SandboxRPG.Service;

public static class ServiceModLoader
{
    private static readonly List<IServiceMod> _mods = new();
    private static List<IServiceMod>? _sorted;

    public static void Register(IServiceMod mod) => _mods.Add(mod);

    public static void InitializeAll(ServiceContext ctx)
    {
        _sorted = TopoSort(_mods);
        foreach (var mod in _sorted)
        {
            Console.WriteLine($"[ServiceModLoader] Initializing: {mod.Name} v{mod.Version}");
            mod.Initialize(ctx);
        }
    }

    private static List<IServiceMod> TopoSort(List<IServiceMod> mods)
    {
        var byName = mods.ToDictionary(m => m.Name);
        var inDegree = mods.ToDictionary(m => m.Name, _ => 0);

        foreach (var mod in mods)
            foreach (var dep in mod.Dependencies)
                if (byName.ContainsKey(dep))
                    inDegree[mod.Name]++;

        var queue = new Queue<IServiceMod>(mods.Where(m => inDegree[m.Name] == 0));
        var result = new List<IServiceMod>();

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
            throw new InvalidOperationException("Circular dependency in service mods.");

        return result;
    }
}
```

- [ ] **Step 4: Commit**

```bash
git add service/GameService.csproj service/IServiceMod.cs service/ServiceModLoader.cs
git commit -m "feat(service): create Game Service project with mod loader"
```

### Task 21: Create NPC config and context

**Files:**
- Create: `service/NpcConfig.cs`
- Create: `service/NpcContext.cs`

- [ ] **Step 1: Create NpcConfig**

```csharp
// service/NpcConfig.cs
namespace SandboxRPG.Service;

public class NpcStateTransition
{
    public string TargetState { get; set; } = "";
    public string Condition { get; set; } = "";
    public float Param { get; set; }

    public NpcStateTransition(string targetState, string condition, float param = 0f)
    {
        TargetState = targetState;
        Condition = condition;
        Param = param;
    }
}

public class NpcStateConfig
{
    public string[] Behaviors { get; set; } = Array.Empty<string>();
    public List<NpcStateTransition> Transitions { get; set; } = new();
}

public class ServiceNpcConfig
{
    public int MaxHealth { get; set; }
    public int AttackDamage { get; set; }
    public float AttackRange { get; set; }
    public ulong AttackCooldownMs { get; set; }
    public float MoveSpeed { get; set; } = 3.0f;
    public float AggroRange { get; set; }
    public float LeashRange { get; set; } = 30f;
    public Dictionary<string, NpcStateConfig> States { get; set; } = new();
}

public static class NpcConfigRegistry
{
    private static readonly Dictionary<string, ServiceNpcConfig> _configs = new();

    public static void Register(string npcType, ServiceNpcConfig config) => _configs[npcType] = config;
    public static ServiceNpcConfig? Get(string npcType) => _configs.TryGetValue(npcType, out var c) ? c : null;
    public static IEnumerable<string> AllTypes => _configs.Keys;
}
```

- [ ] **Step 2: Create NpcContext**

```csharp
// service/NpcContext.cs
using SpacetimeDB.Types;

namespace SandboxRPG.Service;

public class NpcContext
{
    public ulong NpcId { get; set; }
    public string NpcType { get; set; } = "";
    public float PosX { get; set; }
    public float PosY { get; set; }
    public float PosZ { get; set; }
    public float RotY { get; set; }
    public int Health { get; set; }
    public int MaxHealth { get; set; }
    public string CurrentState { get; set; } = "";
    public ulong TargetEntityId { get; set; }
    public string TargetEntityType { get; set; } = "";
    public float SpawnPosX { get; set; }
    public float SpawnPosY { get; set; }
    public float SpawnPosZ { get; set; }
    public ServiceNpcConfig Config { get; set; } = null!;

    // Ephemeral state
    public ulong LastAttackMs { get; set; }
    public ulong LastDamagedMs { get; set; }
    public string TargetIdentityHex { get; set; } = "";
    public float WanderTargetX { get; set; }
    public float WanderTargetZ { get; set; }
    public bool HasWanderTarget { get; set; }

    // World access
    public Func<IEnumerable<Player>> GetPlayers { get; set; } = () => Enumerable.Empty<Player>();

    // Reducer calls
    public Action<ulong, float, float, float, float> MoveNpc { get; set; } = (_, _, _, _, _) => { };
    public Action<ulong, string, ulong, string> SetState { get; set; } = (_, _, _, _) => { };
    public Action<ulong, ulong, string, int, string> DealDamage { get; set; } = (_, _, _, _, _) => { };
    public Action<ulong, string, int, string> DealDamageToPlayer { get; set; } = (_, _, _, _) => { };

    public float DistanceTo(float x, float z)
    {
        float dx = PosX - x;
        float dz = PosZ - z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    public float DistanceToSpawn() => DistanceTo(SpawnPosX, SpawnPosZ);
}
```

- [ ] **Step 3: Create ServiceContext**

```csharp
// service/ServiceContext.cs
namespace SandboxRPG.Service;

public class ServiceContext
{
    // Placeholder — expanded as needed by service mods
}
```

- [ ] **Step 4: Commit**

```bash
git add service/NpcConfig.cs service/NpcContext.cs service/ServiceContext.cs
git commit -m "feat(service): add NPC config, context, and service context"
```

### Task 22: Create behavior system

**Files:**
- Create: `service/behaviors/INpcBehavior.cs`
- Create: `service/behaviors/BehaviorRegistry.cs`
- Create: `service/behaviors/IdleBehavior.cs`
- Create: `service/behaviors/WanderBehavior.cs`
- Create: `service/behaviors/ChaseBehavior.cs`
- Create: `service/behaviors/FleeBehavior.cs`
- Create: `service/behaviors/MeleeAttackBehavior.cs`
- Create: `service/behaviors/ReturnToSpawnBehavior.cs`
- Create: `service/behaviors/PatrolBehavior.cs`

- [ ] **Step 1: Create interface and registry**

```csharp
// service/behaviors/INpcBehavior.cs
namespace SandboxRPG.Service;

public interface INpcBehavior
{
    string Name { get; }
    void Tick(NpcContext ctx, float delta);
}
```

```csharp
// service/behaviors/BehaviorRegistry.cs
namespace SandboxRPG.Service;

public static class BehaviorRegistry
{
    private static readonly Dictionary<string, INpcBehavior> _behaviors = new();

    public static void Register(INpcBehavior behavior) => _behaviors[behavior.Name] = behavior;
    public static INpcBehavior? Get(string name) => _behaviors.TryGetValue(name, out var b) ? b : null;

    public static void RegisterBuiltIns()
    {
        Register(new IdleBehavior());
        Register(new WanderBehavior());
        Register(new ChaseBehavior());
        Register(new FleeBehavior());
        Register(new MeleeAttackBehavior());
        Register(new ReturnToSpawnBehavior());
        Register(new PatrolBehavior());
    }
}
```

- [ ] **Step 2: Create all built-in behaviors**

```csharp
// service/behaviors/IdleBehavior.cs
namespace SandboxRPG.Service;

public class IdleBehavior : INpcBehavior
{
    public string Name => "idle";
    public void Tick(NpcContext ctx, float delta) { /* stand still */ }
}
```

```csharp
// service/behaviors/WanderBehavior.cs
namespace SandboxRPG.Service;

public class WanderBehavior : INpcBehavior
{
    public string Name => "wander";
    private static readonly Random _rng = new();

    public void Tick(NpcContext ctx, float delta)
    {
        if (!ctx.HasWanderTarget || ctx.DistanceTo(ctx.WanderTargetX, ctx.WanderTargetZ) < 1.0f)
        {
            // Pick new random target within leash range
            float angle = (float)(_rng.NextDouble() * Math.PI * 2);
            float dist = (float)(_rng.NextDouble() * ctx.Config.LeashRange * 0.5f);
            ctx.WanderTargetX = ctx.SpawnPosX + MathF.Cos(angle) * dist;
            ctx.WanderTargetZ = ctx.SpawnPosZ + MathF.Sin(angle) * dist;
            ctx.HasWanderTarget = true;
        }

        MoveToward(ctx, ctx.WanderTargetX, ctx.WanderTargetZ, ctx.Config.MoveSpeed * 0.5f, delta);
    }

    private static void MoveToward(NpcContext ctx, float tx, float tz, float speed, float delta)
    {
        float dx = tx - ctx.PosX;
        float dz = tz - ctx.PosZ;
        float dist = MathF.Sqrt(dx * dx + dz * dz);
        if (dist < 0.1f) return;

        float step = MathF.Min(speed * delta, dist);
        float nx = ctx.PosX + (dx / dist) * step;
        float nz = ctx.PosZ + (dz / dist) * step;
        float rotY = MathF.Atan2(dx, dz);

        ctx.MoveNpc(ctx.NpcId, nx, ctx.PosY, nz, rotY);
        ctx.PosX = nx;
        ctx.PosZ = nz;
        ctx.RotY = rotY;
    }
}
```

```csharp
// service/behaviors/ChaseBehavior.cs
using SpacetimeDB.Types;

namespace SandboxRPG.Service;

public class ChaseBehavior : INpcBehavior
{
    public string Name => "chase";

    public void Tick(NpcContext ctx, float delta)
    {
        // Find target: if we have a stored target identity hex, chase that player.
        // Otherwise chase nearest player.
        float tx = ctx.PosX, tz = ctx.PosZ;
        bool found = false;

        if (!string.IsNullOrEmpty(ctx.TargetIdentityHex))
        {
            foreach (var p in ctx.GetPlayers())
            {
                if (!p.IsOnline) continue;
                if (p.Identity.ToString() == ctx.TargetIdentityHex)
                {
                    tx = p.PosX; tz = p.PosZ; found = true; break;
                }
            }
        }

        if (!found)
        {
            // Fallback: nearest player
            float closestDist = float.MaxValue;
            foreach (var p in ctx.GetPlayers())
            {
                if (!p.IsOnline) continue;
                float d = ctx.DistanceTo(p.PosX, p.PosZ);
                if (d < closestDist) { closestDist = d; tx = p.PosX; tz = p.PosZ; found = true; }
            }
        }

        if (!found) return;
        if (ctx.DistanceTo(tx, tz) > ctx.Config.LeashRange) return;

        float dx = tx - ctx.PosX;
        float dz = tz - ctx.PosZ;
        float dist = MathF.Sqrt(dx * dx + dz * dz);
        if (dist < ctx.Config.AttackRange) return; // close enough, let attack behavior handle it

        float step = MathF.Min(ctx.Config.MoveSpeed * delta, dist);
        float nx = ctx.PosX + (dx / dist) * step;
        float nz = ctx.PosZ + (dz / dist) * step;
        float rotY = MathF.Atan2(dx, dz);

        ctx.MoveNpc(ctx.NpcId, nx, ctx.PosY, nz, rotY);
        ctx.PosX = nx;
        ctx.PosZ = nz;
        ctx.RotY = rotY;
    }
}
```

```csharp
// service/behaviors/FleeBehavior.cs
namespace SandboxRPG.Service;

public class FleeBehavior : INpcBehavior
{
    public string Name => "flee";

    public void Tick(NpcContext ctx, float delta)
    {
        // Flee from nearest player
        float closestDist = float.MaxValue;
        float tx = ctx.PosX, tz = ctx.PosZ;
        foreach (var p in ctx.GetPlayers())
        {
            if (!p.IsOnline) continue;
            float d = ctx.DistanceTo(p.PosX, p.PosZ);
            if (d < closestDist) { closestDist = d; tx = p.PosX; tz = p.PosZ; }
        }

        float dx = ctx.PosX - tx; // away from player
        float dz = ctx.PosZ - tz;
        float dist = MathF.Sqrt(dx * dx + dz * dz);
        if (dist < 0.1f) return;

        float step = ctx.Config.MoveSpeed * delta;
        float nx = ctx.PosX + (dx / dist) * step;
        float nz = ctx.PosZ + (dz / dist) * step;
        float rotY = MathF.Atan2(-dx, -dz); // face away

        ctx.MoveNpc(ctx.NpcId, nx, ctx.PosY, nz, rotY);
        ctx.PosX = nx;
        ctx.PosZ = nz;
        ctx.RotY = rotY;
    }
}
```

```csharp
// service/behaviors/MeleeAttackBehavior.cs
namespace SandboxRPG.Service;

public class MeleeAttackBehavior : INpcBehavior
{
    public string Name => "melee_attack";

    public void Tick(NpcContext ctx, float delta)
    {
        ulong nowMs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (nowMs - ctx.LastAttackMs < ctx.Config.AttackCooldownMs) return;

        // Find nearest player in range
        foreach (var p in ctx.GetPlayers())
        {
            if (!p.IsOnline) continue;
            float dist = ctx.DistanceTo(p.PosX, p.PosZ);
            if (dist <= ctx.Config.AttackRange)
            {
                ctx.DealDamageToPlayer(ctx.NpcId, p.Identity.ToString(), ctx.Config.AttackDamage, "melee");
                ctx.LastAttackMs = nowMs;
                return;
            }
        }
    }
}
```

```csharp
// service/behaviors/ReturnToSpawnBehavior.cs
namespace SandboxRPG.Service;

public class ReturnToSpawnBehavior : INpcBehavior
{
    public string Name => "return_to_spawn";

    public void Tick(NpcContext ctx, float delta)
    {
        float dx = ctx.SpawnPosX - ctx.PosX;
        float dz = ctx.SpawnPosZ - ctx.PosZ;
        float dist = MathF.Sqrt(dx * dx + dz * dz);
        if (dist < 1.0f) return;

        float step = MathF.Min(ctx.Config.MoveSpeed * delta, dist);
        float nx = ctx.PosX + (dx / dist) * step;
        float nz = ctx.PosZ + (dz / dist) * step;
        float rotY = MathF.Atan2(dx, dz);

        ctx.MoveNpc(ctx.NpcId, nx, ctx.PosY, nz, rotY);
        ctx.PosX = nx;
        ctx.PosZ = nz;
        ctx.RotY = rotY;
    }
}
```

```csharp
// service/behaviors/PatrolBehavior.cs
namespace SandboxRPG.Service;

public class PatrolBehavior : INpcBehavior
{
    public string Name => "patrol";
    private static readonly Random _rng = new();

    public void Tick(NpcContext ctx, float delta)
    {
        // Simple patrol: wander in a small radius around spawn
        if (!ctx.HasWanderTarget || ctx.DistanceTo(ctx.WanderTargetX, ctx.WanderTargetZ) < 1.0f)
        {
            var rng = _rng;
            float angle = (float)(rng.NextDouble() * Math.PI * 2);
            float dist = (float)(rng.NextDouble() * 5f); // 5 unit patrol radius
            ctx.WanderTargetX = ctx.SpawnPosX + MathF.Cos(angle) * dist;
            ctx.WanderTargetZ = ctx.SpawnPosZ + MathF.Sin(angle) * dist;
            ctx.HasWanderTarget = true;
        }

        float dx = ctx.WanderTargetX - ctx.PosX;
        float dz = ctx.WanderTargetZ - ctx.PosZ;
        float d = MathF.Sqrt(dx * dx + dz * dz);
        if (d < 0.1f) return;

        float step = MathF.Min(ctx.Config.MoveSpeed * 0.4f * delta, d);
        float nx = ctx.PosX + (dx / d) * step;
        float nz = ctx.PosZ + (dz / d) * step;
        float rotY = MathF.Atan2(dx, dz);

        ctx.MoveNpc(ctx.NpcId, nx, ctx.PosY, nz, rotY);
        ctx.PosX = nx;
        ctx.PosZ = nz;
        ctx.RotY = rotY;
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add service/behaviors/
git commit -m "feat(service): add behavior system with 7 built-in NPC behaviors"
```

### Task 23: Create condition system

**Files:**
- Create: `service/conditions/ITransitionCondition.cs`
- Create: `service/conditions/ConditionRegistry.cs`
- Create: `service/conditions/BuiltInConditions.cs`

- [ ] **Step 1: Create condition interface and registry**

```csharp
// service/conditions/ITransitionCondition.cs
namespace SandboxRPG.Service;

public interface ITransitionCondition
{
    string Name { get; }
    bool Evaluate(NpcContext ctx, float param);
}
```

```csharp
// service/conditions/ConditionRegistry.cs
namespace SandboxRPG.Service;

public static class ConditionRegistry
{
    private static readonly Dictionary<string, ITransitionCondition> _conditions = new();

    public static void Register(ITransitionCondition condition) => _conditions[condition.Name] = condition;
    public static ITransitionCondition? Get(string name) => _conditions.TryGetValue(name, out var c) ? c : null;

    public static void RegisterBuiltIns()
    {
        Register(new PlayerInRangeCondition());
        Register(new TargetLostCondition());
        Register(new LeashRangeCondition());
        Register(new HealthBelowCondition());
        Register(new TargetInRangeCondition());
        Register(new NoTargetCondition());
        Register(new WasAttackedCondition());
        Register(new HostileNpcInRangeCondition());
        Register(new NearSpawnCondition());
    }
}
```

- [ ] **Step 2: Create built-in conditions**

```csharp
// service/conditions/BuiltInConditions.cs
namespace SandboxRPG.Service;

public class PlayerInRangeCondition : ITransitionCondition
{
    public string Name => "player_in_range";
    public bool Evaluate(NpcContext ctx, float range)
    {
        foreach (var p in ctx.GetPlayers())
        {
            if (!p.IsOnline) continue;
            if (ctx.DistanceTo(p.PosX, p.PosZ) <= range) return true;
        }
        return false;
    }
}

public class TargetLostCondition : ITransitionCondition
{
    public string Name => "target_lost";
    public bool Evaluate(NpcContext ctx, float _)
    {
        if (string.IsNullOrEmpty(ctx.TargetEntityType)) return true;
        if (ctx.TargetEntityType == "player")
        {
            foreach (var p in ctx.GetPlayers())
                if (p.IsOnline && ctx.DistanceTo(p.PosX, p.PosZ) <= ctx.Config.LeashRange)
                    return false;
            return true;
        }
        return true;
    }
}

public class LeashRangeCondition : ITransitionCondition
{
    public string Name => "leash_range";
    public bool Evaluate(NpcContext ctx, float range)
    {
        return ctx.DistanceToSpawn() > range;
    }
}

public class HealthBelowCondition : ITransitionCondition
{
    public string Name => "health_below";
    public bool Evaluate(NpcContext ctx, float percent)
    {
        if (ctx.MaxHealth <= 0) return false;
        return ((float)ctx.Health / ctx.MaxHealth) < (percent / 100f);
    }
}

public class TargetInRangeCondition : ITransitionCondition
{
    public string Name => "target_in_range";
    public bool Evaluate(NpcContext ctx, float range)
    {
        foreach (var p in ctx.GetPlayers())
        {
            if (!p.IsOnline) continue;
            if (ctx.DistanceTo(p.PosX, p.PosZ) <= range) return true;
        }
        return false;
    }
}

public class NoTargetCondition : ITransitionCondition
{
    public string Name => "no_target";
    public bool Evaluate(NpcContext ctx, float _)
    {
        return string.IsNullOrEmpty(ctx.TargetEntityType) || ctx.TargetEntityId == 0;
    }
}

public class WasAttackedCondition : ITransitionCondition
{
    public string Name => "was_attacked";
    public bool Evaluate(NpcContext ctx, float _)
    {
        // True if NPC received damage within the last 5 seconds
        ulong nowMs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return ctx.LastDamagedMs > 0 && (nowMs - ctx.LastDamagedMs) < 5000;
    }
}

public class HostileNpcInRangeCondition : ITransitionCondition
{
    public string Name => "hostile_npc_in_range";
    public bool Evaluate(NpcContext ctx, float range)
    {
        // Stub: always false for now (NPC-vs-NPC aggro is future work)
        return false;
    }
}

public class NearSpawnCondition : ITransitionCondition
{
    public string Name => "near_spawn";
    public bool Evaluate(NpcContext ctx, float range)
    {
        return ctx.DistanceToSpawn() <= range;
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add service/conditions/
git commit -m "feat(service): add transition condition system with 8 built-in conditions"
```

### Task 24: Create StateMachineRunner and NpcBrain

**Files:**
- Create: `service/StateMachineRunner.cs`
- Create: `service/NpcBrain.cs`

- [ ] **Step 1: Create StateMachineRunner**

```csharp
// service/StateMachineRunner.cs
namespace SandboxRPG.Service;

public static class StateMachineRunner
{
    public static string? EvaluateTransitions(NpcContext ctx, NpcStateConfig stateConfig)
    {
        foreach (var transition in stateConfig.Transitions)
        {
            var condition = ConditionRegistry.Get(transition.Condition);
            if (condition == null)
            {
                Console.WriteLine($"[StateMachine] Unknown condition: {transition.Condition}");
                continue;
            }
            if (condition.Evaluate(ctx, transition.Param))
                return transition.TargetState;
        }
        return null;
    }

    public static void ExecuteBehaviors(NpcContext ctx, NpcStateConfig stateConfig, float delta)
    {
        foreach (var behaviorName in stateConfig.Behaviors)
        {
            var behavior = BehaviorRegistry.Get(behaviorName);
            if (behavior == null)
            {
                Console.WriteLine($"[StateMachine] Unknown behavior: {behaviorName}");
                continue;
            }
            behavior.Tick(ctx, delta);
        }
    }
}
```

- [ ] **Step 2: Create NpcBrain**

```csharp
// service/NpcBrain.cs
using SpacetimeDB.Types;

namespace SandboxRPG.Service;

public class NpcBrain
{
    public ulong NpcId { get; }
    public string NpcType { get; }
    private readonly NpcContext _ctx;
    private readonly ServiceNpcConfig _config;

    public NpcBrain(Npc npc, ServiceNpcConfig config,
        Func<IEnumerable<Player>> getPlayers,
        Action<ulong, float, float, float, float> moveNpc,
        Action<ulong, string, ulong, string> setState,
        Action<ulong, ulong, string, int, string> dealDamage,
        Action<ulong, string, int, string> dealDamageToPlayer)
    {
        NpcId = npc.Id;
        NpcType = npc.NpcType;
        _config = config;

        _ctx = new NpcContext
        {
            NpcId = npc.Id,
            NpcType = npc.NpcType,
            PosX = npc.PosX, PosY = npc.PosY, PosZ = npc.PosZ, RotY = npc.RotY,
            Health = npc.Health, MaxHealth = npc.MaxHealth,
            CurrentState = npc.CurrentState,
            TargetEntityId = npc.TargetEntityId,
            TargetEntityType = npc.TargetEntityType ?? "",
            SpawnPosX = npc.SpawnPosX, SpawnPosY = npc.SpawnPosY, SpawnPosZ = npc.SpawnPosZ,
            Config = config,
            GetPlayers = getPlayers,
            MoveNpc = moveNpc,
            SetState = setState,
            DealDamage = dealDamage,
            DealDamageToPlayer = dealDamageToPlayer,
        };
    }

    public void UpdateFromServer(Npc npc)
    {
        // Track damage for was_attacked condition
        if (npc.Health < _ctx.Health)
            _ctx.LastDamagedMs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        _ctx.PosX = npc.PosX; _ctx.PosY = npc.PosY; _ctx.PosZ = npc.PosZ;
        _ctx.RotY = npc.RotY;
        _ctx.Health = npc.Health; _ctx.MaxHealth = npc.MaxHealth;
        _ctx.CurrentState = npc.CurrentState;
        _ctx.TargetEntityId = npc.TargetEntityId;
        _ctx.TargetEntityType = npc.TargetEntityType ?? "";
    }

    public void Tick(float delta)
    {
        if (!_config.States.TryGetValue(_ctx.CurrentState, out var stateConfig))
            return;

        // Check transitions
        var newState = StateMachineRunner.EvaluateTransitions(_ctx, stateConfig);
        if (newState != null && newState != _ctx.CurrentState)
        {
            _ctx.CurrentState = newState;

            // Find target for combat states
            ulong targetId = 0;
            string targetType = "";
            if (newState == "combat")
            {
                // Target nearest online player
                float closest = float.MaxValue;
                string closestHex = "";
                foreach (var p in _ctx.GetPlayers())
                {
                    if (!p.IsOnline) continue;
                    float d = _ctx.DistanceTo(p.PosX, p.PosZ);
                    if (d < closest)
                    {
                        closest = d;
                        targetType = "player";
                        closestHex = p.Identity.ToString();
                    }
                }
                _ctx.TargetIdentityHex = closestHex;
            }
            else
            {
                _ctx.TargetIdentityHex = "";
            }

            _ctx.SetState(_ctx.NpcId, newState, targetId, targetType);

            // Re-fetch state config for new state
            if (!_config.States.TryGetValue(newState, out stateConfig))
                return;
        }

        // Execute behaviors
        StateMachineRunner.ExecuteBehaviors(_ctx, stateConfig, delta);
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add service/StateMachineRunner.cs service/NpcBrain.cs
git commit -m "feat(service): add StateMachineRunner and NpcBrain"
```

### Task 25: Create SpawnManager

**Files:**
- Create: `service/SpawnManager.cs`

- [ ] **Step 1: Create SpawnManager**

```csharp
// service/SpawnManager.cs
using SpacetimeDB.Types;

namespace SandboxRPG.Service;

public class SpawnManager
{
    private readonly Func<IEnumerable<NpcSpawnRule>> _getSpawnRules;
    private readonly Func<IEnumerable<Npc>> _getNpcs;
    private readonly Action<string, float, float, float, float> _spawnNpc;
    private readonly Action<ulong> _respawnNpc;
    private readonly Random _rng = new();

    // Track death times for respawn delay
    private readonly Dictionary<ulong, DateTimeOffset> _deathTimes = new();
    // Track pending spawns to avoid over-spawning before subscription confirms
    private readonly Dictionary<string, int> _pendingSpawns = new();

    public SpawnManager(
        Func<IEnumerable<NpcSpawnRule>> getSpawnRules,
        Func<IEnumerable<Npc>> getNpcs,
        Action<string, float, float, float, float> spawnNpc,
        Action<ulong> respawnNpc)
    {
        _getSpawnRules = getSpawnRules;
        _getNpcs = getNpcs;
        _spawnNpc = spawnNpc;
        _respawnNpc = respawnNpc;
    }

    public void Tick()
    {
        var npcs = _getNpcs().ToList();
        var now = DateTimeOffset.UtcNow;

        foreach (var rule in _getSpawnRules())
        {
            // Count alive NPCs of this type in this zone
            int aliveCount = 0;
            var deadInZone = new List<Npc>();
            string ruleKey = $"{rule.NpcType}_{rule.ZoneX}_{rule.ZoneZ}";
            int pending = _pendingSpawns.GetValueOrDefault(ruleKey, 0);

            foreach (var npc in npcs)
            {
                if (npc.NpcType != rule.NpcType) continue;
                float dx = npc.SpawnPosX - rule.ZoneX;
                float dz = npc.SpawnPosZ - rule.ZoneZ;
                float distSq = dx * dx + dz * dz;
                float maxDist = rule.ZoneRadius > 0 ? rule.ZoneRadius + 5f : 5f;
                if (distSq > maxDist * maxDist) continue;

                if (npc.IsAlive)
                    aliveCount++;
                else
                    deadInZone.Add(npc);
            }

            // Reduce pending count as NPCs appear
            if (pending > 0 && aliveCount > 0)
            {
                int confirmed = Math.Min(pending, aliveCount);
                _pendingSpawns[ruleKey] = pending - confirmed;
                pending = _pendingSpawns[ruleKey];
            }
            aliveCount += pending; // include pending in count to prevent over-spawn

            // Respawn dead NPCs after delay
            foreach (var dead in deadInZone)
            {
                if (aliveCount >= rule.MaxCount) break;

                if (!_deathTimes.ContainsKey(dead.Id))
                {
                    _deathTimes[dead.Id] = now;
                    continue;
                }

                if ((now - _deathTimes[dead.Id]).TotalSeconds >= rule.RespawnTimeSec)
                {
                    _respawnNpc(dead.Id);
                    _deathTimes.Remove(dead.Id);
                    aliveCount++;
                }
            }

            // Spawn new NPCs if under max and no dead to respawn
            while (aliveCount < rule.MaxCount)
            {
                float x, z;
                if (rule.ZoneRadius > 0)
                {
                    float angle = (float)(_rng.NextDouble() * Math.PI * 2);
                    float dist = (float)(_rng.NextDouble() * rule.ZoneRadius);
                    x = rule.ZoneX + MathF.Cos(angle) * dist;
                    z = rule.ZoneZ + MathF.Sin(angle) * dist;
                }
                else
                {
                    x = rule.ZoneX;
                    z = rule.ZoneZ;
                }

                float rotY = (float)(_rng.NextDouble() * Math.PI * 2);
                _spawnNpc(rule.NpcType, x, 0f, z, rotY);
                _pendingSpawns[ruleKey] = _pendingSpawns.GetValueOrDefault(ruleKey, 0) + 1;
                aliveCount++;
            }
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add service/SpawnManager.cs
git commit -m "feat(service): add SpawnManager for NPC spawn rule evaluation"
```

### Task 26: Create NPC service mod (example configs)

**Files:**
- Create: `service/mods/npcs/NpcServiceMod.cs`

- [ ] **Step 1: Create NPC service mod**

```csharp
// service/mods/npcs/NpcServiceMod.cs
namespace SandboxRPG.Service;

public class NpcServiceMod : IServiceMod
{
    public string Name => "npcs";
    public string Version => "1.0.0";
    public string[] Dependencies => Array.Empty<string>();

    public void Initialize(ServiceContext ctx)
    {
        NpcConfigRegistry.Register("wolf", new ServiceNpcConfig
        {
            MaxHealth = 50,
            AttackDamage = 8,
            AttackRange = 2.0f,
            AttackCooldownMs = 1500,
            MoveSpeed = 4.0f,
            AggroRange = 10f,
            LeashRange = 30f,
            States = new()
            {
                ["idle"] = new NpcStateConfig
                {
                    Behaviors = new[] { "wander" },
                    Transitions = new() { new("combat", "player_in_range", 10f) },
                },
                ["combat"] = new NpcStateConfig
                {
                    Behaviors = new[] { "chase", "melee_attack" },
                    Transitions = new()
                    {
                        new("idle", "target_lost"),
                        new("idle", "leash_range", 30f),
                    },
                },
            },
        });

        NpcConfigRegistry.Register("merchant", new ServiceNpcConfig
        {
            MaxHealth = 100,
            AttackDamage = 0,
            AttackRange = 0f,
            AttackCooldownMs = 0,
            MoveSpeed = 0f,
            AggroRange = 0f,
            LeashRange = 5f,
            States = new()
            {
                ["idle"] = new NpcStateConfig
                {
                    Behaviors = new[] { "idle" },
                    Transitions = new(),
                },
            },
        });

        NpcConfigRegistry.Register("guard", new ServiceNpcConfig
        {
            MaxHealth = 150,
            AttackDamage = 15,
            AttackRange = 2.5f,
            AttackCooldownMs = 1200,
            MoveSpeed = 3.5f,
            AggroRange = 15f,
            LeashRange = 20f,
            States = new()
            {
                ["idle"] = new NpcStateConfig
                {
                    Behaviors = new[] { "patrol" },
                    Transitions = new()
                    {
                        new("combat", "was_attacked"),
                        new("combat", "hostile_npc_in_range", 15f),
                    },
                },
                ["combat"] = new NpcStateConfig
                {
                    Behaviors = new[] { "chase", "melee_attack" },
                    Transitions = new()
                    {
                        new("return", "target_lost"),
                        new("return", "leash_range", 20f),
                    },
                },
                ["return"] = new NpcStateConfig
                {
                    Behaviors = new[] { "return_to_spawn" },
                    Transitions = new()
                    {
                        new("idle", "near_spawn", 2f), // within 2 units of spawn = arrived
                    },
                },
            },
        });

        Console.WriteLine("[NpcServiceMod] Registered wolf, merchant, guard configs.");
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add service/mods/npcs/NpcServiceMod.cs
git commit -m "feat(service): add NPC service mod with wolf, merchant, guard configs"
```

### Task 27: Create GameService entry point

**Files:**
- Create: `service/GameService.cs`

NOTE: The Game Service needs auto-generated SpacetimeDB bindings. Before running, generate bindings:
```bash
cd server && spacetime generate --lang csharp --out-dir ../service/SpacetimeDB --bin-path bin/Release/net8.0/wasi-wasm/AppBundle/StdbModule.wasm
```

- [ ] **Step 1: Generate service bindings**

```bash
cd server && spacetime generate --lang csharp --out-dir ../service/SpacetimeDB --bin-path bin/Release/net8.0/wasi-wasm/AppBundle/StdbModule.wasm
```

- [ ] **Step 2: Create GameService entry point**

```csharp
// service/GameService.cs
using SpacetimeDB;
using SpacetimeDB.Types;

namespace SandboxRPG.Service;

public static class GameService
{
    private static DbConnection? _conn;
    private static bool _subscriptionApplied;
    private static readonly Dictionary<ulong, NpcBrain> _brains = new();
    private static SpawnManager? _spawnManager;
    private static ulong _lastCleanupMs;

    private const int TickRateMs = 100; // 10 ticks/sec

    public static async Task Main(string[] args)
    {
        string url = args.Length > 0 ? args[0] : "http://127.0.0.1:3000";
        string dbName = args.Length > 1 ? args[1] : "sandbox-rpg";

        Console.WriteLine($"[GameService] Connecting to {url} database={dbName}...");

        // Register built-in systems
        BehaviorRegistry.RegisterBuiltIns();
        ConditionRegistry.RegisterBuiltIns();

        // Register and init service mods
        ServiceModLoader.Register(new NpcServiceMod());
        ServiceModLoader.InitializeAll(new ServiceContext());

        // Connect to SpacetimeDB
        _conn = DbConnection.Builder()
            .WithUri(url)
            .WithDatabaseName(dbName)
            .OnConnect(OnConnected)
            .OnConnectError(OnConnectError)
            .OnDisconnect(OnDisconnected)
            .Build();

        // Main loop
        while (true)
        {
            _conn.FrameTick();

            if (_subscriptionApplied)
            {
                Tick();
            }

            await Task.Delay(TickRateMs);
        }
    }

    private static void OnConnected(DbConnection conn, Identity identity, string token)
    {
        Console.WriteLine($"[GameService] Connected! Identity: {identity}");

        // Register as service
        conn.Reducers.RegisterServiceIdentity();

        conn.SubscriptionBuilder()
            .OnApplied(_ =>
            {
                Console.WriteLine("[GameService] Subscription applied.");
                _subscriptionApplied = true;
                InitSpawnManager();
            })
            .OnError((_, err) => Console.WriteLine($"[GameService] Sub error: {err}"))
            .SubscribeToAllTables();
    }

    private static void OnConnectError(Exception err)
    {
        Console.WriteLine($"[GameService] Connection error: {err.Message}");
    }

    private static void OnDisconnected(DbConnection conn, Exception? err)
    {
        Console.WriteLine($"[GameService] Disconnected: {err?.Message ?? "clean"}");
        _subscriptionApplied = false;
    }

    private static void InitSpawnManager()
    {
        _spawnManager = new SpawnManager(
            () => _conn!.Db.NpcSpawnRule.Iter(),
            () => _conn!.Db.Npc.Iter(),
            (type, x, y, z, rotY) => _conn!.Reducers.SpawnNpc(type, x, y, z, rotY),
            (id) => _conn!.Reducers.NpcRespawn(id)
        );
    }

    private static void Tick()
    {
        float delta = TickRateMs / 1000f;

        // Spawn/respawn NPCs
        _spawnManager?.Tick();

        // Update brains
        var currentNpcs = new HashSet<ulong>();
        foreach (var npc in _conn!.Db.Npc.Iter())
        {
            currentNpcs.Add(npc.Id);

            if (!npc.IsAlive) continue;

            if (!_brains.TryGetValue(npc.Id, out var brain))
            {
                var config = NpcConfigRegistry.Get(npc.NpcType);
                if (config == null) continue;

                brain = new NpcBrain(npc, config,
                    () => _conn.Db.Player.Iter(),
                    (id, x, y, z, r) => _conn.Reducers.NpcMove(id, x, y, z, r),
                    (id, s, tid, tt) => _conn.Reducers.NpcSetState(id, s, tid, tt),
                    (id, tid, tt, amt, dt) => _conn.Reducers.NpcDealDamage(id, tid, tt, amt, dt),
                    (id, tHex, amt, dt) => _conn.Reducers.NpcDealDamageToPlayer(id, tHex, amt, dt)
                );
                _brains[npc.Id] = brain;
            }
            else
            {
                brain.UpdateFromServer(npc);
            }

            brain.Tick(delta);
        }

        // Cleanup brains for deleted NPCs
        var toRemove = _brains.Keys.Where(id => !currentNpcs.Contains(id)).ToList();
        foreach (var id in toRemove) _brains.Remove(id);

        // Periodic damage event cleanup (every 30 seconds)
        ulong nowMs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (nowMs - _lastCleanupMs > 30_000)
        {
            _conn.Reducers.CleanupDamageEvents();
            _lastCleanupMs = nowMs;
        }
    }
}
```

- [ ] **Step 3: Build the service**

```bash
cd service && dotnet build
```
Expected: Build succeeds

- [ ] **Step 4: Commit**

```bash
git add service/
git commit -m "feat(service): add GameService entry point with tick loop and NPC AI"
```

---

## Chunk 6: Integration & Verification

### Task 28: Full build verification

- [ ] **Step 1: Build server**

```bash
cd server && spacetime build
```

- [ ] **Step 2: Build client**

```bash
cd client && dotnet build SandboxRPG.csproj
```

- [ ] **Step 3: Build service**

```bash
cd service && dotnet build
```

- [ ] **Step 4: Fix any compilation errors**

Address any build errors across all three projects.

- [ ] **Step 5: Commit any fixes**

```bash
git add -A
git commit -m "fix: resolve build errors across server, client, and service"
```

### Task 29: End-to-end test

- [ ] **Step 1: Start SpacetimeDB**

```bash
spacetime start --in-memory
```

- [ ] **Step 2: Re-login and publish**

```bash
spacetime logout && spacetime login --server-issued-login local --no-browser
cd server && spacetime build && spacetime publish -b bin/Release/net8.0/wasi-wasm/AppBundle/StdbModule.wasm
```

- [ ] **Step 3: Start the Game Service**

```bash
cd service && dotnet run
```
Expected: Connects, registers service identity, starts spawning NPCs

- [ ] **Step 4: Start Godot client**

Launch the Godot editor and run the game. Verify:
- NPCs appear in the world (wolf, merchant, guard with colored capsules)
- Wolves wander and chase when you approach
- Merchant shows trade UI on interaction
- Guard patrols near spawn
- Attacking a wolf shows damage numbers and it eventually dies/drops loot
- Dead wolves respawn after 30 seconds

- [ ] **Step 5: Final commit**

```bash
git add -A
git commit -m "feat(npcs): complete NPC system — server, service, client with 3 example NPCs"
```
