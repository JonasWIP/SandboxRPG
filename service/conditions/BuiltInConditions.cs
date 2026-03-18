// service/conditions/BuiltInConditions.cs
// NOTE: requires generated bindings — Player type comes from SpacetimeDB.Types
// TODO: replace IPlayerData with Player (SpacetimeDB.Types) when bindings are generated

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
