// client/mods/npcs/registries/NpcVisualRegistry.cs
using Godot;
using System.Collections.Generic;

namespace SandboxRPG;

public class NpcVisualDef
{
    public string? ModelPath { get; set; }
    public float Scale { get; set; } = 1.0f;
    public Color TintColor { get; set; } = Colors.White;
    public Color HealthBarColor { get; set; } = Colors.Red;
    public string DisplayName { get; set; } = "NPC";
}

public static class NpcVisualRegistry
{
    private static readonly Dictionary<string, NpcVisualDef> _defs = new();

    public static void Register(string npcType, NpcVisualDef def) => _defs[npcType] = def;
    public static NpcVisualDef? Get(string npcType) => _defs.TryGetValue(npcType, out var d) ? d : null;
}
