// server/mods/ModLoader.cs
using SpacetimeDB;

namespace SandboxRPG.Server.Mods;

public static class ModLoader
{
    private static readonly List<IMod> _mods   = new();
    private static List<IMod>?         _sorted = null;  // cached after first sort

    public static void Register(IMod mod) => _mods.Add(mod);

    private static List<IMod> Sorted() =>
        _sorted ??= ModLoaderHelpers.TopoSort(_mods, m => m.Name, m => m.Dependencies);

    public static void RunAll(ReducerContext ctx)
    {
        foreach (var mod in Sorted())
        {
            Log.Info($"[ModLoader] Seeding mod: {mod.Name} v{mod.Version}");
            mod.Seed(ctx);
        }
    }

    public static void ForwardClientConnected(ReducerContext ctx, Identity identity)
    {
        foreach (var mod in Sorted())
            mod.OnClientConnected(ctx, identity);
    }

    public static void ForwardClientDisconnected(ReducerContext ctx, Identity identity)
    {
        foreach (var mod in Sorted())
            mod.OnClientDisconnected(ctx, identity);
    }
}
