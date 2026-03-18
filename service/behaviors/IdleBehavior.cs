// service/behaviors/IdleBehavior.cs
namespace SandboxRPG.Service;

public class IdleBehavior : INpcBehavior
{
    public string Name => "idle";
    public void Tick(NpcContext ctx, float delta) { /* stand still */ }
}
