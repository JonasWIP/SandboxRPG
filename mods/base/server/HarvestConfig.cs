// mods/base/server/HarvestConfig.cs
using System.Collections.Generic;

namespace SandboxRPG.Server;

/// <summary>
/// Runtime registry for tool damage and world object drop tables.
/// Replaces the hard-coded switches in WorldReducers.
/// Plain static dictionaries — NOT SpacetimeDB tables.
/// </summary>
public static class HarvestConfig
{
    private record struct DropEntry(string ItemType, uint Quantity);
    private record struct DamageKey(string Tool, string Object);

    private static readonly Dictionary<string, DropEntry> _drops  = new();
    private static readonly Dictionary<DamageKey, uint>   _damage = new();

    public static void RegisterDrop(string objectType, string itemType, uint quantity)
        => _drops[objectType] = new DropEntry(itemType, quantity);

    public static void RegisterToolDamage(string toolType, string objectType, uint damage)
        => _damage[new DamageKey(toolType, objectType)] = damage;

    public static (string ItemType, uint Quantity) GetDrop(string objectType)
        => _drops.TryGetValue(objectType, out var d) ? (d.ItemType, d.Quantity) : ("wood", 1u);

    /// <summary>Returns damage dealt; 5 is the bare-hands/unknown-tool default.</summary>
    public static uint GetToolDamage(string toolType, string objectType)
        => _damage.TryGetValue(new DamageKey(toolType, objectType), out var d) ? d : 5u;
}
