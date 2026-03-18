// service/mods/npcs/NpcServiceMod.cs
namespace SandboxRPG.Service;

public class NpcServiceMod : IServiceMod
{
    public string Name => "npcs";
    public string Version => "1.0.0";
    public string[] Dependencies => Array.Empty<string>();

    public void Initialize(ServiceContext ctx)
    {
        NpcConfigRegistry.Register("wolf", new ServiceNpcConfig
        {
            MaxHealth = 50,
            AttackDamage = 8,
            AttackRange = 2.0f,
            AttackCooldownMs = 1500,
            MoveSpeed = 4.0f,
            AggroRange = 10f,
            LeashRange = 30f,
            States = new()
            {
                ["idle"] = new NpcStateConfig
                {
                    Behaviors = new[] { "wander" },
                    Transitions = new() { new("combat", "player_in_range", 10f) },
                },
                ["combat"] = new NpcStateConfig
                {
                    Behaviors = new[] { "chase", "melee_attack" },
                    Transitions = new()
                    {
                        new("idle", "target_lost"),
                        new("idle", "leash_range", 30f),
                    },
                },
            },
        });

        NpcConfigRegistry.Register("merchant", new ServiceNpcConfig
        {
            MaxHealth = 100,
            AttackDamage = 0,
            AttackRange = 0f,
            AttackCooldownMs = 0,
            MoveSpeed = 0f,
            AggroRange = 0f,
            LeashRange = 5f,
            States = new()
            {
                ["idle"] = new NpcStateConfig
                {
                    Behaviors = new[] { "idle" },
                    Transitions = new(),
                },
            },
        });

        NpcConfigRegistry.Register("guard", new ServiceNpcConfig
        {
            MaxHealth = 150,
            AttackDamage = 15,
            AttackRange = 2.5f,
            AttackCooldownMs = 1200,
            MoveSpeed = 3.5f,
            AggroRange = 15f,
            LeashRange = 20f,
            States = new()
            {
                ["idle"] = new NpcStateConfig
                {
                    Behaviors = new[] { "patrol" },
                    Transitions = new()
                    {
                        new("combat", "was_attacked"),
                        new("combat", "hostile_npc_in_range", 15f),
                    },
                },
                ["combat"] = new NpcStateConfig
                {
                    Behaviors = new[] { "chase", "melee_attack" },
                    Transitions = new()
                    {
                        new("return", "target_lost"),
                        new("return", "leash_range", 20f),
                    },
                },
                ["return"] = new NpcStateConfig
                {
                    Behaviors = new[] { "return_to_spawn" },
                    Transitions = new()
                    {
                        new("idle", "near_spawn", 2f), // within 2 units of spawn = arrived
                    },
                },
            },
        });

        Console.WriteLine("[NpcServiceMod] Registered wolf, merchant, guard configs.");
    }
}
