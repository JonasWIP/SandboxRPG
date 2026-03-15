using SpacetimeDB;
using System;

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
        SeedTerrainConfig(ctx);
        SeedRecipes(ctx);
        SeedWorldItems(ctx);
        SeedWorldObjects(ctx);
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
                PosY = 0.3f,
                PosZ = 1f,
                RotY = (float)Math.PI,   // face inland (+Z direction)
                Health = 100f,
                MaxHealth = 100f,
                Stamina = 100f,
                MaxStamina = 100f,
                IsOnline = true,
                ColorHex = "#3CB4E5",
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
        ctx.Db.WorldItem.Insert(new WorldItem { ItemType = "wood",  Quantity = 5, PosX =  3f, PosY = TerrainHeightAt( 3f,  3f) + 0.2f, PosZ =  3f });
        ctx.Db.WorldItem.Insert(new WorldItem { ItemType = "stone", Quantity = 3, PosX = -4f, PosY = TerrainHeightAt(-4f,  2f) + 0.2f, PosZ =  2f });
        ctx.Db.WorldItem.Insert(new WorldItem { ItemType = "wood",  Quantity = 8, PosX =  7f, PosY = TerrainHeightAt( 7f,  6f) + 0.2f, PosZ =  6f });
        ctx.Db.WorldItem.Insert(new WorldItem { ItemType = "iron",  Quantity = 2, PosX = -8f, PosY = TerrainHeightAt(-8f,  4f) + 0.2f, PosZ =  4f });
        ctx.Db.WorldItem.Insert(new WorldItem { ItemType = "stone", Quantity = 5, PosX = 10f, PosY = TerrainHeightAt(10f,  8f) + 0.2f, PosZ =  8f });

        Log.Info("Seeded starter world items.");
    }

    private static void SeedTerrainConfig(ReducerContext ctx)
    {
        ctx.Db.TerrainConfig.Insert(new TerrainConfig
        {
            Id             = 0,
            Seed           = 42,
            WorldSize      = 500f,
            NoiseScale     = 0.04f,
            NoiseAmplitude = 1.5f,
        });
        Log.Info("Seeded terrain config.");
    }

    /// <summary>Mirrors client Terrain.HeightAt. Seed/noise constants must match TerrainConfig defaults.</summary>
    private static float TerrainHeightAt(float x, float z)
    {
        const uint  Seed  = 42;
        const float NScl  = 0.04f;
        const float NAmp  = 1.5f;

        if (z < 0f) return (float)Math.Max(z * 0.3, -3.0);
        double t     = Math.Clamp((z - 2.0) / 15.0, 0.0, 1.0);
        double baseH = t * t * (3.0 - 2.0 * t) * 2.0;
        double nr    = Math.Clamp((z - 5.0) / 15.0, 0.0, 1.0);
        double s     = Seed * 0.001;
        double noise = Math.Sin(x * NScl + s) * Math.Cos(z * NScl * 1.7 + s * 1.3) * NAmp
                     + Math.Sin((x + z) * NScl * 2.9 + s * 0.7) * NAmp * 0.3;
        return (float)(baseH + noise * nr);
    }

    private static void SeedWorldObjects(ReducerContext ctx)
    {
        var rng = new Random(42);

        WorldObject MakeObject(string type, float xMin, float xMax, float zMin, float zMax, uint hp)
        {
            float x = (float)(rng.NextDouble() * (xMax - xMin) + xMin);
            float z = (float)(rng.NextDouble() * (zMax - zMin) + zMin);
            return new WorldObject
            {
                ObjectType = type,
                PosX = x, PosY = TerrainHeightAt(x, z), PosZ = z,
                RotY = (float)(rng.NextDouble() * Math.PI * 2),
                Health = hp, MaxHealth = hp,
            };
        }

        for (int i = 0; i < 250; i++)  ctx.Db.WorldObject.Insert(MakeObject("tree_pine",  -200f, 200f,  30f, 230f, 100));
        for (int i = 0; i < 60;  i++)  ctx.Db.WorldObject.Insert(MakeObject("tree_dead",  -150f, 150f,  20f,  60f, 60));
        for (int i = 0; i < 90;  i++)  ctx.Db.WorldObject.Insert(MakeObject("rock_large", -200f, 200f,   0f, 150f, 150));
        for (int i = 0; i < 75;  i++)  ctx.Db.WorldObject.Insert(MakeObject("rock_small", -220f, 220f, -20f, 170f, 80));
        for (int i = 0; i < 50;  i++)  ctx.Db.WorldObject.Insert(MakeObject("bush",       -100f, 100f,   5f,  50f, 30));

        Log.Info("Seeded world objects.");
    }
}
