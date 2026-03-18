// service/NpcContext.cs
// NOTE: requires generated bindings — Player type comes from SpacetimeDB.Types
// TODO: uncomment when bindings are generated:
// using SpacetimeDB.Types;

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
    // TODO: replace IPlayerData with Player (SpacetimeDB.Types) when bindings are generated
    public Func<IEnumerable<IPlayerData>> GetPlayers { get; set; } = () => Enumerable.Empty<IPlayerData>();

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

// TODO: remove this placeholder interface when bindings are generated.
// This is a stand-in for the generated SpacetimeDB Player type.
public interface IPlayerData
{
    bool IsOnline { get; }
    float PosX { get; }
    float PosZ { get; }
    string IdentityHex { get; }
}
