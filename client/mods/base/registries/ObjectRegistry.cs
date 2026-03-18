// client/mods/base/registries/ObjectRegistry.cs
using System.Collections.Generic;
using Godot;

namespace SandboxRPG;

public static class ObjectRegistry
{
    private static readonly Dictionary<string, ObjectDef> _defs = new();

    public static void Register(string type, ObjectDef def)
    {
        if (string.IsNullOrEmpty(type)) { GD.PrintErr("[ObjectRegistry] Skipping registration: empty key"); return; }
        if (def is null) { GD.PrintErr($"[ObjectRegistry] Skipping registration: null def for '{type}'"); return; }
        if (_defs.ContainsKey(type)) GD.Print($"[ObjectRegistry] Overwriting existing entry: {type}");
        _defs[type] = def;
    }
    public static ObjectDef? Get(string type) => _defs.TryGetValue(type, out var d) ? d : null;

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
            var def = ResourceLoader.Load<ObjectDef>(folderPath.TrimEnd('/') + "/" + file);
            if (def is not null) Register(key, def);
        }
        dir.ListDirEnd();
    }
}
