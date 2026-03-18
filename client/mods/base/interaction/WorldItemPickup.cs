using Godot;
using SpacetimeDB.Types;

namespace SandboxRPG;

public partial class WorldItemPickup : StaticBody3D, IInteractable
{
    public ulong WorldItemId { get; set; }
    public string ItemType { get; set; } = "";
    public uint Quantity { get; set; } = 1;

    public string HintText => $"[E] Pick up {ItemType} x{Quantity}";
    public string InteractAction => "interact";

    public bool CanInteract(Player? player) => true;

    public void Interact(Player? player)
    {
        GameManager.Instance.PickupWorldItem(WorldItemId);
    }

    public void UpdateFromServer()
    {
        foreach (var wi in GameManager.Instance.GetAllWorldItems())
        {
            if (wi.Id == WorldItemId) { Quantity = wi.Quantity; return; }
        }
    }
}
