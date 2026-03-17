// client/mods/base/BaseClientMod.cs
using Godot;
using System;

namespace SandboxRPG;

/// <summary>
/// Autoload mod — registers the base game content.
/// Depends on nothing; all other mods that use base registries declare Dependencies = ["base"].
/// Initialize() is called by ModManager (in dependency order) from WorldManager._Ready().
/// </summary>
public partial class BaseClientMod : Node, IClientMod
{
    public string   ModName      => "base";
    public string[] Dependencies => Array.Empty<string>();

    public override void _Ready() => ModManager.Register(this);

    public void Initialize(Node sceneRoot)
    {
        BaseContent.RegisterAll();
        GD.Print("[BaseClientMod] Content registered.");
    }
}
