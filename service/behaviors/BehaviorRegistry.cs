// service/behaviors/BehaviorRegistry.cs
namespace SandboxRPG.Service;

public static class BehaviorRegistry
{
    private static readonly Dictionary<string, INpcBehavior> _behaviors = new();

    public static void Register(INpcBehavior behavior) => _behaviors[behavior.Name] = behavior;
    public static INpcBehavior? Get(string name) => _behaviors.TryGetValue(name, out var b) ? b : null;

    public static void RegisterBuiltIns()
    {
        Register(new IdleBehavior());
        Register(new WanderBehavior());
        Register(new ChaseBehavior());
        Register(new FleeBehavior());
        Register(new MeleeAttackBehavior());
        Register(new ReturnToSpawnBehavior());
        Register(new PatrolBehavior());
    }
}
