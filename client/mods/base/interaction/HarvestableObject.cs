using Godot;
using SpacetimeDB.Types;

namespace SandboxRPG;

public partial class HarvestableObject : StaticBody3D, IInteractable
{
    public ulong WorldObjectId { get; set; }
    public string ObjectType { get; set; } = "";

    public string HintText => $"[LMB] Harvest {ObjectType}";
    public string InteractAction => "primary_attack";

    public bool CanInteract(Player? player)
    {
        return !BuildSystem.IsBuildable(Hotbar.Instance?.ActiveItemType);
    }

    public void Interact(Player? player)
    {
        var toolType = Hotbar.Instance?.ActiveItemType ?? string.Empty;
        GameManager.Instance.HarvestWorldObject(WorldObjectId, toolType);
    }
}
