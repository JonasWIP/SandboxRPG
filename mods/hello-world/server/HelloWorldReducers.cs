// mods/hello-world/server/HelloWorldReducers.cs
using SpacetimeDB;

namespace SandboxRPG.Server;

public static partial class Module
{
    [Reducer]
    public static void SayHello(ReducerContext ctx, string message)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > 256)
            throw new Exception("Invalid message.");

        var existing = ctx.Db.HelloWorldMessage.PlayerId.Find(ctx.Sender);
        if (existing is not null)
        {
            var row = existing.Value;
            row.Message = message;
            ctx.Db.HelloWorldMessage.PlayerId.Update(row);
        }
        else
        {
            ctx.Db.HelloWorldMessage.Insert(new HelloWorldMessage
            {
                PlayerId = ctx.Sender,
                Message  = message,
            });
        }
    }
}
