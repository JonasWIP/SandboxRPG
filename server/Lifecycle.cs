// server/Lifecycle.cs
using SpacetimeDB;
using SandboxRPG.Server.Mods;

namespace SandboxRPG.Server;

public static partial class Module
{
    [Reducer(ReducerKind.Init)]
    public static void Init(ReducerContext ctx)
    {
        Log.Info("SandboxRPG server module initialized!");
        // Force static field initializers to run — WASM doesn't guarantee
        // they execute before Init. Touching each mod field triggers the
        // static constructor which calls ModLoader.Register().
        _ = _baseMod;
        _ = _helloWorldMod;
        _ = _interactablesMod;
        _ = _currencyMod;
        ModLoader.RunAll(ctx);
    }

    [Reducer(ReducerKind.ClientConnected)]
    public static void ClientConnected(ReducerContext ctx) =>
        ModLoader.ForwardClientConnected(ctx, ctx.Sender);

    [Reducer(ReducerKind.ClientDisconnected)]
    public static void ClientDisconnected(ReducerContext ctx) =>
        ModLoader.ForwardClientDisconnected(ctx, ctx.Sender);
}
