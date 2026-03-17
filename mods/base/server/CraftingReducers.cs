using System;
using SpacetimeDB;

namespace SandboxRPG.Server;

public static partial class Module
{
    // =========================================================================
    // CRAFTING REDUCERS
    // =========================================================================

    [Reducer]
    public static void CraftItem(ReducerContext ctx, ulong recipeId)
    {
        var recipe = ctx.Db.CraftingRecipe.Id.Find(recipeId);
        if (recipe is null) throw new Exception("Recipe not found.");

        var r = recipe.Value;
        var identity = ctx.Sender;
        var ingredients = ParseIngredients(r.Ingredients);

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
        {
            uint remaining = needed;
            foreach (var inv in ctx.Db.InventoryItem.Iter())
            {
                if (remaining == 0) break;
                if (inv.OwnerId != identity || inv.ItemType != itemType) continue;

                if (inv.Quantity <= remaining)
                {
                    remaining -= inv.Quantity;
                    ctx.Db.InventoryItem.Delete(inv);
                }
                else
                {
                    var updated = inv;
                    updated.Quantity -= remaining;
                    ctx.Db.InventoryItem.Id.Update(updated);
                    remaining = 0;
                }
            }
        }

        // Award crafted item — stack with existing slot if possible
        bool stacked = false;
        foreach (var invItem in ctx.Db.InventoryItem.Iter())
        {
            if (invItem.OwnerId == identity && invItem.ItemType == r.ResultItemType)
            {
                var updated = invItem;
                updated.Quantity += r.ResultQuantity;
                ctx.Db.InventoryItem.Id.Update(updated);
                stacked = true;
                break;
            }
        }
        if (!stacked)
        {
            ctx.Db.InventoryItem.Insert(new InventoryItem
            {
                OwnerId  = identity,
                ItemType = r.ResultItemType,
                Quantity = r.ResultQuantity,
                Slot     = FindOpenHotbarSlot(ctx, identity),
            });
        }

        Log.Info($"Player crafted {r.ResultQuantity}x {r.ResultItemType}");
    }
}
