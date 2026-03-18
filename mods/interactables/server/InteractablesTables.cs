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
}
