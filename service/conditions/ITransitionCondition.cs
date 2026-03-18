// service/conditions/ITransitionCondition.cs
namespace SandboxRPG.Service;

public interface ITransitionCondition
{
    string Name { get; }
    bool Evaluate(NpcContext ctx, float param);
}
