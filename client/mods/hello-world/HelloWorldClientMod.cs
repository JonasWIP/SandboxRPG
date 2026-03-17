// client/mods/hello-world/HelloWorldClientMod.cs
using Godot;

namespace SandboxRPG;

/// <summary>
/// Godot autoload for the hello-world client mod.
/// Registers with ModManager in _Ready (autoloads run before scene nodes).
/// WorldManager._Ready() then calls ModManager.InitializeAll, which triggers Initialize().
/// </summary>
public partial class HelloWorldClientMod : Node, IClientMod
{
    public string ModName => "hello-world";
    public string[] Dependencies => System.Array.Empty<string>();

    public override void _Ready()
    {
        ModManager.Register(this);
    }

    public void Initialize(Node sceneRoot)
    {
        var panel = GD.Load<PackedScene>("res://mods/hello-world/ui/HelloWorldPanel.tscn").Instantiate();
        sceneRoot.AddChild(panel);
    }
}
