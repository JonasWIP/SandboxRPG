using Godot;
using System.Collections.Generic;
using System.Linq;
using SpacetimeDB.Types;

namespace SandboxRPG;

/// <summary>
/// Singleton autoload. Reads ModConfig table on connect, builds dependency graph,
/// and provides enable/disable state to mod UI scripts.
/// </summary>
public partial class ModManager : Node
{
    public static ModManager Instance { get; private set; } = null!;

    // Registered mods in topological dependency order
    private readonly List<string> _enabledMods = new();

    public override void _Ready()
    {
        Instance = this;
        GameManager.Instance.SubscriptionApplied += OnSubscriptionApplied;
    }

    private void OnSubscriptionApplied()
    {
        _enabledMods.Clear();
        var rows = GameManager.Instance.Conn?.Db.ModConfig.Iter().ToList() ?? new();
        var enabled = rows.Where(r => r.Enabled).ToDictionary(r => r.ModId);
        var sorted = TopologicalSort(enabled);
        _enabledMods.AddRange(sorted);
        GD.Print($"[ModManager] Active mods: {string.Join(", ", _enabledMods)}");
    }

    public bool IsEnabled(string modId) => _enabledMods.Contains(modId);

    private static List<string> TopologicalSort(Dictionary<string, ModConfig> mods)
    {
        var result = new List<string>();
        var visited = new HashSet<string>();

        void Visit(string id)
        {
            if (visited.Contains(id) || !mods.ContainsKey(id)) return;
            visited.Add(id);
            var deps = mods[id].Dependencies
                .Split(',', System.StringSplitOptions.RemoveEmptyEntries);
            foreach (var dep in deps) Visit(dep);
            result.Add(id);
        }

        foreach (var id in mods.Keys) Visit(id);
        return result;
    }
}
