// client/mods/npcs/registries/TradeRegistry.cs
using System.Collections.Generic;

namespace SandboxRPG;

public class TradeDisplayInfo
{
    public string DisplayName { get; set; } = "";
}

public static class TradeRegistry
{
    private static readonly Dictionary<string, TradeDisplayInfo> _info = new();

    public static void Register(string itemType, TradeDisplayInfo info) => _info[itemType] = info;
    public static TradeDisplayInfo? Get(string itemType) => _info.TryGetValue(itemType, out var d) ? d : null;
}
