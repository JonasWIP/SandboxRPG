// server/mods/ModLoader.cs
using SpacetimeDB;

namespace SandboxRPG.Server.Mods;

public static class ModLoader
{
    private static readonly List<IMod> _mods = new();

    public static void Register(IMod mod) => _mods.Add(mod);

    public static void RunAll(ReducerContext ctx)
    {
        foreach (var mod in ModLoaderHelpers.TopoSort(_mods, m => m.Name, m => m.Dependencies))
        {
            Log.Info($"[ModLoader] Seeding mod: {mod.Name} v{mod.Version}");
            mod.Seed(ctx);
        }
    }
}
