// mods/hello-world/server/HelloWorldTables.cs
using SpacetimeDB;

namespace SandboxRPG.Server;

public static partial class Module
{
    /// <summary>One greeting row per player. Owned by hello-world mod.</summary>
    [Table(Name = "hello_world_message", Public = true)]
    public partial struct HelloWorldMessage
    {
        [PrimaryKey]
        public Identity PlayerId;
        public string Message;
    }
}
