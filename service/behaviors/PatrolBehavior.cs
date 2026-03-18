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
