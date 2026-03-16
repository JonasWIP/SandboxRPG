// mods/hello-world/server/HelloWorldMod.cs
using SpacetimeDB;
using System;
using SandboxRPG.Server.Mods;

namespace SandboxRPG.Server;

// Placing the registration inside partial class Module guarantees the static field
// initializer fires before the Init reducer, which is when ModLoader.RunAll is called.
public static partial class Module
{
    private static readonly HelloWorldModImpl _helloWorldMod = new();

    private sealed class HelloWorldModImpl : IMod
    {
        public HelloWorldModImpl() => ModLoader.Register(this);

        public string Name    => "hello-world";
        public string Version => "1.0.0";
        public string[] Dependencies => Array.Empty<string>();

        public void Seed(ReducerContext ctx)
        {
            ctx.Db.CraftingRecipe.Insert(new CraftingRecipe
            {
                ResultItemType   = "hello_item",
                ResultQuantity   = 1,
                Ingredients      = "wood:1",
                CraftTimeSeconds = 1f,
            });
            Log.Info("[HelloWorldMod] Seeded.");
        }
    }
}
