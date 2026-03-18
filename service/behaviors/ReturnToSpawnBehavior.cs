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
