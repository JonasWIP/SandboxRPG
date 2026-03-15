#if MOD_CASINO
using Godot;
public partial class CoinPusherUI : Node3D
{
    private static readonly System.Collections.Generic.Dictionary<ulong, CoinPusherUI> _instances = new();
    public static void Open(ulong id) { GD.Print($"[CoinPusher] Open {id}"); }
    public static CoinPusherUI AttachToNode(Node3D node, ulong id)
    {
        var ui = new CoinPusherUI();
        node.AddChild(ui);
        _instances[id] = ui;
        return ui;
    }
}
#endif
