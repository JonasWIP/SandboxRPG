using System;
using SpacetimeDB;

namespace SandboxRPG.Server;

public static partial class Module
{
    // =========================================================================
    // CRAFTING REDUCERS
    // =========================================================================

    [Reducer]
    public static void CraftItem(ReducerContext ctx, ulong recipeId, string station)
    {
        var recipe = ctx.Db.CraftingRecipe.Id.Find(recipeId);
        if (recipe is null) throw new Exception("Recipe not found.");

        var r = recipe.Value;
        var identity = ctx.Sender;
        var ingredients = ParseIngredients(r.Ingredients);

        // Station check
        if (!string.IsNullOrEmpty(r.Station) && r.Station != station)
            throw new Exception($"This recipe requires a {r.Station}.");

        // Verify player has all required ingredients
        foreach (var (itemType, needed) in ingredients)
        {
            uint have = 0;
            foreach (var inv in ctx.Db.InventoryItem.Iter())
            {
                if (inv.OwnerId == identity && inv.ItemType == itemType)
                    have += inv.Quantity;
            }
            if (have < needed)
                throw new Exception($"Not enough {itemType}. Need {needed}, have {have}.");
        }

        // Consume ingredients
        foreach (var (itemType, needed) in ingredients)
            ConsumeFromInventory(ctx, identity, itemType, needed);

        // Award crafted item
        AddOrStackInventoryItem(ctx, identity, r.ResultItemType, r.ResultQuantity,
            FindOpenHotbarSlot(ctx, identity));

        Log.Info($"Player crafted {r.ResultQuantity}x {r.ResultItemType}");
    }
}
