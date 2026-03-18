// server/StructureHooks.cs
using System;
using System.Collections.Generic;
using SpacetimeDB;

namespace SandboxRPG.Server;

/// <summary>
/// Callback registry for structure placement and removal events.
/// Mods register hooks during Seed(); BuildingReducers fires them.
/// </summary>
public static class StructureHooks
{
    private static readonly Dictionary<string, Action<ReducerContext, Module.PlacedStructure>> _onPlace  = new();
    private static readonly Dictionary<string, Action<ReducerContext, Module.PlacedStructure>> _onRemove = new();

    public static void RegisterOnPlace(string structureType, Action<ReducerContext, Module.PlacedStructure> handler)
        => _onPlace[structureType] = handler;

    public static void RegisterOnRemove(string structureType, Action<ReducerContext, Module.PlacedStructure> handler)
        => _onRemove[structureType] = handler;

    public static void FireOnPlace(ReducerContext ctx, Module.PlacedStructure structure)
    {
        if (_onPlace.TryGetValue(structure.StructureType, out var handler))
            handler(ctx, structure);
    }

    public static void FireOnRemove(ReducerContext ctx, Module.PlacedStructure structure)
    {
        if (_onRemove.TryGetValue(structure.StructureType, out var handler))
            handler(ctx, structure);
    }

    public static void Clear()
    {
        _onPlace.Clear();
        _onRemove.Clear();
    }
}
