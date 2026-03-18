// mods/interactables/server/InteractablesTables.cs
using SpacetimeDB;

namespace SandboxRPG.Server;

public static partial class Module
{
    [Table(Name = "furnace_state", Public = true)]
    public partial struct FurnaceState
    {
        [PrimaryKey] public ulong StructureId;
        public string RecipeType;
        public ulong StartTimeMs;
        public ulong DurationMs;
        public bool Complete;
    }

    [Table(Name = "sign_text", Public = true)]
    public partial struct SignText
    {
        [PrimaryKey] public ulong StructureId;
        public string Text;
    }

    /// <summary>Access control for interactable structures — who can use a structure.</summary>
    [Table(Name = "access_control", Public = true)]
    public partial struct AccessControl
    {
        [PrimaryKey] public ulong StructureId;
        public Identity OwnerId;
        public bool IsPublic; // if true, anyone can interact
    }

    /// <summary>Container slots — items stored inside a chest or furnace.</summary>
    [Table(Name = "container_slot", Public = true)]
    public partial struct ContainerSlot
    {
        [AutoInc][PrimaryKey] public ulong Id;
        public ulong ContainerId;   // structureId of the owning structure
        public string ContainerType; // "chest", "furnace"
        public string ItemType;
        public uint Quantity;
        public int Slot;
    }
}
