#if MOD_CASINO
using SpacetimeDB;
using System;
using System.Linq;

namespace SandboxRPG.Server;

public static partial class Module
{
    internal static void SeedCasino(ReducerContext ctx)
    {
        // Only seed once — check if casino_building already exists
        if (ctx.Db.PlacedStructure.Iter().Any(s => s.StructureType == "casino_building"))
            return;

        // Casino building POI at (50, 0, 50)
        var building = ctx.Db.PlacedStructure.Insert(new PlacedStructure
        {
            OwnerId = ctx.Sender,
            StructureType = "casino_building",
            PosX = 50f, PosY = 0f, PosZ = 50f,
            RotY = 0f, Health = 1000, MaxHealth = 1000
        });

        // Seed one of each machine inside the building (relative positions)
        var machineTypes = new[]
        {
            ("casino_slot_machine",    52f, 0f, 50f),
            ("casino_blackjack_table", 55f, 0f, 50f),
            ("casino_coin_pusher",     58f, 0f, 50f),
            ("casino_arcade_reaction", 52f, 0f, 53f),
            ("casino_arcade_pattern",  55f, 0f, 53f),
            ("casino_exchange",        58f, 0f, 53f),
        };

        foreach (var (type, x, y, z) in machineTypes)
        {
            var placed = ctx.Db.PlacedStructure.Insert(new PlacedStructure
            {
                OwnerId = ctx.Sender, StructureType = type,
                PosX = x, PosY = y, PosZ = z,
                RotY = 0f, Health = 500, MaxHealth = 500
            });

            // Initialize session rows for each machine
            InitMachineSession(ctx, placed.Id, type);
        }

        SeedCasinoRecipes(ctx);
    }

    private static void InitMachineSession(ReducerContext ctx, ulong machineId, string type)
    {
        switch (type)
        {
            case "casino_slot_machine":
                ctx.Db.SlotSession.Insert(new SlotSession
                    { MachineId = machineId, IsIdle = true });
                break;
            case "casino_blackjack_table":
                ctx.Db.BlackjackGame.Insert(new BlackjackGame
                    { MachineId = machineId, State = 0, RoundId = 1,
                      Deck = BuildDeck(), DealerHand = "", DealerHandHidden = "" });
                break;
            case "casino_coin_pusher":
                ctx.Db.CoinPusherState.Insert(new CoinPusherState
                    { MachineId = machineId, JackpotThreshold = 200 });
                break;
            case "casino_arcade_reaction":
                ctx.Db.ArcadeSession.Insert(new ArcadeSession
                    { MachineId = machineId, GameType = 0, State = 0 });
                break;
            case "casino_arcade_pattern":
                ctx.Db.ArcadeSession.Insert(new ArcadeSession
                    { MachineId = machineId, GameType = 1, State = 0 });
                break;
            case "casino_exchange":
                // Exchange machine uses currency mod reducers directly — no session table needed
                break;
        }
    }

    internal static string BuildDeck()
    {
        var ranks = new[] { "A","2","3","4","5","6","7","8","9","10","J","Q","K" };
        var suits = new[] { "S","H","D","C" };
        var cards = new System.Collections.Generic.List<string>();
        foreach (var s in suits) foreach (var r in ranks) cards.Add(r + s);
        // Fisher-Yates shuffle with fresh RNG
        var rng = new System.Random();
        for (int i = cards.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (cards[i], cards[j]) = (cards[j], cards[i]);
        }
        return string.Join(",", cards);
    }

    private static void SeedCasinoRecipes(ReducerContext ctx)
    {
        var recipes = new[]
        {
            ("casino_slot_machine",     1u, "iron:10,stone:5",  5f),
            ("casino_blackjack_table",  1u, "wood:8,iron:4",    5f),
            ("casino_coin_pusher",      1u, "iron:6,stone:10",  5f),
            ("casino_arcade_reaction",  1u, "iron:8,stone:2",   5f),
            ("casino_arcade_pattern",   1u, "iron:8,stone:2",   5f),
            ("casino_exchange",         1u, "iron:4,stone:4",   5f),
        };
        foreach (var (result, qty, ingredients, time) in recipes)
        {
            if (!ctx.Db.CraftingRecipe.Iter().Any(r => r.ResultItemType == result))
                ctx.Db.CraftingRecipe.Insert(new CraftingRecipe
                {
                    ResultItemType = result, ResultQuantity = qty,
                    Ingredients = ingredients, CraftTimeSeconds = time
                });
        }
    }
}
#endif
