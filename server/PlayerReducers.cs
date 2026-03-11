using System;
using SpacetimeDB;

namespace SandboxRPG.Server;

public static partial class Module
{
    // =========================================================================
    // PLAYER REDUCERS
    // =========================================================================

    [Reducer]
    public static void SetName(ReducerContext ctx, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 32)
            throw new Exception("Name must be 1–32 characters.");

        var existing = ctx.Db.Player.Identity.Find(ctx.Sender);
        if (existing is not null)
        {
            var player = existing.Value;
            player.Name = name;
            ctx.Db.Player.Identity.Update(player);
        }
    }

    [Reducer]
    public static void MovePlayer(ReducerContext ctx, float posX, float posY, float posZ, float rotY)
    {
        var existing = ctx.Db.Player.Identity.Find(ctx.Sender);
        if (existing is null) return;

        var player = existing.Value;

        // Anti-cheat: reject moves > ~20 units per tick
        float dx = posX - player.PosX;
        float dy = posY - player.PosY;
        float dz = posZ - player.PosZ;
        if (dx * dx + dy * dy + dz * dz > 400f)
        {
            Log.Warn($"Player {player.Name} tried to teleport — rejected.");
            return;
        }

        player.PosX = posX;
        player.PosY = posY;
        player.PosZ = posZ;
        player.RotY = rotY;
        ctx.Db.Player.Identity.Update(player);
    }

    // =========================================================================
    // CHAT REDUCER
    // =========================================================================

    [Reducer]
    public static void SendChat(ReducerContext ctx, string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 256) return;

        var player = ctx.Db.Player.Identity.Find(ctx.Sender);
        string senderName = player?.Name ?? "Unknown";

        ctx.Db.ChatMessage.Insert(new ChatMessage
        {
            SenderId = ctx.Sender,
            SenderName = senderName,
            Text = text,
            Timestamp = (ulong)((DateTimeOffset)ctx.Timestamp).ToUnixTimeMilliseconds() * 1000,
        });
    }
}
