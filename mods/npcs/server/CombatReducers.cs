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
        npc.Health = Math.Max(0, npc.Health - damage);
        if (npc.Health <= 0)
        {
            npc.IsAlive = false;
            ctx.Db.Npc.Id.Update(npc);
            NpcDeathInternal(ctx, npc);
        }
        else
        {
            ctx.Db.Npc.Id.Update(npc);
        }
    }

    [Reducer]
    public static void PlayerAttackPlayer(ReducerContext ctx, string targetIdentityHex)
    {
        // PvP stub — not implemented yet
        Log.Warn($"[CombatReducers] PvP not implemented. {ctx.Sender} tried to attack {targetIdentityHex}");
    }
}
