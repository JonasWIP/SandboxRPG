#if MOD_CASINO
using Godot;
using SandboxRPG;

/// <summary>
/// Registers all casino structure interaction handlers.
/// Call CasinoUI.Register() from ModManager when casino mod is enabled.
/// </summary>
public static class CasinoUI
{
    public static void Register()
    {
        InteractionSystem.RegisterStructureHandler("casino_slot_machine",    id => SlotMachineUI.Open(id));
        InteractionSystem.RegisterStructureHandler("casino_blackjack_table", id => BlackjackUI.Open(id));
        InteractionSystem.RegisterStructureHandler("casino_coin_pusher",     id => CoinPusherUI.Open(id));
        InteractionSystem.RegisterStructureHandler("casino_arcade_reaction", id => ArcadeUI.TryTriggerReaction(id));
        InteractionSystem.RegisterStructureHandler("casino_arcade_pattern",  id => ArcadeUI.Open(id, isPattern: true));
#if MOD_CURRENCY
        InteractionSystem.RegisterStructureHandler("casino_exchange",        id => ExchangeUI.Open(id));
#endif
    }

    /// <summary>
    /// Finds a "Screen" MeshInstance3D child of <paramref name="machineNode"/> and applies
    /// <paramref name="viewport"/>'s texture to it. Shared by SlotMachineUI and ArcadeUI.
    /// </summary>
    public static void ApplyScreenTexture(Node3D machineNode, SubViewport viewport)
    {
        var screen = machineNode.FindChild("Screen") as MeshInstance3D;
        if (screen == null) return;
        screen.MaterialOverride = new StandardMaterial3D { AlbedoTexture = viewport.GetTexture() };
    }
}
#endif
