// service/behaviors/INpcBehavior.cs
namespace SandboxRPG.Service;

public interface INpcBehavior
{
    string Name { get; }
    void Tick(NpcContext ctx, float delta);
}
