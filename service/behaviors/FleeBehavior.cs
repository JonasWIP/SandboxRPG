// service/behaviors/FleeBehavior.cs
// NOTE: requires generated bindings — Player type comes from SpacetimeDB.Types
// TODO: replace IPlayerData with Player (SpacetimeDB.Types) when bindings are generated

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
