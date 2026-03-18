// mods/npcs/server/TradeReducers.cs
using SpacetimeDB;

namespace SandboxRPG.Server;

public static partial class Module
{
    [Reducer]
    public static void NpcTrade(ReducerContext ctx, ulong npcId, string buyItemType, uint quantity)
    {
        var player = ctx.Db.Player.Identity.Find(ctx.Sender);
        if (player is null) return;
        var p = player.Value;

        var npcRow = ctx.Db.Npc.Id.Find(npcId);
        if (npcRow is null) return;
        var npc = npcRow.Value;

        if (!npc.IsAlive) return;

        // Check NPC is a trader
        var cfg = FindNpcConfig(ctx, npc.NpcType);
        if (cfg is null || !cfg.Value.IsTrader) return;

        // Range check (5.0 units)
        float dx = p.PosX - npc.PosX;
        float dz = p.PosZ - npc.PosZ;
        if (dx * dx + dz * dz > 5.0f * 5.0f) return;

        // Find trade offer
        NpcTradeOffer? offer = null;
        foreach (var o in ctx.Db.NpcTradeOffer.Iter())
        {
            if (o.NpcType == npc.NpcType && o.ItemType == buyItemType)
            { offer = o; break; }
        }
        if (offer is null) return;

        int totalCost = offer.Value.Price * (int)quantity;
        string currency = offer.Value.Currency;

        // Check player has enough currency
        uint currencyHeld = 0;
        foreach (var item in ctx.Db.InventoryItem.Iter())
            if (item.OwnerId == ctx.Sender && item.ItemType == currency)
                currencyHeld += item.Quantity;

        if (currencyHeld < (uint)totalCost)
        {
            Log.Warn($"[TradeReducers] Player lacks {currency}: has {currencyHeld}, needs {totalCost}");
            return;
        }

        // Deduct currency (consume from first matching stacks)
        int remaining = totalCost;
        var toDelete = new System.Collections.Generic.List<InventoryItem>();
        var toUpdate = new System.Collections.Generic.List<(InventoryItem old, uint newQty)>();
        foreach (var item in ctx.Db.InventoryItem.Iter())
        {
            if (remaining <= 0) break;
            if (item.OwnerId != ctx.Sender || item.ItemType != currency) continue;

            if (item.Quantity <= (uint)remaining)
            {
                remaining -= (int)item.Quantity;
                toDelete.Add(item);
            }
            else
            {
                toUpdate.Add((item, item.Quantity - (uint)remaining));
                remaining = 0;
            }
        }
        foreach (var item in toDelete) ctx.Db.InventoryItem.Delete(item);
        foreach (var (old, newQty) in toUpdate)
        {
            var updated = old;
            updated.Quantity = newQty;
            ctx.Db.InventoryItem.Id.Update(updated);
        }

        // Give purchased item
        ctx.Db.InventoryItem.Insert(new InventoryItem
        {
            OwnerId = ctx.Sender,
            ItemType = buyItemType,
            Quantity = quantity,
            Slot = -1,
        });

        Log.Info($"[TradeReducers] Player bought {quantity}x {buyItemType} for {totalCost} {currency}");
    }
}
