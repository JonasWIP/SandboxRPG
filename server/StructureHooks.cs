// server/StructureHooks.cs
using System;
using System.Collections.Generic;
using SpacetimeDB;

namespace SandboxRPG.Server;

public static partial class Module
{
    public static class StructureHooks
    {
        private static readonly Dictionary<string, List<Action<ReducerContext, PlacedStructure>>> _onPlace = new();
        private static readonly Dictionary<string, List<Action<ReducerContext, PlacedStructure>>> _onRemove = new();

        public static void RegisterOnPlace(string structureType, Action<ReducerContext, PlacedStructure> hook)
        {
            if (!_onPlace.ContainsKey(structureType))
                _onPlace[structureType] = new();
            _onPlace[structureType].Add(hook);
        }

        public static void RegisterOnRemove(string structureType, Action<ReducerContext, PlacedStructure> hook)
        {
            if (!_onRemove.ContainsKey(structureType))
                _onRemove[structureType] = new();
            _onRemove[structureType].Add(hook);
        }

        public static void RunOnPlace(ReducerContext ctx, PlacedStructure structure)
        {
            if (_onPlace.TryGetValue(structure.StructureType, out var hooks))
                foreach (var hook in hooks) hook(ctx, structure);
        }

        public static void RunOnRemove(ReducerContext ctx, PlacedStructure structure)
        {
            if (_onRemove.TryGetValue(structure.StructureType, out var hooks))
                foreach (var hook in hooks) hook(ctx, structure);
        }

        public static void Clear()
        {
            _onPlace.Clear();
            _onRemove.Clear();
        }
    }
}
