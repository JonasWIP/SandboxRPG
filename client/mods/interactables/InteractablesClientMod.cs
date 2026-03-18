// client/mods/interactables/InteractablesClientMod.cs
using Godot;

namespace SandboxRPG;

public partial class InteractablesClientMod : Node, IClientMod
{
    public string ModName => "interactables";
    public string[] Dependencies => new[] { "base" };

    public override void _Ready() => ModManager.Register(this);

    public void Initialize(Node sceneRoot) => InteractablesContent.RegisterAll();
}
