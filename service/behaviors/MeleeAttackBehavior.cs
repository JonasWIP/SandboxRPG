// service/behaviors/MeleeAttackBehavior.cs
using SpacetimeDB.Types;

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
