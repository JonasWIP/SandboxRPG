#if MOD_CASINO
using SpacetimeDB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SandboxRPG.Server;

public static partial class Module
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ulong NowMicros(ReducerContext ctx)
        => (ulong)((DateTimeOffset)ctx.Timestamp).ToUnixTimeMilliseconds() * 1000;

    private static bool IsDefaultIdentity(Identity id)
        => id == default;

    // ── Slot Machine ──────────────────────────────────────────────────────────

    [Reducer]
    public static void SpinSlot(ReducerContext ctx, ulong machineId, ulong betAmount)
    {
        var sessionNullable = ctx.Db.SlotSession.MachineId.Find(machineId)
            ?? throw new Exception("Slot machine not found");
        var session = sessionNullable;

        // Piggybacked stale session cleanup
        if (!IsDefaultIdentity(session.PlayerId) &&
            session.ExpiresAt > 0 && NowMicros(ctx) > session.ExpiresAt)
        {
            ctx.Db.SlotSession.Delete(session);
            ctx.Db.SlotSession.Insert(new SlotSession { MachineId = machineId, IsIdle = true });
            session = ctx.Db.SlotSession.MachineId.Find(machineId)!.Value;
        }

        if (!session.IsIdle && !IsDefaultIdentity(session.PlayerId) &&
            session.PlayerId != ctx.Sender)
            throw new Exception("Machine is occupied");

        if (betAmount == 0) throw new Exception("Bet must be > 0");
        DebitCoins(ctx, ctx.Sender, betAmount, $"slot_bet:{machineId}");

        // Roll reels
        var symbols = new[] { "Ch", "Le", "Or", "St", "Ge", "Be" };
        var rng = new System.Random();
        string r1 = symbols[rng.Next(symbols.Length)];
        string r2 = symbols[rng.Next(symbols.Length)];
        string r3 = symbols[rng.Next(symbols.Length)];
        string reels = $"{r1}|{r2}|{r3}";

        // Payout
        ulong multiplier = 0;
        if (r1 == r2 && r2 == r3)
        {
            multiplier = r1 switch { "Ge" => 100, "St" => 20, "Ch" => 10, _ => 5 };
        }
        else if (r1 == r2 || r2 == r3 || r1 == r3) multiplier = 2;

        ulong winAmount = betAmount * multiplier;
        if (winAmount > 0)
            CreditCoins(ctx, ctx.Sender, winAmount, $"slot_win:{machineId}");

        ctx.Db.SlotSession.Delete(session);
        ctx.Db.SlotSession.Insert(new SlotSession
        {
            MachineId = machineId,
            PlayerId = ctx.Sender,
            Reels = reels,
            IsIdle = false,
            Bet = betAmount,
            WinAmount = winAmount,
            ExpiresAt = NowMicros(ctx) + 30_000_000 // 30s in micros
        });
    }

    [Reducer]
    public static void ReleaseSlot(ReducerContext ctx, ulong machineId)
    {
        var session = ctx.Db.SlotSession.MachineId.Find(machineId)
            ?? throw new Exception("Slot machine not found");
        if (session.PlayerId != ctx.Sender)
            throw new Exception("Not your session");
        ctx.Db.SlotSession.Delete(session);
        ctx.Db.SlotSession.Insert(new SlotSession { MachineId = machineId, IsIdle = true });
    }
}
#endif
