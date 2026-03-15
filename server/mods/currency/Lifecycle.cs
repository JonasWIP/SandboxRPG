#if MOD_CURRENCY
using SpacetimeDB;

namespace SandboxRPG.Server;

public static partial class Module
{
    /// <summary>
    /// Internal — call from ClientConnected. Idempotent: only creates row if missing.
    /// Awards 500 Copper starting balance on first connect.
    /// </summary>
    internal static void GrantStartingBalance(ReducerContext ctx, Identity identity)
    {
        if (ctx.Db.CurrencyBalance.PlayerId.Find(identity) != null) return;
        CreditCoins(ctx, identity, 500, "starting_balance");
    }
}
#endif
