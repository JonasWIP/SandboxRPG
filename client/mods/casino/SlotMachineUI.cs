#if MOD_CASINO
using Godot;
using SandboxRPG;
using SpacetimeDB.Types;

/// <summary>
/// Manages the slot machine SubViewport display and bet popup.
/// Attaches to the machine node; one instance per placed slot machine.
/// </summary>
public partial class SlotMachineUI : Node
{
    private static readonly System.Collections.Generic.Dictionary<ulong, SlotMachineUI> _instances = new();

    private ulong _machineId;
    private SubViewport _viewport;
    private Label _reelLabel;
    private Label _winLabel;
    private CanvasLayer _betPopup;
    private SpinBox _betSpinBox;
    private bool _animating;

    public static void Open(ulong machineId)
    {
        // Show bet popup as a CanvasLayer overlay
        if (!_instances.TryGetValue(machineId, out var ui)) return;
        ui.ShowBetPopup();
    }

    public static SlotMachineUI AttachToNode(Node3D machineNode, ulong machineId)
    {
        var ui = new SlotMachineUI();
        ui._machineId = machineId;
        machineNode.AddChild(ui);
        _instances[machineId] = ui;
        return ui;
    }

    public override void _Ready()
    {
        // Build SubViewport for the machine screen
        _viewport = new SubViewport { Size = new Vector2I(256, 128), TransparentBg = true };
        AddChild(_viewport);

        var bg = new ColorRect { Color = new Color(0.05f, 0.05f, 0.2f), Size = new Vector2(256, 128) };
        _viewport.AddChild(bg);

        _reelLabel = new Label { Text = "? ? ?", Position = new Vector2(20, 30) };
        _reelLabel.AddThemeFontSizeOverride("font_size", 32);
        _viewport.AddChild(_reelLabel);

        _winLabel = new Label { Position = new Vector2(20, 90) };
        _winLabel.AddThemeFontSizeOverride("font_size", 14);
        _viewport.AddChild(_winLabel);

        // Attach viewport texture to the Screen mesh on the machine node
        ApplyScreenTexture();

        // Subscribe to SlotSession updates
        GameManager.Instance.Conn.Db.SlotSession.OnUpdate += OnSessionUpdate;
    }

    private void ApplyScreenTexture()
    {
        var parent = GetParent<Node3D>();
        if (parent == null) return;
        var screenMesh = parent.FindChild("Screen") as MeshInstance3D;
        if (screenMesh == null) return;
        var mat = new StandardMaterial3D { AlbedoTexture = _viewport.GetTexture() };
        screenMesh.MaterialOverride = mat;
    }

    private void OnSessionUpdate(SpacetimeDB.Types.EventContext ctx, SlotSession oldVal, SlotSession newVal)
    {
        if (newVal.MachineId != _machineId) return;
        var captured = newVal;
        Callable.From(() => UpdateDisplay(captured)).CallDeferred();
    }

    private void UpdateDisplay(SlotSession session)
    {
        if (session.IsIdle)
        {
            _reelLabel.Text = "? ? ?";
            _winLabel.Text = "Insert coins!";
            return;
        }

        // Play spin animation then show result
        if (!_animating)
        {
            _animating = true;
            AnimateReels(session);
        }
    }

    private async void AnimateReels(SlotSession session)
    {
        // Fake spin: cycle through symbols for 1.5s
        var symbols = new[] { "Ch", "Le", "Or", "St", "Ge", "Be" };
        var rng = new System.Random();
        double elapsed = 0;
        while (elapsed < 1.5)
        {
            _reelLabel.Text = $"{symbols[rng.Next(6)]} {symbols[rng.Next(6)]} {symbols[rng.Next(6)]}";
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            elapsed += GetProcessDeltaTime();
        }
        // Show real result
        var parts = session.Reels.Split('|');
        _reelLabel.Text = string.Join(" ", parts);
        _winLabel.Text = session.WinAmount > 0 ? $"WIN: {session.WinAmount} Cu" : "No luck!";
        _animating = false;

        // Auto-release after 3s if this is our session
        var localPlayer = GameManager.Instance.GetLocalPlayer();
        if (localPlayer != null && session.PlayerId == localPlayer.Identity)
        {
            await ToSignal(GetTree().CreateTimer(3.0), SceneTreeTimer.SignalName.Timeout);
            GameManager.Instance.Conn.Reducers.ReleaseSlot(_machineId);
        }
    }

    private void ShowBetPopup()
    {
        if (_betPopup != null) { _betPopup.Visible = true; return; }
        _betPopup = new CanvasLayer();
        var panel = new Panel();
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.Size = new Vector2(220, 120);
        panel.Position = new Vector2(-110, -60);

        var vbox = new VBoxContainer { Position = new Vector2(10, 10), Size = new Vector2(200, 100) };
        var lbl = new Label { Text = "Spin amount (Copper):" };
        _betSpinBox = new SpinBox { MinValue = 1, MaxValue = 10000, Value = 10, Step = 1 };
        var btn = new Button { Text = "SPIN" };
        btn.Pressed += OnSpinPressed;

        vbox.AddChild(lbl);
        vbox.AddChild(_betSpinBox);
        vbox.AddChild(btn);
        panel.AddChild(vbox);
        _betPopup.AddChild(panel);
        GetTree().Root.AddChild(_betPopup);
    }

    private void OnSpinPressed()
    {
        _betPopup.Visible = false;
        ulong bet = (ulong)_betSpinBox.Value;
        GameManager.Instance.Conn.Reducers.SpinSlot(_machineId, bet);
    }

    public override void _ExitTree()
    {
        _instances.Remove(_machineId);
        GameManager.Instance.Conn.Db.SlotSession.OnUpdate -= OnSessionUpdate;
    }
}
#endif
