#if MOD_CASINO
using Godot;
public partial class BlackjackUI : Node3D
{
    private static readonly System.Collections.Generic.Dictionary<ulong, BlackjackUI> _instances = new();
    public static void Open(ulong id) { GD.Print($"[Blackjack] Open {id}"); }
    public static BlackjackUI AttachToNode(Node3D node, ulong id)
    {
        var ui = new BlackjackUI();
        node.AddChild(ui);
        _instances[id] = ui;
        return ui;
    }
}
#endif
