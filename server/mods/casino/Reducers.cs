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

    // ── Blackjack helpers ─────────────────────────────────────────────────────

    private static int CardValue(string card)
    {
        string rank = card[..^1]; // strip suit
        return rank switch { "A" => 11, "J" or "Q" or "K" => 10, _ => int.Parse(rank) };
    }

    private static int HandValue(string hand)
    {
        if (string.IsNullOrEmpty(hand)) return 0;
        var cards = hand.Split(',');
        int total = 0, aces = 0;
        foreach (var c in cards)
        {
            int v = CardValue(c);
            if (v == 11) aces++;
            total += v;
        }
        while (total > 21 && aces > 0) { total -= 10; aces--; }
        return total;
    }

    private static (string card, string remainingDeck) DrawCard(string deck)
    {
        var cards = deck.Split(',');
        return (cards[0], string.Join(",", cards[1..]));
    }

    private static void CheckAllSeatsResolved(ReducerContext ctx, ulong machineId, uint roundId)
    {
        var seats = ctx.Db.BlackjackSeat.Iter()
            .Where(s => s.MachineId == machineId && s.RoundId == roundId).ToList();
        bool allDone = seats.All(s => s.State is 2 or 3 or 4); // Standing/Bust/Done
        if (!allDone) return;

        RunDealerTurn(ctx, machineId, roundId);
    }

    private static void RunDealerTurn(ReducerContext ctx, ulong machineId, uint roundId)
    {
        var gameFound = ctx.Db.BlackjackGame.MachineId.Find(machineId);
        if (gameFound is null) throw new Exception("Game not found");
        var g = gameFound.Value;
        g.State = 2; // DealerTurn
        string deck = g.Deck;

        while (HandValue(g.DealerHand) < 17)
        {
            var (card, remaining) = DrawCard(deck);
            g.DealerHand = string.IsNullOrEmpty(g.DealerHand) ? card : g.DealerHand + "," + card;
            deck = remaining;
        }
        g.Deck = deck;
        g.DealerHandHidden = g.DealerHand; // reveal

        int dealerTotal = HandValue(g.DealerHand);
        var seats = ctx.Db.BlackjackSeat.Iter()
            .Where(s => s.MachineId == machineId && s.RoundId == roundId).ToList();

        foreach (var seat in seats)
        {
            int playerTotal = HandValue(seat.Hand);
            bool playerBust = playerTotal > 21;
            bool dealerBust = dealerTotal > 21;
            ulong payout = 0;

            if (!playerBust)
            {
                if (dealerBust || playerTotal > dealerTotal) payout = seat.Bet * 2; // Win
                else if (playerTotal == dealerTotal) payout = seat.Bet;             // Push
            }
            if (payout > 0)
                CreditCoins(ctx, seat.PlayerId, payout, $"blackjack_win:{machineId}");
        }

        g.State = 3; // Payout
        ctx.Db.BlackjackGame.Delete(gameFound.Value);
        ctx.Db.BlackjackGame.Insert(g);
    }

    // ── Blackjack reducers ────────────────────────────────────────────────────

    [Reducer]
    public static void JoinBlackjack(ReducerContext ctx, ulong machineId, byte seatIndex)
    {
        if (seatIndex > 3) throw new Exception("Seat index must be 0–3");

        var gameFound = ctx.Db.BlackjackGame.MachineId.Find(machineId);
        if (gameFound is null) throw new Exception("Blackjack table not found");
        var game = gameFound.Value;

        // Reset from Payout state
        if (game.State == 3)
        {
            foreach (var old in ctx.Db.BlackjackSeat.Iter()
                .Where(s => s.MachineId == machineId && s.RoundId == game.RoundId).ToList())
                ctx.Db.BlackjackSeat.Delete(old);
            var reset = game;
            reset.State = 0;
            ctx.Db.BlackjackGame.Delete(game);
            ctx.Db.BlackjackGame.Insert(reset);
            var reloaded = ctx.Db.BlackjackGame.MachineId.Find(machineId);
            if (reloaded is null) throw new Exception("Table reload failed");
            game = reloaded.Value;
        }

        if (game.State != 0) throw new Exception("Round already in progress");

        bool taken = ctx.Db.BlackjackSeat.Iter().Any(s =>
            s.MachineId == machineId && s.SeatIndex == seatIndex && s.RoundId == game.RoundId);
        if (taken) throw new Exception("Seat already taken");

        bool alreadySitting = ctx.Db.BlackjackSeat.Iter().Any(s =>
            s.MachineId == machineId && s.PlayerId == ctx.Sender && s.RoundId == game.RoundId);
        if (alreadySitting) throw new Exception("Already seated");

        ctx.Db.BlackjackSeat.Insert(new BlackjackSeat
        {
            MachineId = machineId, SeatIndex = seatIndex,
            PlayerId = ctx.Sender, Hand = "", Bet = 0, State = 0,
            RoundId = game.RoundId
        });
    }

    [Reducer]
    public static void PlaceBet(ReducerContext ctx, ulong machineId, ulong amount)
    {
        if (amount == 0) throw new Exception("Bet must be > 0");
        var gameFound = ctx.Db.BlackjackGame.MachineId.Find(machineId);
        if (gameFound is null) throw new Exception("Table not found");
        var game = gameFound.Value;
        if (game.State != 0) throw new Exception("Betting closed");

        var seat = ctx.Db.BlackjackSeat.Iter()
            .FirstOrDefault(s => s.MachineId == machineId && s.PlayerId == ctx.Sender
                              && s.RoundId == game.RoundId);
        if (seat.PlayerId != ctx.Sender) throw new Exception("Not seated");
        if (seat.Bet > 0) throw new Exception("Bet already placed");

        DebitCoins(ctx, ctx.Sender, amount, $"blackjack_bet:{machineId}");
        ctx.Db.BlackjackSeat.Delete(seat);
        ctx.Db.BlackjackSeat.Insert(new BlackjackSeat
        {
            Id = seat.Id, MachineId = seat.MachineId, SeatIndex = seat.SeatIndex,
            PlayerId = seat.PlayerId, Hand = seat.Hand, Bet = amount,
            State = seat.State, RoundId = seat.RoundId
        });
    }

    [Reducer]
    public static void StartBlackjackRound(ReducerContext ctx, ulong machineId)
    {
        var gameFound = ctx.Db.BlackjackGame.MachineId.Find(machineId);
        if (gameFound is null) throw new Exception("Table not found");
        var game = gameFound.Value;
        if (game.State != 0 && game.State != 3)
            throw new Exception("Round already started");

        if (game.State == 3)
        {
            foreach (var old in ctx.Db.BlackjackSeat.Iter()
                .Where(s => s.MachineId == machineId).ToList())
                ctx.Db.BlackjackSeat.Delete(old);
        }

        var seatsWithBets = ctx.Db.BlackjackSeat.Iter()
            .Where(s => s.MachineId == machineId && s.Bet > 0).ToList();
        if (seatsWithBets.Count == 0) throw new Exception("No players with bets");

        uint newRoundId = game.RoundId + 1;
        string deck = BuildDeck();

        // Deal 2 cards to each player
        foreach (var seat in seatsWithBets)
        {
            string hand = "";
            for (int i = 0; i < 2; i++)
            {
                var (card, remaining) = DrawCard(deck);
                deck = remaining;
                hand = string.IsNullOrEmpty(hand) ? card : hand + "," + card;
            }
            ctx.Db.BlackjackSeat.Delete(seat);
            ctx.Db.BlackjackSeat.Insert(new BlackjackSeat
            {
                Id = seat.Id, MachineId = seat.MachineId, SeatIndex = seat.SeatIndex,
                PlayerId = seat.PlayerId, Hand = hand, Bet = seat.Bet,
                State = 0, // will be set below
                RoundId = newRoundId
            });
        }

        // Deal 2 to dealer
        string dealerHand = "";
        for (int i = 0; i < 2; i++)
        {
            var (card, remaining) = DrawCard(deck);
            deck = remaining;
            dealerHand = string.IsNullOrEmpty(dealerHand) ? card : dealerHand + "," + card;
        }

        // Set first seat to Acting, rest to Waiting
        bool firstSet = false;
        var allSeats = ctx.Db.BlackjackSeat.Iter()
            .Where(s => s.MachineId == machineId && s.RoundId == newRoundId)
            .OrderBy(s => s.SeatIndex).ToList();
        foreach (var s in allSeats)
        {
            byte state = !firstSet ? (byte)1 : (byte)0; // 1=Acting, 0=Waiting
            if (!firstSet) firstSet = true;
            ctx.Db.BlackjackSeat.Delete(s);
            ctx.Db.BlackjackSeat.Insert(new BlackjackSeat
            {
                Id = s.Id, MachineId = s.MachineId, SeatIndex = s.SeatIndex,
                PlayerId = s.PlayerId, Hand = s.Hand, Bet = s.Bet,
                State = state, RoundId = newRoundId
            });
        }

        string hiddenDealer = dealerHand.Split(',')[0] + ",??";
        ctx.Db.BlackjackGame.Delete(game);
        ctx.Db.BlackjackGame.Insert(new BlackjackGame
        {
            MachineId = machineId, State = 1,
            DealerHand = dealerHand, DealerHandHidden = hiddenDealer,
            Deck = deck, RoundId = newRoundId
        });
    }

    [Reducer]
    public static void HitBlackjack(ReducerContext ctx, ulong machineId)
    {
        var gameFound = ctx.Db.BlackjackGame.MachineId.Find(machineId);
        if (gameFound is null) throw new Exception("Table not found");
        var game = gameFound.Value;
        if (game.State != 1) throw new Exception("Not in player turns");

        var seat = ctx.Db.BlackjackSeat.Iter()
            .FirstOrDefault(s => s.MachineId == machineId && s.PlayerId == ctx.Sender
                              && s.RoundId == game.RoundId && s.State == 1);
        if (seat.PlayerId != ctx.Sender) throw new Exception("Not your turn");

        var (card, deck) = DrawCard(game.Deck);
        string newHand = seat.Hand + "," + card;
        byte newState = HandValue(newHand) > 21 ? (byte)3 : (byte)1; // Bust or still Acting

        ctx.Db.BlackjackSeat.Delete(seat);
        ctx.Db.BlackjackSeat.Insert(new BlackjackSeat
        {
            Id = seat.Id, MachineId = seat.MachineId, SeatIndex = seat.SeatIndex,
            PlayerId = seat.PlayerId, Hand = newHand, Bet = seat.Bet,
            State = newState, RoundId = seat.RoundId
        });
        var g = game; g.Deck = deck;
        ctx.Db.BlackjackGame.Delete(game);
        ctx.Db.BlackjackGame.Insert(g);

        if (newState == 3) AdvanceToNextSeat(ctx, machineId, game.RoundId);
    }

    [Reducer]
    public static void StandBlackjack(ReducerContext ctx, ulong machineId)
    {
        var gameFound = ctx.Db.BlackjackGame.MachineId.Find(machineId);
        if (gameFound is null) throw new Exception("Table not found");
        var game = gameFound.Value;
        if (game.State != 1) throw new Exception("Not in player turns");

        var seat = ctx.Db.BlackjackSeat.Iter()
            .FirstOrDefault(s => s.MachineId == machineId && s.PlayerId == ctx.Sender
                              && s.RoundId == game.RoundId && s.State == 1);
        if (seat.PlayerId != ctx.Sender) throw new Exception("Not your turn");

        ctx.Db.BlackjackSeat.Delete(seat);
        ctx.Db.BlackjackSeat.Insert(new BlackjackSeat
        {
            Id = seat.Id, MachineId = seat.MachineId, SeatIndex = seat.SeatIndex,
            PlayerId = seat.PlayerId, Hand = seat.Hand, Bet = seat.Bet,
            State = 2, RoundId = seat.RoundId // Standing
        });
        AdvanceToNextSeat(ctx, machineId, game.RoundId);
    }

    private static void AdvanceToNextSeat(ReducerContext ctx, ulong machineId, uint roundId)
    {
        var next = ctx.Db.BlackjackSeat.Iter()
            .Where(s => s.MachineId == machineId && s.RoundId == roundId && s.State == 0)
            .OrderBy(s => s.SeatIndex).FirstOrDefault();

        if (next.RoundId == roundId) // found a waiting seat
        {
            ctx.Db.BlackjackSeat.Delete(next);
            ctx.Db.BlackjackSeat.Insert(new BlackjackSeat
            {
                Id = next.Id, MachineId = next.MachineId, SeatIndex = next.SeatIndex,
                PlayerId = next.PlayerId, Hand = next.Hand, Bet = next.Bet,
                State = 1, RoundId = next.RoundId // Acting
            });
        }
        else
        {
            CheckAllSeatsResolved(ctx, machineId, roundId);
        }
    }

    [Reducer]
    public static void LeaveBlackjack(ReducerContext ctx, ulong machineId)
    {
        var gameFound = ctx.Db.BlackjackGame.MachineId.Find(machineId);
        var seat = ctx.Db.BlackjackSeat.Iter()
            .FirstOrDefault(s => s.MachineId == machineId && s.PlayerId == ctx.Sender);
        if (seat.PlayerId != ctx.Sender) return;

        if (gameFound is not null && gameFound.Value.State == 0 && seat.Bet > 0)
            CreditCoins(ctx, ctx.Sender, seat.Bet, "blackjack_leave_refund");

        ctx.Db.BlackjackSeat.Delete(seat);
    }

    [Reducer]
    public static void SkipSeat(ReducerContext ctx, ulong machineId, byte seatIndex)
    {
        var gameFound = ctx.Db.BlackjackGame.MachineId.Find(machineId);
        if (gameFound is null) throw new Exception("Table not found");
        var game = gameFound.Value;
        if (game.State != 1) throw new Exception("Not in player turns");

        var seat = ctx.Db.BlackjackSeat.Iter()
            .FirstOrDefault(s => s.MachineId == machineId && s.SeatIndex == seatIndex
                              && s.RoundId == game.RoundId && s.State == 1);
        if (seat.RoundId != game.RoundId) throw new Exception("Seat not found or not active");

        var target = ctx.Db.Player.Identity.Find(seat.PlayerId);
        if (target != null && target.Value.IsOnline)
            throw new Exception("Player is still connected");

        ctx.Db.BlackjackSeat.Delete(seat);
        ctx.Db.BlackjackSeat.Insert(new BlackjackSeat
        {
            Id = seat.Id, MachineId = seat.MachineId, SeatIndex = seat.SeatIndex,
            PlayerId = seat.PlayerId, Hand = seat.Hand, Bet = seat.Bet,
            State = 2, RoundId = seat.RoundId
        });
        AdvanceToNextSeat(ctx, machineId, game.RoundId);
    }

    // ── Arcade machines ───────────────────────────────────────────────────────

    [Reducer]
    public static void StartArcade(ReducerContext ctx, ulong machineId, ulong bet)
    {
        if (bet == 0) throw new Exception("Bet must be > 0");
        var sessionNullable = ctx.Db.ArcadeSession.MachineId.Find(machineId);
        if (sessionNullable is null) throw new Exception("Arcade machine not found");
        var session = sessionNullable.Value;

        // Piggybacked stale session cleanup
        if (!IsDefaultIdentity(session.PlayerId) &&
            session.ExpiresAt > 0 && NowMicros(ctx) > session.ExpiresAt)
        {
            var cleared = session;
            cleared.PlayerId = default;
            cleared.State = 0;
            ctx.Db.ArcadeSession.Delete(session);
            ctx.Db.ArcadeSession.Insert(cleared);
            sessionNullable = ctx.Db.ArcadeSession.MachineId.Find(machineId);
            session = sessionNullable!.Value;
        }

        if (!IsDefaultIdentity(session.PlayerId) && session.PlayerId != ctx.Sender)
            throw new Exception("Machine occupied");

        DebitCoins(ctx, ctx.Sender, bet, $"arcade_bet:{machineId}");

        string challenge;
        ulong expiry;
        if (session.GameType == 0) // Reaction
        {
            var rng = new System.Random();
            int targetMs = rng.Next(1000, 3000);
            int windowMs = 150;
            challenge = $"{targetMs}:{windowMs}";
            expiry = NowMicros(ctx) + 10_000_000; // 10s
        }
        else // Pattern
        {
            var chars = new[] { 'R', 'G', 'B', 'Y' };
            var rng = new System.Random();
            challenge = new string(System.Linq.Enumerable.Range(0, 5)
                .Select(_ => chars[rng.Next(chars.Length)]).ToArray());
            expiry = NowMicros(ctx) + 15_000_000; // 15s
        }

        ctx.Db.ArcadeSession.Delete(session);
        ctx.Db.ArcadeSession.Insert(new ArcadeSession
        {
            MachineId = machineId, PlayerId = ctx.Sender,
            GameType = session.GameType, State = 1, // Active
            Bet = bet, ChallengeData = challenge,
            StartTime = NowMicros(ctx), ExpiresAt = expiry
        });
    }

    [Reducer]
    public static void ArcadeInputReaction(ReducerContext ctx, ulong machineId)
    {
        var sessionNullable = ctx.Db.ArcadeSession.MachineId.Find(machineId);
        if (sessionNullable is null) throw new Exception("Session not found");
        var session = sessionNullable.Value;
        if (session.State != 1 || session.PlayerId != ctx.Sender)
            throw new Exception("Not your active session");

        var parts = session.ChallengeData.Split(':');
        int targetMs = int.Parse(parts[0]);
        int windowMs = int.Parse(parts[1]);

        // Use server timestamp exclusively — no client-supplied timing
        long elapsedMs = (long)((NowMicros(ctx) - session.StartTime) / 1000);
        bool hit = Math.Abs(elapsedMs - targetMs) <= (windowMs + 200);

        if (hit)
            CreditCoins(ctx, ctx.Sender, session.Bet * 3, $"arcade_reaction_win:{machineId}");

        ctx.Db.ArcadeSession.Delete(session);
        ctx.Db.ArcadeSession.Insert(new ArcadeSession
        {
            MachineId = machineId, PlayerId = default, GameType = session.GameType,
            State = 0, Bet = 0, ChallengeData = hit ? "HIT" : "MISS", ExpiresAt = 0
        });
    }

    [Reducer]
    public static void ArcadeInputPattern(ReducerContext ctx, ulong machineId, string playerSequence)
    {
        var sessionNullable = ctx.Db.ArcadeSession.MachineId.Find(machineId);
        if (sessionNullable is null) throw new Exception("Session not found");
        var session = sessionNullable.Value;
        if (session.State != 1 || session.PlayerId != ctx.Sender)
            throw new Exception("Not your active session");

        bool correct = playerSequence == session.ChallengeData;
        bool expired = NowMicros(ctx) > session.ExpiresAt;

        if (correct && !expired)
            CreditCoins(ctx, ctx.Sender, session.Bet * 2, $"arcade_pattern_win:{machineId}");

        ctx.Db.ArcadeSession.Delete(session);
        ctx.Db.ArcadeSession.Insert(new ArcadeSession
        {
            MachineId = machineId, PlayerId = default, GameType = session.GameType,
            State = 0, Bet = 0,
            ChallengeData = correct && !expired ? "CORRECT" : "WRONG",
            ExpiresAt = 0
        });
    }

    // ── Coin Pusher ───────────────────────────────────────────────────────────

    [Reducer]
    public static void PushCoin(ReducerContext ctx, ulong machineId, ulong copperAmount)
    {
        if (copperAmount == 0) throw new Exception("Amount must be > 0");
        var stateNullable = ctx.Db.CoinPusherState.MachineId.Find(machineId);
        if (stateNullable is null) throw new Exception("Coin pusher not found");
        var state = stateNullable.Value;

        DebitCoins(ctx, ctx.Sender, copperAmount, $"coin_push:{machineId}");

        uint newCount = state.CoinCount + 1;
        ulong newPool = state.CopperPool + copperAmount;
        ulong now = NowMicros(ctx);

        if (newCount >= state.JackpotThreshold)
        {
            // Jackpot — pay out pool to this player, reset
            CreditCoins(ctx, ctx.Sender, newPool, $"coin_pusher_jackpot:{machineId}");
            ctx.Db.CoinPusherState.Delete(state);
            ctx.Db.CoinPusherState.Insert(new CoinPusherState
            {
                MachineId = machineId, CoinCount = 0, CopperPool = 0,
                LastPusherId = ctx.Sender, LastPushTime = now,
                JackpotThreshold = state.JackpotThreshold
            });
        }
        else
        {
            ctx.Db.CoinPusherState.Delete(state);
            ctx.Db.CoinPusherState.Insert(new CoinPusherState
            {
                MachineId = machineId, CoinCount = newCount, CopperPool = newPool,
                LastPusherId = ctx.Sender, LastPushTime = now,
                JackpotThreshold = state.JackpotThreshold
            });
        }
    }
}
#endif
