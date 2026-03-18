// server/GameConstants.cs
namespace SandboxRPG.Server;

/// <summary>Shared constants for magic numbers used across reducers.</summary>
public static class GameConstants
{
    // ---- Inventory Slots ----
    public const int BagSlot = -1;
    public const int HotbarSize = 8;

    // ---- Furnace Slots ----
    public const int FurnaceInputSlot = 0;
    public const int FurnaceOutputSlot = 1;

    // ---- Distances (squared, for comparison without sqrt) ----
    public const float PickupRangeSq = 5f * 5f;         // 25
    public const float MeleeAttackRangeSq = 3f * 3f;    // 9
    public const float TradeRangeSq = 10f * 10f;        // 100
    public const float MaxMovePerTickSq = 20f * 20f;    // 400

    // ---- Combat ----
    public const int BaseMeleeDamage = 10;
    public const int SwordDamage = 25;
    public const float SellPriceMultiplier = 0.5f;
    public const int MinSellPrice = 1;

    // ---- Misc ----
    public const int MaxChatLength = 256;
    public const int MaxNameLength = 32;
    public const int MaxSignTextLength = 200;
    public const ulong DamageEventTtlMs = 30_000;
}
