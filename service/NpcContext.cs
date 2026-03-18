// service/NpcContext.cs
using SpacetimeDB.Types;

namespace SandboxRPG.Service;

public class NpcContext
{
    public ulong NpcId { get; set; }
    public string NpcType { get; set; } = "";
    public float PosX { get; set; }
    public float PosY { get; set; }
    public float PosZ { get; set; }
    public float RotY { get; set; }
    public int Health { get; set; }
    public int MaxHealth { get; set; }
    public string CurrentState { get; set; } = "";
    public ulong TargetEntityId { get; set; }
    public string TargetEntityType { get; set; } = "";
    public float SpawnPosX { get; set; }
    public float SpawnPosY { get; set; }
    public float SpawnPosZ { get; set; }
    public ServiceNpcConfig Config { get; set; } = null!;

    // Ephemeral state
    public ulong LastAttackMs { get; set; }
    public ulong LastDamagedMs { get; set; }
    public string TargetIdentityHex { get; set; } = "";
    public float WanderTargetX { get; set; }
    public float WanderTargetZ { get; set; }
    public bool HasWanderTarget { get; set; }

    // World access
    public Func<IEnumerable<Player>> GetPlayers { get; set; } = () => Enumerable.Empty<Player>();

    // Reducer calls
    public Action<ulong, float, float, float, float> MoveNpc { get; set; } = (_, _, _, _, _) => { };
    public Action<ulong, string, ulong, string> SetState { get; set; } = (_, _, _, _) => { };
    public Action<ulong, ulong, string, int, string> DealDamage { get; set; } = (_, _, _, _, _) => { };
    public Action<ulong, string, int, string> DealDamageToPlayer { get; set; } = (_, _, _, _) => { };

    public float DistanceTo(float x, float z)
    {
        float dx = PosX - x;
        float dz = PosZ - z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    public float DistanceToSpawn() => DistanceTo(SpawnPosX, SpawnPosZ);
}
