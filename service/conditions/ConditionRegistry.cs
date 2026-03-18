// service/conditions/ConditionRegistry.cs
namespace SandboxRPG.Service;

public static class ConditionRegistry
{
    private static readonly Dictionary<string, ITransitionCondition> _conditions = new();

    public static void Register(ITransitionCondition condition) => _conditions[condition.Name] = condition;
    public static ITransitionCondition? Get(string name) => _conditions.TryGetValue(name, out var c) ? c : null;

    public static void RegisterBuiltIns()
    {
        Register(new PlayerInRangeCondition());
        Register(new TargetLostCondition());
        Register(new LeashRangeCondition());
        Register(new HealthBelowCondition());
        Register(new TargetInRangeCondition());
        Register(new NoTargetCondition());
        Register(new WasAttackedCondition());
        Register(new HostileNpcInRangeCondition());
        Register(new NearSpawnCondition());
    }
}
