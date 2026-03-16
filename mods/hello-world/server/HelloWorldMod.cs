// mods/hello-world/server/HelloWorldMod.cs
using SpacetimeDB;
using System;
using SandboxRPG.Mods.HelloWorld;
using SandboxRPG.Server.Mods;

namespace SandboxRPG.Server;

public static partial class Module
{
    private static readonly HelloWorldModImpl _helloWorldMod = new();

    private sealed class HelloWorldModImpl : IMod
    {
        public HelloWorldModImpl() => ModLoader.Register(this);

        public string   Name         => "hello-world";
        public string   Version      => "1.0.0";
        public string[] Dependencies => Array.Empty<string>();

        public void Seed(ReducerContext ctx)
        {
            ctx.Db.CraftingRecipe.Insert(new CraftingRecipe
            {
                ResultItemType   = HelloWorldConstants.ItemType,
                ResultQuantity   = HelloWorldConstants.Quantity,
                Ingredients      = HelloWorldConstants.Ingredients,
                CraftTimeSeconds = HelloWorldConstants.CraftTimeSeconds,
            });
            Log.Info("[HelloWorldMod] Seeded.");
        }
    }
}
