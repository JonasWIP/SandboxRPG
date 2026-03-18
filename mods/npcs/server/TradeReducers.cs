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

        // Range check
        float dx = p.PosX - npc.PosX;
        float dz = p.PosZ - npc.PosZ;
        if (dx * dx + dz * dz > GameConstants.TradeRangeSq) return;

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

        // Check and deduct currency
        uint currencyHeld = CountInventoryItem(ctx, ctx.Sender, currency);
        if (currencyHeld < (uint)totalCost)
        {
            Log.Warn($"[TradeReducers] Player lacks {currency}: has {currencyHeld}, needs {totalCost}");
            return;
        }
        ConsumeFromInventory(ctx, ctx.Sender, currency, (uint)totalCost);

        // Give purchased item
        AddOrStackInventoryItem(ctx, ctx.Sender, buyItemType, quantity);

        Log.Info($"[TradeReducers] Player bought {quantity}x {buyItemType} for {totalCost} {currency}");
    }

    [Reducer]
    public static void NpcSellItem(ReducerContext ctx, ulong npcId, ulong inventoryItemId, uint quantity)
    {
        Log.Info($"[NpcSellItem] Called: npcId={npcId}, itemId={inventoryItemId}, qty={quantity}, sender={ctx.Sender}");

        var player = ctx.Db.Player.Identity.Find(ctx.Sender);
        if (player is null) { Log.Warn("[NpcSellItem] Player not found"); return; }
        var p = player.Value;

        var npcRow = ctx.Db.Npc.Id.Find(npcId);
        if (npcRow is null) { Log.Warn($"[NpcSellItem] NPC {npcId} not found"); return; }
        var npc = npcRow.Value;

        if (!npc.IsAlive) { Log.Warn("[NpcSellItem] NPC not alive"); return; }

        var cfg = FindNpcConfig(ctx, npc.NpcType);
        if (cfg is null || !cfg.Value.IsTrader) { Log.Warn("[NpcSellItem] NPC not a trader"); return; }

        // Range check
        float dx = p.PosX - npc.PosX;
        float dz = p.PosZ - npc.PosZ;
        float distSq = dx * dx + dz * dz;
        if (distSq > GameConstants.TradeRangeSq) { Log.Warn($"[NpcSellItem] Out of range: {distSq}"); return; }

        // Find the inventory item
        var itemRow = ctx.Db.InventoryItem.Id.Find(inventoryItemId);
        if (itemRow is null) { Log.Warn($"[NpcSellItem] Item {inventoryItemId} not found"); return; }
        var item = itemRow.Value;
        if (item.OwnerId != ctx.Sender) { Log.Warn("[NpcSellItem] Not item owner"); return; }
        if (quantity > item.Quantity || quantity == 0) { Log.Warn($"[NpcSellItem] Bad qty: {quantity} vs {item.Quantity}"); return; }

        // Calculate sell price (half of buy price if listed, otherwise minimum)
        int pricePerUnit = GameConstants.MinSellPrice;
        foreach (var offer in ctx.Db.NpcTradeOffer.Iter())
        {
            if (offer.NpcType == npc.NpcType && offer.ItemType == item.ItemType)
            { pricePerUnit = System.Math.Max(GameConstants.MinSellPrice, (int)(offer.Price * GameConstants.SellPriceMultiplier)); break; }
        }

        int totalEarned = pricePerUnit * (int)quantity;

        // Deduct items from inventory
        if (quantity >= item.Quantity)
        {
            ctx.Db.InventoryItem.Delete(item);
        }
        else
        {
            var updated = item;
            updated.Quantity -= quantity;
            ctx.Db.InventoryItem.Id.Update(updated);
        }

        // Give currency
        AddOrStackInventoryItem(ctx, ctx.Sender, "copper_coin", (uint)totalEarned);

        Log.Info($"[TradeReducers] Player sold {quantity}x {item.ItemType} for {totalEarned} copper_coin");
    }
}
