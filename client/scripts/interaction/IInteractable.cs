// client/scripts/interaction/IInteractable.cs
using SpacetimeDB.Types;

namespace SandboxRPG;

public interface IInteractable
{
    string HintText { get; }
    string InteractAction => "interact";
    bool CanInteract(Player? player);
    void Interact(Player? player);
}
