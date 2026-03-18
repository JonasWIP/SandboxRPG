// service/ServiceModLoader.cs
namespace SandboxRPG.Service;

public static class ServiceModLoader
{
    private static readonly List<IServiceMod> _mods = new();
    private static List<IServiceMod>? _sorted;

    public static void Register(IServiceMod mod) => _mods.Add(mod);

    public static void InitializeAll(ServiceContext ctx)
    {
        _sorted = TopoSort(_mods);
        foreach (var mod in _sorted)
        {
            Console.WriteLine($"[ServiceModLoader] Initializing: {mod.Name} v{mod.Version}");
            mod.Initialize(ctx);
        }
    }

    private static List<IServiceMod> TopoSort(List<IServiceMod> mods)
    {
        var byName = mods.ToDictionary(m => m.Name);
        var inDegree = mods.ToDictionary(m => m.Name, _ => 0);

        foreach (var mod in mods)
            foreach (var dep in mod.Dependencies)
                if (byName.ContainsKey(dep))
                    inDegree[mod.Name]++;

        var queue = new Queue<IServiceMod>(mods.Where(m => inDegree[m.Name] == 0));
        var result = new List<IServiceMod>();

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
            throw new InvalidOperationException("Circular dependency in service mods.");

        return result;
    }
}
