// client/scripts/mods/IClientMod.cs
using Godot;

namespace SandboxRPG;

public interface IClientMod
{
    string   ModName      { get; }
    string[] Dependencies { get; }  // mod names that must Initialize before this one
    void     Initialize(Node sceneRoot);
}
