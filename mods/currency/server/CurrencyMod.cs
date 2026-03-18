using SpacetimeDB;
using System;
using SandboxRPG.Server.Mods;

namespace SandboxRPG.Server;

public static partial class Module
{
    private static readonly CurrencyModImpl _currencyMod = new();

    private sealed class CurrencyModImpl : IMod
    {
        public CurrencyModImpl() => ModLoader.Register(this);

        public string   Name         => "currency";
        public string   Version      => "1.0.0";
        public string[] Dependencies => new[] { "base" };

        public void Seed(ReducerContext ctx)
        {
            SeedCurrencyRecipes(ctx);
            Log.Info("[CurrencyMod] Seeded.");
        }
    }

    private static void SeedCurrencyRecipes(ReducerContext ctx)
    {
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe
        {
            ResultItemType = "silver_coin", ResultQuantity = 1,
            Ingredients = "copper_coin:100", CraftTimeSeconds = 0, Station = "",
        });
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe
        {
            ResultItemType = "copper_coin", ResultQuantity = 100,
            Ingredients = "silver_coin:1", CraftTimeSeconds = 0, Station = "",
        });
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe
        {
            ResultItemType = "gold_coin", ResultQuantity = 1,
            Ingredients = "silver_coin:100", CraftTimeSeconds = 0, Station = "",
        });
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe
        {
            ResultItemType = "silver_coin", ResultQuantity = 100,
            Ingredients = "gold_coin:1", CraftTimeSeconds = 0, Station = "",
        });
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe
        {
            ResultItemType = "platinum_coin", ResultQuantity = 1,
            Ingredients = "gold_coin:100", CraftTimeSeconds = 0, Station = "",
        });
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe
        {
            ResultItemType = "gold_coin", ResultQuantity = 100,
            Ingredients = "platinum_coin:1", CraftTimeSeconds = 0, Station = "",
        });
    }
}
