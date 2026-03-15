#if MOD_CASINO
using Godot;
public partial class SlotMachineUI : Node
{
    private static readonly System.Collections.Generic.Dictionary<ulong, SlotMachineUI> _instances = new();
    public static void Open(ulong id) { GD.Print($"[Slot] Open {id}"); }
    public static SlotMachineUI AttachToNode(Node3D node, ulong id)
    {
        var ui = new SlotMachineUI();
        node.AddChild(ui);
        _instances[id] = ui;
        return ui;
    }
}
#endif
