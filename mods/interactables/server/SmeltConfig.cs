// mods/interactables/server/SmeltConfig.cs
using System.Collections.Generic;

namespace SandboxRPG.Server;

public static class SmeltConfig
{
    public record struct SmeltRecipe(string InputItem, string OutputItem, uint OutputQuantity, ulong DurationMs);

    private static readonly Dictionary<string, SmeltRecipe> _recipes = new();

    public static void Register(string inputItem, string outputItem, uint outputQuantity, ulong durationMs)
        => _recipes[inputItem] = new SmeltRecipe(inputItem, outputItem, outputQuantity, durationMs);

    public static SmeltRecipe? Get(string inputItem)
        => _recipes.TryGetValue(inputItem, out var r) ? r : null;

    public static void Clear() => _recipes.Clear();
}
