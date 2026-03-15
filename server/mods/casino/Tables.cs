#if MOD_CASINO
using SpacetimeDB;

namespace SandboxRPG.Server;

public static partial class Module
{

// ── Slot Machine ──────────────────────────────────────────────────────────────
[Table(Name = "slot_session", Public = true)]
public partial struct SlotSession
{
    [PrimaryKey]
    public ulong MachineId;    // = PlacedStructure.Id
    public Identity PlayerId;  // zero-value = unoccupied
    public string Reels;       // "🍒|🍋|⭐" — result after spin
    public bool IsIdle;        // true = available; false = showing result
    public ulong Bet;
    public ulong WinAmount;
    public ulong ExpiresAt;    // microseconds; 0 = no expiry
}

// ── Blackjack ─────────────────────────────────────────────────────────────────
[Table(Name = "blackjack_game", Public = true)]
public partial struct BlackjackGame
{
    [PrimaryKey]
    public ulong MachineId;
    public byte State;     // 0=WaitingForPlayers 1=PlayerTurns 2=DealerTurn 3=Payout
    public string DealerHand;       // "AS,10H" — full hand (shown after DealerTurn)
    public string DealerHandHidden; // "??,10H" — shown during PlayerTurns
    public string Deck;             // comma-separated remaining draw pile
    public uint RoundId;
}

[Table(Name = "blackjack_seat", Public = true)]
public partial struct BlackjackSeat
{
    [PrimaryKey, AutoInc]
    public ulong Id;
    public ulong MachineId;
    public byte SeatIndex;   // 0–3
    public Identity PlayerId;
    public string Hand;      // "7D,KS"
    public ulong Bet;
    public byte State;       // 0=Waiting 1=Acting 2=Standing 3=Bust 4=Done
    public uint RoundId;     // matches BlackjackGame.RoundId
}

// ── Coin Pusher ───────────────────────────────────────────────────────────────
[Table(Name = "coin_pusher_state", Public = true)]
public partial struct CoinPusherState
{
    [PrimaryKey]
    public ulong MachineId;
    public uint CoinCount;
    public ulong CopperPool;       // total Copper bet since last jackpot reset
    public Identity LastPusherId;  // zero-value = none
    public ulong LastPushTime;
    public uint JackpotThreshold;  // default 200
}

// ── Arcade ────────────────────────────────────────────────────────────────────
[Table(Name = "arcade_session", Public = true)]
public partial struct ArcadeSession
{
    [PrimaryKey]
    public ulong MachineId;
    public Identity PlayerId;      // zero-value = unoccupied
    public byte GameType;          // 0=Reaction 1=Pattern
    public byte State;             // 0=Idle 1=Active 2=Judging
    public ulong Bet;
    public string ChallengeData;   // Reaction: "targetMs:windowMs"; Pattern: "RRBLG"
    public ulong StartTime;        // server microsecond timestamp
    public ulong ExpiresAt;        // microseconds; piggybacked cleanup
}

} // end partial class Module
#endif
