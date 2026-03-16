// server/mods/ModLoader.cs
using SpacetimeDB;

namespace SandboxRPG.Server.Mods;

public static class ModLoader
{
    private static readonly List<IMod> _mods = new();

    public static void Register(IMod mod) => _mods.Add(mod);

    public static void RunAll(ReducerContext ctx)
    {
        foreach (var mod in TopoSort(_mods))
        {
            Log.Info($"[ModLoader] Seeding mod: {mod.Name} v{mod.Version}");
            mod.Seed(ctx);
        }
    }

    // Kahn's algorithm. Throws InvalidOperationException on circular dependency.
    // Unknown dependency names (mods not registered) are silently ignored.
    private static IEnumerable<IMod> TopoSort(List<IMod> mods)
    {
        var nameToMod = mods.ToDictionary(m => m.Name);
        var inDegree   = mods.ToDictionary(m => m.Name, _ => 0);

        foreach (var mod in mods)
            foreach (var dep in mod.Dependencies)
                if (!nameToMod.ContainsKey(dep))
                {
                    Log.Warn($"[ModLoader] Mod '{mod.Name}' depends on '{dep}' which is not registered — skipping.");
                    continue;
                }
                else
                    inDegree[mod.Name]++;

        var queue  = new Queue<IMod>(mods.Where(m => inDegree[m.Name] == 0));
        var result = new List<IMod>();

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            result.Add(current);
            foreach (var dependent in mods.Where(m => m.Dependencies.Contains(current.Name)))
            {
                inDegree[dependent.Name]--;
                if (inDegree[dependent.Name] == 0)
                    queue.Enqueue(dependent);
            }
        }

        if (result.Count != mods.Count)
            throw new InvalidOperationException("[ModLoader] Circular dependency detected in mods.");

        return result;
    }
}
