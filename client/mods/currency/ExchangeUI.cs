#if MOD_CURRENCY
using Godot;
public partial class ExchangeUI : Node { public static void Open(ulong id) { GD.Print($"[Exchange] Open {id}"); } }
#endif
