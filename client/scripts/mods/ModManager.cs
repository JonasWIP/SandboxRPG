// client/scripts/mods/ModManager.cs
using Godot;
using System.Collections.Generic;

namespace SandboxRPG;

/// <summary>
/// Autoload singleton. Client mods call ModManager.Register(this) from their _Ready.
/// WorldManager._Ready() calls InitializeAll(this) once the game scene is fully loaded.
/// Autoloads execute _Ready before scene nodes, so all mods are registered before
/// WorldManager.InitializeAll is called.
/// </summary>
public partial class ModManager : Node
{
    public static ModManager Instance { get; private set; } = null!;
    private static readonly List<IClientMod> _pending = new();

    public static void Register(IClientMod mod) => _pending.Add(mod);

    public override void _Ready() => Instance = this;

    private bool _initialized;

    public void InitializeAll(Node sceneRoot)
    {
        if (_initialized) return;
        _initialized = true;

        foreach (var mod in _pending)
        {
            GD.Print($"[ModManager] Initializing: {mod.ModName}");
            mod.Initialize(sceneRoot);
        }
    }
}
