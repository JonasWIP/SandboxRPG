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
