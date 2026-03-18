// service/NpcConfig.cs
namespace SandboxRPG.Service;

public class NpcStateTransition
{
    public string TargetState { get; set; } = "";
    public string Condition { get; set; } = "";
    public float Param { get; set; }

    public NpcStateTransition(string targetState, string condition, float param = 0f)
    {
        TargetState = targetState;
        Condition = condition;
        Param = param;
    }
}

public class NpcStateConfig
{
    public string[] Behaviors { get; set; } = Array.Empty<string>();
    public List<NpcStateTransition> Transitions { get; set; } = new();
}

public class ServiceNpcConfig
{
    public int MaxHealth { get; set; }
    public int AttackDamage { get; set; }
    public float AttackRange { get; set; }
    public ulong AttackCooldownMs { get; set; }
    public float MoveSpeed { get; set; } = 3.0f;
    public float AggroRange { get; set; }
    public float LeashRange { get; set; } = 30f;
    public Dictionary<string, NpcStateConfig> States { get; set; } = new();
}

public static class NpcConfigRegistry
{
    private static readonly Dictionary<string, ServiceNpcConfig> _configs = new();

    public static void Register(string npcType, ServiceNpcConfig config) => _configs[npcType] = config;
    public static ServiceNpcConfig? Get(string npcType) => _configs.TryGetValue(npcType, out var c) ? c : null;
    public static IEnumerable<string> AllTypes => _configs.Keys;
}
