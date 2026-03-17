// client/scripts/mods/ModManager.cs
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SandboxRPG;

/// <summary>
/// Autoload singleton. Client mods call ModManager.Register(this) from their _Ready.
/// WorldManager._Ready() calls InitializeAll(this) once the game scene is ready.
/// Mods are initialised in dependency order (topological sort on Dependencies).
/// </summary>
public partial class ModManager : Node
{
    public static ModManager Instance { get; private set; } = null!;
    private static readonly List<IClientMod> _pending = new();

    public static void Register(IClientMod mod) => _pending.Add(mod);

    public override void _Ready() => Instance = this;

    private bool _initialized;

    public void InitializeAll(Node sceneRoot)
    {
        if (_initialized) return;
        _initialized = true;

        var sorted = TopoSort(_pending);
        foreach (var mod in sorted)
        {
            GD.Print($"[ModManager] Initializing: {mod.ModName}");
            mod.Initialize(sceneRoot);
        }
    }

    private static List<IClientMod> TopoSort(List<IClientMod> mods)
    {
        var byName   = mods.ToDictionary(m => m.ModName);
        var inDegree = mods.ToDictionary(m => m.ModName, _ => 0);

        foreach (var mod in mods)
            foreach (var dep in mod.Dependencies)
                if (byName.ContainsKey(dep))
                    inDegree[mod.ModName]++;

        var queue  = new Queue<IClientMod>(mods.Where(m => inDegree[m.ModName] == 0));
        var result = new List<IClientMod>();

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            result.Add(current);
            foreach (var dependent in mods.Where(m => m.Dependencies.Contains(current.ModName)))
            {
                inDegree[dependent.ModName]--;
                if (inDegree[dependent.ModName] == 0)
                    queue.Enqueue(dependent);
            }
        }

        if (result.Count != mods.Count)
            throw new InvalidOperationException("[ModManager] Circular dependency detected in client mods.");

        return result;
    }
}
