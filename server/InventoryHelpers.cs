// server/InventoryHelpers.cs
using System.Collections.Generic;

namespace SandboxRPG.Server;

/// <summary>Pure C# inventory helpers — no SpacetimeDB dependency. Testable in isolation.</summary>
public static class InventoryHelpers
{
    /// <summary>Parses "wood:4,stone:2" ingredient strings into typed tuples.</summary>
    public static List<(string itemType, uint quantity)> ParseIngredients(string? ingredients)
    {
        var result = new List<(string, uint)>();
        if (string.IsNullOrEmpty(ingredients)) return result;

        foreach (var part in ingredients.Split(','))
        {
            var kv = part.Trim().Split(':');
            if (kv.Length == 2 && uint.TryParse(kv[1], out uint qty))
                result.Add((kv[0].Trim(), qty));
        }
        return result;
    }
}
