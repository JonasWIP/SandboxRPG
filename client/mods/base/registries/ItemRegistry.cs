// client/mods/base/registries/ItemRegistry.cs
using System.Collections.Generic;
using Godot;

namespace SandboxRPG;

public static class ItemRegistry
{
    private static readonly Dictionary<string, ItemDef> _defs = new();

    public static void     Register(string itemType, ItemDef def) => _defs[itemType] = def;
    public static ItemDef? Get(string itemType) => _defs.TryGetValue(itemType, out var d) ? d : null;

    /// <summary>
    /// Scans folderPath for *.tres files and registers each one keyed by filename
    /// (without extension). Silently skips files that fail to load or are wrong type.
    /// </summary>
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
            var def = ResourceLoader.Load<ItemDef>(folderPath.TrimEnd('/') + "/" + file);
            if (def is not null) Register(key, def);
        }
        dir.ListDirEnd();
    }
}
