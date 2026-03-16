// client/scripts/mods/IClientMod.cs
using Godot;

namespace SandboxRPG;

public interface IClientMod
{
    string ModName { get; }
    void Initialize(Node sceneRoot);
}
