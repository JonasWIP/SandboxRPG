// client/mods/interactables/interaction/InteractableStructure.cs
using Godot;
using SpacetimeDB;
using SpacetimeDB.Types;

namespace SandboxRPG;

/// <summary>
/// StaticBody3D that implements IInteractable.
/// Used by StructureSpawner instead of a plain StaticBody3D for interactable
/// structure types (chest, furnace, crafting_table, sign).
/// </summary>
public partial class InteractableStructure : StaticBody3D, IInteractable
{
    /// <summary>The PlacedStructure primary key.</summary>
    public ulong StructureId { get; set; }

    /// <summary>The structure type string (e.g. "chest", "furnace").</summary>
    public string StructureType { get; set; } = "";

    /// <summary>The Identity of the player who placed this structure.</summary>
    public Identity OwnerId { get; set; }

    // =========================================================================
    // IInteractable
    // =========================================================================

    public string HintText => StructureType switch
    {
        "chest"          => "Open Chest [E]",
        "furnace"        => "Open Furnace [E]",
        "crafting_table" => "Use Crafting Table [E]",
        "sign"           => "Read Sign [E]",
        _                => $"Interact [E]",
    };

    public bool CanInteract(Player? player)
    {
        if (player == null) return false;

        // Public structures (or structures the player owns) are always accessible.
        var ac = GameManager.Instance.GetAccessControl(StructureId, "placed_structure");
        if (ac == null) return true;          // no access control record → public
        if (ac.IsPublic) return true;         // explicitly public
        if (ac.OwnerId == player.Identity) return true; // owner always has access
        return false;
    }

    public void Interact(Player? player)
    {
        var isOwner = player != null && player.Identity == OwnerId;

        BasePanel panel = StructureType switch
        {
            "chest"          => new ContainerPanel(StructureId, "placed_structure", 16, "Chest"),
            "furnace"        => new FurnacePanel(StructureId),
            "crafting_table" => new CraftingTablePanel(),
            "sign"           => new SignPanel(StructureId, isOwner),
            _                => new ContainerPanel(StructureId, "placed_structure", 16),
        };

        UIManager.Instance.Push(panel);
    }
}
