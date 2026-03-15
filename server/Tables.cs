using SpacetimeDB;

namespace SandboxRPG.Server;

public static partial class Module
{
    // =========================================================================
    // TABLES
    // All tables are public so clients can subscribe and receive updates.
    // =========================================================================

    /// <summary>Player data — one row per connected identity, persisted across sessions.</summary>
    [Table(Name = "player", Public = true)]
    public partial struct Player
    {
        [PrimaryKey]
        public Identity Identity;
        public string Name;
        public float PosX;
        public float PosY;
        public float PosZ;
        public float RotY; // Y-axis rotation in radians
        public float Health;
        public float MaxHealth;
        public float Stamina;
        public float MaxStamina;
        public bool IsOnline;
        public string ColorHex; // e.g. "#3CB4E5"
    }

    /// <summary>Inventory items belonging to players. Slot -1 = bag, 0–8 = hotbar.</summary>
    [Table(Name = "inventory_item", Public = true)]
    public partial struct InventoryItem
    {
        [AutoInc][PrimaryKey]
        public ulong Id;
        public Identity OwnerId;
        public string ItemType;
        public uint Quantity;
        public int Slot;
    }

    /// <summary>Items lying in the world (resource nodes, player drops).</summary>
    [Table(Name = "world_item", Public = true)]
    public partial struct WorldItem
    {
        [AutoInc][PrimaryKey]
        public ulong Id;
        public string ItemType;
        public uint Quantity;
        public float PosX;
        public float PosY;
        public float PosZ;
    }

    /// <summary>Structures placed by players (walls, floors, furniture).</summary>
    [Table(Name = "placed_structure", Public = true)]
    public partial struct PlacedStructure
    {
        [AutoInc][PrimaryKey]
        public ulong Id;
        public Identity OwnerId;
        public string StructureType;
        public float PosX;
        public float PosY;
        public float PosZ;
        public float RotY;
        public float Health;
        public float MaxHealth;
    }

    /// <summary>Server-authoritative crafting recipes. Clients read these to render the UI.</summary>
    [Table(Name = "crafting_recipe", Public = true)]
    public partial struct CraftingRecipe
    {
        [AutoInc][PrimaryKey]
        public ulong Id;
        public string ResultItemType;
        public uint ResultQuantity;
        /// <summary>Ingredients as "item:qty,item:qty" — parsed by ParseIngredients().</summary>
        public string Ingredients;
        public float CraftTimeSeconds;
    }

    /// <summary>Chat message log. Appended on SendChat, never deleted.</summary>
    [Table(Name = "chat_message", Public = true)]
    public partial struct ChatMessage
    {
        [AutoInc][PrimaryKey]
        public ulong Id;
        public Identity SenderId;
        public string SenderName;
        public string Text;
        public ulong Timestamp; // microseconds since epoch
    }

    /// <summary>Harvestable and decorative world objects (trees, rocks, bushes).
    /// Seeded on Init, deleted when health reaches 0.</summary>
    [Table(Name = "world_object", Public = true)]
    public partial struct WorldObject
    {
        [AutoInc][PrimaryKey]
        public ulong Id;
        public string ObjectType;   // "tree_pine", "rock_large", "rock_small", "tree_dead", "bush"
        public float PosX;
        public float PosY;
        public float PosZ;
        public float RotY;          // Y-axis rotation in radians
        public uint Health;
        public uint MaxHealth;
    }

    /// <summary>Singleton terrain configuration. Always has exactly one row (Id = 0).
    /// Clients subscribe and regenerate terrain mesh + collision whenever this changes.</summary>
    [Table(Name = "terrain_config", Public = true)]
    public partial struct TerrainConfig
    {
        [PrimaryKey]
        public uint Id;            // always 0
        public uint Seed;
        public float WorldSize;    // units (side length of the square world)
        public float NoiseScale;   // spatial frequency of hills
        public float NoiseAmplitude; // max height added by noise
    }
}
