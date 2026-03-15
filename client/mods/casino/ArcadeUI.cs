#if MOD_CASINO
using Godot;
using SandboxRPG;
using System.Collections.Generic;
using SpacetimeDB.Types;

/// <summary>
/// Handles both arcade machine types (Reaction and Pattern).
/// Reaction: renders an animated needle on the machine's SubViewport screen.
/// Pattern: shows colored flashes, player presses R/G/B/Y keys in order.
/// </summary>
public partial class ArcadeUI : Node
{
    private static readonly Dictionary<ulong, ArcadeUI> _instances = new();

    private ulong _machineId;
    private bool _isPattern;
    private SubViewport _viewport;
    private CanvasLayer? _betPopup;
    private Label _feedbackLabel;
    private bool _isActive;

    // Pattern input state
    private string _currentChallenge = "";
    private string _playerInput = "";

    public static void Open(ulong machineId, bool isPattern)
    {
        if (!_instances.TryGetValue(machineId, out var ui)) return;
        ui.ShowBetPopup();
    }

    public static void TryTriggerReaction(ulong machineId)
    {
        if (_instances.TryGetValue(machineId, out var ui) && ui._isActive)
            ui.TriggerReactionInput();
        else
            Open(machineId, isPattern: false);
    }

    public static ArcadeUI AttachToNode(Node3D machineNode, ulong machineId, bool isPattern)
    {
        var ui = new ArcadeUI { _machineId = machineId, _isPattern = isPattern };
        machineNode.AddChild(ui);
        _instances[machineId] = ui;
        return ui;
    }

    public override void _Ready()
    {
        _viewport = new SubViewport { Size = new Vector2I(256, 256), TransparentBg = true };
        AddChild(_viewport);

        var bg = new ColorRect
        {
            Color = _isPattern ? new Color(0.15f, 0.05f, 0.25f) : new Color(0.05f, 0.1f, 0.1f),
            Size = new Vector2(256, 256)
        };
        _viewport.AddChild(bg);

        _feedbackLabel = new Label { Position = new Vector2(10, 10) };
        _feedbackLabel.AddThemeFontSizeOverride("font_size", 18);
        _viewport.AddChild(_feedbackLabel);

        ApplyScreenTexture();
        GameManager.Instance.Conn.Db.ArcadeSession.OnUpdate += OnSessionUpdate;
    }

    private void ApplyScreenTexture()
    {
        var parent = GetParent<Node3D>();
        if (parent == null) return;
        var screen = parent.FindChild("Screen") as MeshInstance3D;
        if (screen == null) return;
        var mat = new StandardMaterial3D { AlbedoTexture = _viewport.GetTexture() };
        screen.MaterialOverride = mat;
    }

    private void OnSessionUpdate(SpacetimeDB.Types.EventContext ctx, ArcadeSession old, ArcadeSession newVal)
    {
        if (newVal.MachineId != _machineId) return;
        var captured = newVal;
        Callable.From(() => HandleSessionChange(captured)).CallDeferred();
    }

    private void HandleSessionChange(ArcadeSession session)
    {
        var localPlayer = GameManager.Instance.GetLocalPlayer();
        _isActive = session.State == 1 && localPlayer != null && session.PlayerId == localPlayer.Identity;

        if (session.State == 1) // Active
        {
            if (_isPattern) StartPatternGame(session.ChallengeData);
            else StartReactionGame(session);
        }
        else if (session.State == 0) // Result
        {
            _isActive = false;
            bool won = session.ChallengeData is "HIT" or "CORRECT";
            _feedbackLabel.Text = won ? "WIN!" : "Miss...";
        }
    }

    // ── Reaction game ────────────────────────────────────────────────────────

    private async void StartReactionGame(ArcadeSession session)
    {
        var parts = session.ChallengeData.Split(':');
        int targetMs = int.Parse(parts[0]);

        _feedbackLabel.Text = "Watch the needle...";

        var needle = new Line2D
        {
            Points = new[] { new Vector2(128, 128), new Vector2(128, 20) },
            Width = 4,
            DefaultColor = Colors.Red
        };
        _viewport.AddChild(needle);

        double elapsed = 0;
        double total = targetMs / 1000.0;
        while (elapsed < total + 1.0)
        {
            float angle = Mathf.Lerp(-70f, 70f, (float)(elapsed / total));
            needle.Rotation = Mathf.DegToRad(angle);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            elapsed += GetProcessDeltaTime();
        }
        needle.QueueFree();
        _feedbackLabel.Text = "Too late!";
    }

    public void TriggerReactionInput()
    {
        GameManager.Instance.Conn.Reducers.ArcadeInputReaction(_machineId);
    }

    // ── Pattern game ─────────────────────────────────────────────────────────

    private async void StartPatternGame(string challenge)
    {
        _currentChallenge = challenge;
        _playerInput = "";

        var colorMap = new Dictionary<char, Color>
        {
            ['R'] = Colors.Red, ['G'] = Colors.Green,
            ['B'] = Colors.Blue, ['Y'] = Colors.Yellow
        };

        for (int i = 0; i < challenge.Length; i++)
        {
            char c = challenge[i];
            _feedbackLabel.Text = $"Remember: {c}";
            var flash = new ColorRect { Color = colorMap[c], Size = new Vector2(256, 200), Position = new Vector2(0, 50) };
            _viewport.AddChild(flash);
            await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
            flash.QueueFree();
            await ToSignal(GetTree().CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);
        }
        _feedbackLabel.Text = "Your turn! (R/G/B/Y keys)";
    }

    public override void _Input(InputEvent @event)
    {
        if (!_isPattern || _currentChallenge.Length == 0) return;
        var keyMap = new Dictionary<Key, char>
        {
            [Key.R] = 'R', [Key.G] = 'G', [Key.B] = 'B', [Key.Y] = 'Y'
        };
        if (@event is InputEventKey keyEvent && keyEvent.Pressed &&
            keyMap.TryGetValue(keyEvent.Keycode, out char pressed))
        {
            _playerInput += pressed;
            _feedbackLabel.Text = $"Input: {_playerInput}";
            if (_playerInput.Length == _currentChallenge.Length)
            {
                GameManager.Instance.Conn.Reducers.ArcadeInputPattern(_machineId, _playerInput);
                _currentChallenge = "";
                _playerInput = "";
            }
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
        var vbox = new VBoxContainer { Position = new Vector2(10, 10) };
        vbox.AddChild(new Label { Text = _isPattern ? "Pattern Game - Bet (Copper):" : "Reaction Game - Bet (Copper):" });
        var spin = new SpinBox { MinValue = 1, MaxValue = 5000, Value = 10 };
        vbox.AddChild(spin);
        var btn = new Button { Text = "START" };
        btn.Pressed += () => {
            _betPopup!.Visible = false;
            GameManager.Instance.Conn.Reducers.StartArcade(_machineId, (ulong)spin.Value);
        };
        vbox.AddChild(btn);
        panel.AddChild(vbox);
        _betPopup.AddChild(panel);
        GetTree().Root.AddChild(_betPopup);
    }

    public override void _ExitTree()
    {
        _instances.Remove(_machineId);
        GameManager.Instance.Conn.Db.ArcadeSession.OnUpdate -= OnSessionUpdate;
    }
}
#endif
