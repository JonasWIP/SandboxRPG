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
        InteractionSystem.RegisterStructureHandler("casino_exchange",        id => ExchangeUI.Open(id));
    }
}
#endif
