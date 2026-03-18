// service/NpcBrain.cs
using SpacetimeDB.Types;

namespace SandboxRPG.Service;

public class NpcBrain
{
    public ulong NpcId { get; }
    public string NpcType { get; }
    private readonly NpcContext _ctx;
    private readonly ServiceNpcConfig _config;

    public NpcBrain(Npc npc, ServiceNpcConfig config,
        Func<IEnumerable<Player>> getPlayers,
        Action<ulong, float, float, float, float> moveNpc,
        Action<ulong, string, ulong, string> setState,
        Action<ulong, ulong, string, int, string> dealDamage,
        Action<ulong, string, int, string> dealDamageToPlayer)
    {
        NpcId = npc.Id;
        NpcType = npc.NpcType;
        _config = config;

        _ctx = new NpcContext
        {
            NpcId = npc.Id,
            NpcType = npc.NpcType,
            PosX = npc.PosX, PosY = npc.PosY, PosZ = npc.PosZ, RotY = npc.RotY,
            Health = npc.Health, MaxHealth = npc.MaxHealth,
            CurrentState = npc.CurrentState,
            TargetEntityId = npc.TargetEntityId,
            TargetEntityType = npc.TargetEntityType ?? "",
            SpawnPosX = npc.SpawnPosX, SpawnPosY = npc.SpawnPosY, SpawnPosZ = npc.SpawnPosZ,
            Config = config,
            GetPlayers = getPlayers,
            MoveNpc = moveNpc,
            SetState = setState,
            DealDamage = dealDamage,
            DealDamageToPlayer = dealDamageToPlayer,
        };
    }

    public void UpdateFromServer(Npc npc)
    {
        // Track damage for was_attacked condition
        if (npc.Health < _ctx.Health)
            _ctx.LastDamagedMs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        _ctx.PosX = npc.PosX; _ctx.PosY = npc.PosY; _ctx.PosZ = npc.PosZ;
        _ctx.RotY = npc.RotY;
        _ctx.Health = npc.Health; _ctx.MaxHealth = npc.MaxHealth;
        _ctx.CurrentState = npc.CurrentState;
        _ctx.TargetEntityId = npc.TargetEntityId;
        _ctx.TargetEntityType = npc.TargetEntityType ?? "";
    }

    public void Tick(float delta)
    {
        if (!_config.States.TryGetValue(_ctx.CurrentState, out var stateConfig))
            return;

        // Check transitions
        var newState = StateMachineRunner.EvaluateTransitions(_ctx, stateConfig);
        if (newState != null && newState != _ctx.CurrentState)
        {
            _ctx.CurrentState = newState;

            // Find target for combat states
            ulong targetId = 0;
            string targetType = "";
            if (newState == "combat")
            {
                // Target nearest online player
                float closest = float.MaxValue;
                string closestHex = "";
                foreach (var p in _ctx.GetPlayers())
                {
                    if (!p.IsOnline) continue;
                    float d = _ctx.DistanceTo(p.PosX, p.PosZ);
                    if (d < closest)
                    {
                        closest = d;
                        targetType = "player";
                        closestHex = p.Identity.ToString();
                    }
                }
                _ctx.TargetIdentityHex = closestHex;
            }
            else
            {
                _ctx.TargetIdentityHex = "";
            }

            _ctx.SetState(_ctx.NpcId, newState, targetId, targetType);

            // Re-fetch state config for new state
            if (!_config.States.TryGetValue(newState, out stateConfig))
                return;
        }

        // Execute behaviors
        StateMachineRunner.ExecuteBehaviors(_ctx, stateConfig, delta);
    }
}
