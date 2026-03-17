// server/mods/IMod.cs
using SpacetimeDB;

namespace SandboxRPG.Server.Mods;

public interface IMod
{
    string Name { get; }
    string Version { get; }
    string[] Dependencies { get; }  // mod names that must seed before this one
    void Seed(ReducerContext ctx);
    void OnClientConnected(ReducerContext ctx, Identity identity) { }    // default: no-op
    void OnClientDisconnected(ReducerContext ctx, Identity identity) { } // default: no-op
}
