#if MOD_CURRENCY
using SpacetimeDB;

namespace SandboxRPG.Server;

public static partial class Module
{
    [Table(Name = "currency_balance", Public = true)]
    public partial struct CurrencyBalance
    {
        [PrimaryKey]
        public Identity PlayerId;
        public ulong Copper; // Silver=Copper/100, Gold=Copper/10000 (display only)
    }

    [Table(Name = "currency_transaction", Public = true)]
    public partial struct CurrencyTransaction
    {
        [PrimaryKey, AutoInc]
        public ulong Id;
        public Identity PlayerId;
        public long Amount;   // positive=credit, negative=debit
        public string Reason;
        public ulong Timestamp;
    }
}
#endif
