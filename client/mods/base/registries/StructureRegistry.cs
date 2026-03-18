// client/mods/base/registries/StructureRegistry.cs
using System.Collections.Generic;
using Godot;

namespace SandboxRPG;

public static class StructureRegistry
{
    private static readonly Dictionary<string, StructureDef> _defs = new();

    public static void Register(string type, StructureDef def)
    {
        if (string.IsNullOrEmpty(type)) { GD.PrintErr("[StructureRegistry] Skipping registration: empty key"); return; }
        if (def is null) { GD.PrintErr($"[StructureRegistry] Skipping registration: null def for '{type}'"); return; }
        if (_defs.ContainsKey(type)) GD.Print($"[StructureRegistry] Overwriting existing entry: {type}");
        _defs[type] = def;
    }
    public static StructureDef? Get(string type) => _defs.TryGetValue(type, out var d) ? d : null;

    /// <summary>Returns all registered structure types where IsPlaceable = true.</summary>
    public static IEnumerable<(string Type, StructureDef Def)> AllPlaceable()
    {
        foreach (var kvp in _defs)
            if (kvp.Value.IsPlaceable)
                yield return (kvp.Key, kvp.Value);
    }

    public static void LoadFolder(string folderPath)
    {
        var dir = DirAccess.Open(folderPath);
        if (dir is null) return;
        dir.ListDirBegin();
        string file;
        while ((file = dir.GetNext()) != "")
        {
            if (!file.EndsWith(".tres")) continue;
            var key = System.IO.Path.GetFileNameWithoutExtension(file);
            var def = ResourceLoader.Load<StructureDef>(folderPath.TrimEnd('/') + "/" + file);
            if (def is not null) Register(key, def);
        }
        dir.ListDirEnd();
    }
}
