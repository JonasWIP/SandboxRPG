// client/mods/npcs/registries/DialogueRegistry.cs
using System.Collections.Generic;

namespace SandboxRPG;

public static class DialogueRegistry
{
    private static readonly Dictionary<string, string[]> _dialogues = new();

    public static void Register(string npcType, string[] lines) => _dialogues[npcType] = lines;
    public static string[]? Get(string npcType) => _dialogues.TryGetValue(npcType, out var d) ? d : null;
}
