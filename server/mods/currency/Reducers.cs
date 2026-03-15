#if MOD_CURRENCY
using SpacetimeDB;
using System.Collections.Generic;
using System.Linq;

namespace SandboxRPG.Server;

public static partial class Module
{
    // ── Internal helpers (not [Reducer] — not callable by clients) ────────────

    internal static void CreditCoins(ReducerContext ctx, Identity playerId, ulong amount, string reason)
    {
        var existing = ctx.Db.CurrencyBalance.PlayerId.Find(playerId);
        if (existing is null)
        {
            ctx.Db.CurrencyBalance.Insert(new CurrencyBalance { PlayerId = playerId, Copper = amount });
        }
        else
        {
            var row = existing.Value;
            ctx.Db.CurrencyBalance.Delete(row);
            ctx.Db.CurrencyBalance.Insert(new CurrencyBalance { PlayerId = playerId, Copper = row.Copper + amount });
        }
        ctx.Db.CurrencyTransaction.Insert(new CurrencyTransaction
        {
            PlayerId = playerId,
            Amount = (long)amount,
            Reason = reason,
            Timestamp = (ulong)((DateTimeOffset)ctx.Timestamp).ToUnixTimeMilliseconds() * 1000
        });
    }

    internal static void DebitCoins(ReducerContext ctx, Identity playerId, ulong amount, string reason)
    {
        var existingNullable = ctx.Db.CurrencyBalance.PlayerId.Find(playerId);
        if (existingNullable is null)
            throw new Exception("No currency balance found");
        var existing = existingNullable.Value;
        if (existing.Copper < amount)
            throw new Exception($"Insufficient funds: have {existing.Copper}, need {amount}");
        ctx.Db.CurrencyBalance.Delete(existing);
        ctx.Db.CurrencyBalance.Insert(new CurrencyBalance { PlayerId = playerId, Copper = existing.Copper - amount });
        ctx.Db.CurrencyTransaction.Insert(new CurrencyTransaction
        {
            PlayerId = playerId,
            Amount = -(long)amount,
            Reason = reason,
            Timestamp = (ulong)((DateTimeOffset)ctx.Timestamp).ToUnixTimeMilliseconds() * 1000
        });
    }

    // ── Public reducers ───────────────────────────────────────────────────────

    /// <summary>Exchange raw resources for Copper. qty must be a multiple of batch size.</summary>
    [Reducer]
    public static void ExchangeResources(ReducerContext ctx, string resourceType, uint qty)
    {
        // Batch rates: (batchSize, copperYield)
        var rates = new Dictionary<string, (uint batch, uint yield)>
        {
            ["wood"]  = (10, 5),
            ["stone"] = (5,  5),
            ["iron"]  = (1,  20),
        };
        if (!rates.TryGetValue(resourceType, out var rate))
            throw new Exception($"Resource '{resourceType}' has no exchange rate");
        if (qty % rate.batch != 0)
            throw new Exception($"qty must be a multiple of {rate.batch} for {resourceType}");

        uint batches = qty / rate.batch;
        ulong payout = (ulong)(batches * rate.yield);

        // Consume from inventory
        var items = ctx.Db.InventoryItem.Iter().Where(i => i.OwnerId == ctx.Sender);
        uint remaining = qty;
        foreach (var item in items)
        {
            if (item.ItemType != resourceType) continue;
            if (item.Quantity <= remaining)
            {
                remaining -= item.Quantity;
                ctx.Db.InventoryItem.Delete(item);
            }
            else
            {
                var updated = item;
                updated.Quantity -= remaining;
                remaining = 0;
                ctx.Db.InventoryItem.Delete(item);
                ctx.Db.InventoryItem.Insert(updated);
            }
            if (remaining == 0) break;
        }
        if (remaining > 0) throw new Exception($"Not enough {resourceType} in inventory");

        CreditCoins(ctx, ctx.Sender, payout, $"exchange:{resourceType}x{qty}");
    }

    /// <summary>Move Copper from wallet balance to physical coin inventory items.</summary>
    [Reducer]
    public static void WithdrawCoins(ReducerContext ctx, string denomination, uint amount)
    {
        ulong costPerUnit = denomination switch
        {
            "copper" => 1,
            "silver" => 100,
            "gold"   => 10000,
            _ => throw new Exception($"Unknown denomination: {denomination}")
        };
        string itemType = $"coin_{denomination}";
        ulong totalCost = costPerUnit * amount;

        DebitCoins(ctx, ctx.Sender, totalCost, $"withdraw:{denomination}x{amount}");

        // Grant coin items
        var existing = ctx.Db.InventoryItem.Iter().Where(i => i.OwnerId == ctx.Sender)
            .FirstOrDefault(i => i.ItemType == itemType && i.Slot == -1);
        if (existing.ItemType == itemType) // found (struct default check)
        {
            ctx.Db.InventoryItem.Delete(existing);
            ctx.Db.InventoryItem.Insert(new InventoryItem
            {
                OwnerId = ctx.Sender, ItemType = itemType,
                Quantity = existing.Quantity + amount, Slot = -1
            });
        }
        else
        {
            ctx.Db.InventoryItem.Insert(new InventoryItem
            {
                OwnerId = ctx.Sender, ItemType = itemType, Quantity = amount, Slot = -1
            });
        }
    }

    /// <summary>Convert physical coin inventory items back to wallet Copper balance.</summary>
    [Reducer]
    public static void DepositCoins(ReducerContext ctx, string denomination, uint amount)
    {
        ulong valuePerUnit = denomination switch
        {
            "copper" => 1,
            "silver" => 100,
            "gold"   => 10000,
            _ => throw new Exception($"Unknown denomination: {denomination}")
        };
        string itemType = $"coin_{denomination}";
        ulong totalValue = valuePerUnit * amount;

        // Consume coin items from inventory
        var existing = ctx.Db.InventoryItem.Iter().Where(i => i.OwnerId == ctx.Sender)
            .FirstOrDefault(i => i.ItemType == itemType);
        if (existing.ItemType != itemType || existing.Quantity < amount)
            throw new Exception($"Not enough {itemType} in inventory");

        ctx.Db.InventoryItem.Delete(existing);
        if (existing.Quantity > amount)
            ctx.Db.InventoryItem.Insert(new InventoryItem
            {
                OwnerId = ctx.Sender, ItemType = itemType,
                Quantity = existing.Quantity - amount, Slot = existing.Slot
            });

        CreditCoins(ctx, ctx.Sender, totalValue, $"deposit:{denomination}x{amount}");
    }
}
#endif
