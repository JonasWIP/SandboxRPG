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

    // NowMs is defined in server/Helpers.cs (shared across all partial class Module files)

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
        npc.PosX = x; npc.PosY = y; npc.PosZ = z; npc.RotY = rotY;
        npc.LastUpdateMs = NowMs(ctx);
        ctx.Db.Npc.Id.Update(npc);
    }

    [Reducer]
    public static void NpcSetState(ReducerContext ctx, ulong npcId, string state, ulong targetEntityId, string targetEntityType)
    {
        if (!IsService(ctx)) return;
        var row = ctx.Db.Npc.Id.Find(npcId);
        if (row is null) return;
        var npc = row.Value;
        npc.CurrentState = state;
        npc.TargetEntityId = targetEntityId;
        npc.TargetEntityType = targetEntityType;
        npc.LastUpdateMs = NowMs(ctx);
        ctx.Db.Npc.Id.Update(npc);
    }

    [Reducer]
    public static void NpcDealDamage(ReducerContext ctx, ulong npcId, ulong targetId, string targetType, int amount, string damageType)
    {
        if (!IsService(ctx)) return;

        // Player damage is handled by NpcDealDamageToPlayer (players are keyed by Identity, not ulong)
        if (targetType == "player") return;

        // Insert damage event
        ctx.Db.DamageEvent.Insert(new DamageEvent
        {
            SourceId = npcId, SourceType = "npc",
            TargetId = targetId, TargetType = targetType,
            Amount = amount, DamageType = damageType,
            Timestamp = NowMs(ctx),
        });

        if (targetType == "npc")
        {
            var target = ctx.Db.Npc.Id.Find(targetId);
            if (target is null) return;
            var t = target.Value;
            t.Health = Math.Max(0, t.Health - amount);
            if (t.Health <= 0)
            {
                t.IsAlive = false;
                ctx.Db.Npc.Id.Update(t);
                NpcDeathInternal(ctx, t);
            }
            else
            {
                ctx.Db.Npc.Id.Update(t);
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

        npc.Health = cfg.Value.MaxHealth;
        npc.PosX = npc.SpawnPosX; npc.PosY = npc.SpawnPosY; npc.PosZ = npc.SpawnPosZ;
        npc.IsAlive = true;
        npc.CurrentState = "idle";
        npc.TargetEntityId = 0;
        npc.TargetEntityType = "";
        npc.LastUpdateMs = NowMs(ctx);
        ctx.Db.Npc.Id.Update(npc);
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
        ulong cutoff = NowMs(ctx) - GameConstants.DamageEventTtlMs;
        var toDelete = new System.Collections.Generic.List<DamageEvent>();
        foreach (var e in ctx.Db.DamageEvent.Iter())
            if (e.Timestamp < cutoff) toDelete.Add(e);
        foreach (var e in toDelete)
            ctx.Db.DamageEvent.Delete(e);
    }
}
