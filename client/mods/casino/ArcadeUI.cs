#if MOD_CASINO
using Godot;
public partial class ArcadeUI : Node
{
    private static readonly System.Collections.Generic.Dictionary<ulong, ArcadeUI> _instances = new();
    private bool _isActive;
    public static void Open(ulong id, bool isPattern) { GD.Print($"[Arcade] Open {id} pattern={isPattern}"); }
    public static void TryTriggerReaction(ulong id)
    {
        if (_instances.TryGetValue(id, out var ui) && ui._isActive)
            GD.Print($"[Arcade] Trigger reaction {id}");
        else
            Open(id, isPattern: false);
    }
    public static ArcadeUI AttachToNode(Node3D node, ulong id, bool isPattern)
    {
        var ui = new ArcadeUI();
        node.AddChild(ui);
        _instances[id] = ui;
        return ui;
    }
}
#endif
