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
