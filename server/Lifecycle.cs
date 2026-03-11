using SpacetimeDB;

namespace SandboxRPG.Server;

public static partial class Module
{
    // =========================================================================
    // LIFECYCLE REDUCERS
    // Called automatically by SpacetimeDB — not invokable by clients.
    // =========================================================================

    [Reducer(ReducerKind.Init)]
    public static void Init(ReducerContext ctx)
    {
        Log.Info("SandboxRPG server module initialized!");
        SeedRecipes(ctx);
        SeedWorldItems(ctx);
    }

    [Reducer(ReducerKind.ClientConnected)]
    public static void ClientConnected(ReducerContext ctx)
    {
        var identity = ctx.Sender;
        var existing = ctx.Db.Player.Identity.Find(identity);

        if (existing is not null)
        {
            var player = existing.Value;
            player.IsOnline = true;
            ctx.Db.Player.Identity.Update(player);
            Log.Info($"Player '{player.Name}' reconnected.");
        }
        else
        {
            ctx.Db.Player.Insert(new Player
            {
                Identity = identity,
                Name = $"Player_{identity.ToString()[..8]}",
                PosX = 0f,
                PosY = 1f,
                PosZ = 0f,
                RotY = 0f,
                Health = 100f,
                MaxHealth = 100f,
                Stamina = 100f,
                MaxStamina = 100f,
                IsOnline = true,
            });

            // Give starter tools
            ctx.Db.InventoryItem.Insert(new InventoryItem { OwnerId = identity, ItemType = "wood_pickaxe", Quantity = 1, Slot = 0 });
            ctx.Db.InventoryItem.Insert(new InventoryItem { OwnerId = identity, ItemType = "wood_axe",     Quantity = 1, Slot = 1 });

            Log.Info($"New player created: {identity}");
        }
    }

    [Reducer(ReducerKind.ClientDisconnected)]
    public static void ClientDisconnected(ReducerContext ctx)
    {
        var existing = ctx.Db.Player.Identity.Find(ctx.Sender);
        if (existing is not null)
        {
            var player = existing.Value;
            player.IsOnline = false;
            ctx.Db.Player.Identity.Update(player);
            Log.Info($"Player '{player.Name}' disconnected.");
        }
    }

    // =========================================================================
    // SEED DATA (called once on Init)
    // =========================================================================

    private static void SeedRecipes(ReducerContext ctx)
    {
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe { ResultItemType = "wood_wall",      ResultQuantity = 1, Ingredients = "wood:4",        CraftTimeSeconds = 2f });
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe { ResultItemType = "stone_wall",     ResultQuantity = 1, Ingredients = "stone:6",       CraftTimeSeconds = 3f });
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe { ResultItemType = "wood_floor",     ResultQuantity = 1, Ingredients = "wood:3",        CraftTimeSeconds = 1.5f });
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe { ResultItemType = "wood_door",      ResultQuantity = 1, Ingredients = "wood:3,iron:1", CraftTimeSeconds = 2f });
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe { ResultItemType = "campfire",       ResultQuantity = 1, Ingredients = "wood:5,stone:3", CraftTimeSeconds = 3f });
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe { ResultItemType = "workbench",      ResultQuantity = 1, Ingredients = "wood:8,stone:4", CraftTimeSeconds = 5f });
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe { ResultItemType = "chest",          ResultQuantity = 1, Ingredients = "wood:6,iron:2",  CraftTimeSeconds = 4f });
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe { ResultItemType = "stone_pickaxe",  ResultQuantity = 1, Ingredients = "wood:2,stone:3", CraftTimeSeconds = 2f });
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe { ResultItemType = "iron_pickaxe",   ResultQuantity = 1, Ingredients = "wood:2,iron:3",  CraftTimeSeconds = 3f });

        Log.Info("Seeded crafting recipes.");
    }

    private static void SeedWorldItems(ReducerContext ctx)
    {
        ctx.Db.WorldItem.Insert(new WorldItem { ItemType = "wood",  Quantity = 5, PosX =  3f, PosY = 0.5f, PosZ =  3f });
        ctx.Db.WorldItem.Insert(new WorldItem { ItemType = "stone", Quantity = 3, PosX = -4f, PosY = 0.5f, PosZ =  2f });
        ctx.Db.WorldItem.Insert(new WorldItem { ItemType = "wood",  Quantity = 8, PosX =  7f, PosY = 0.5f, PosZ = -5f });
        ctx.Db.WorldItem.Insert(new WorldItem { ItemType = "iron",  Quantity = 2, PosX = -8f, PosY = 0.5f, PosZ = -6f });
        ctx.Db.WorldItem.Insert(new WorldItem { ItemType = "stone", Quantity = 5, PosX = 10f, PosY = 0.5f, PosZ =  8f });

        Log.Info("Seeded starter world items.");
    }
}
