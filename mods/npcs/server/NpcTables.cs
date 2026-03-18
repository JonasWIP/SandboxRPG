// mods/npcs/server/NpcTables.cs
using SpacetimeDB;

namespace SandboxRPG.Server;

public static partial class Module
{
    [Table(Name = "service_identity", Public = true)]
    public partial struct ServiceIdentity
    {
        [AutoInc][PrimaryKey]
        public ulong Id;
        public Identity ServiceId;
    }

    [Table(Name = "npc_config", Public = true)]
    public partial struct NpcConfig
    {
        [AutoInc][PrimaryKey]
        public ulong Id;
        public string NpcType;
        public int MaxHealth;
        public int AttackDamage;
        public float AttackRange;
        public ulong AttackCooldownMs;
        public bool IsAttackable;
        public bool IsTrader;
        public bool HasDialogue;
    }

    [Table(Name = "npc", Public = true)]
    public partial struct Npc
    {
        [AutoInc][PrimaryKey]
        public ulong Id;
        public string NpcType;
        public float PosX;
        public float PosY;
        public float PosZ;
        public float RotY;
        public int Health;
        public int MaxHealth;
        public string CurrentState;
        public ulong TargetEntityId;
        public string TargetEntityType;
        public float SpawnPosX;
        public float SpawnPosY;
        public float SpawnPosZ;
        public bool IsAlive;
        public ulong LastUpdateMs;
    }

    [Table(Name = "npc_loot_table", Public = true)]
    public partial struct NpcLootTable
    {
        [AutoInc][PrimaryKey]
        public ulong Id;
        public string NpcType;
        public string ItemType;
        public int Quantity;
        public float DropChance;
    }

    [Table(Name = "npc_spawn_rule", Public = true)]
    public partial struct NpcSpawnRule
    {
        [AutoInc][PrimaryKey]
        public ulong Id;
        public string NpcType;
        public float ZoneX;
        public float ZoneZ;
        public float ZoneRadius;
        public int MaxCount;
        public float RespawnTimeSec;
    }

    [Table(Name = "npc_trade_offer", Public = true)]
    public partial struct NpcTradeOffer
    {
        [AutoInc][PrimaryKey]
        public ulong Id;
        public string NpcType;
        public string ItemType;
        public int Price;
        public string Currency;
    }

    [Table(Name = "damage_event", Public = true)]
    public partial struct DamageEvent
    {
        [AutoInc][PrimaryKey]
        public ulong Id;
        public ulong SourceId;
        public string SourceType;
        public ulong TargetId;
        public string TargetType;
        public int Amount;
        public string DamageType;
        public ulong Timestamp;
    }
}
