#if MOD_CASINO
using Godot;
using SandboxRPG;
using System.Collections.Generic;
using SpacetimeDB.Types;

/// <summary>
/// Manages the blackjack table interaction:
/// - Shows seat selection popup
/// - Spawns 3D card MeshInstance3D objects on the felt surface
/// - Subscribes to BlackjackGame + BlackjackSeat updates
/// </summary>
public partial class BlackjackUI : Node3D
{
    private static readonly Dictionary<ulong, BlackjackUI> _instances = new();

    private ulong _machineId;
    private CanvasLayer? _popup;
    private readonly Dictionary<string, MeshInstance3D> _cardNodes = new();
    private static readonly Vector3 FeltOrigin = new(0f, 0.55f, 0f);
    private const float CardSpacing = 0.22f;

    public static void Open(ulong machineId)
    {
        if (!_instances.TryGetValue(machineId, out var ui)) return;
        ui.ShowSeatPopup();
    }

    public static BlackjackUI AttachToNode(Node3D tableNode, ulong machineId)
    {
        var ui = new BlackjackUI { _machineId = machineId };
        tableNode.AddChild(ui);
        _instances[machineId] = ui;
        return ui;
    }

    public override void _Ready()
    {
        GameManager.Instance.Conn.Db.BlackjackSeat.OnInsert += (_, _) => CallDeferred(nameof(RefreshCards));
        GameManager.Instance.Conn.Db.BlackjackSeat.OnUpdate += (_, _, _) => CallDeferred(nameof(RefreshCards));
        GameManager.Instance.Conn.Db.BlackjackSeat.OnDelete += (_, _) => CallDeferred(nameof(RefreshCards));
        GameManager.Instance.Conn.Db.BlackjackGame.OnUpdate += (_, _, _) => CallDeferred(nameof(RefreshCards));
    }

    private void ShowSeatPopup()
    {
        if (_popup != null) { _popup.Visible = true; return; }
        _popup = new CanvasLayer();
        var panel = new Panel();
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.Size = new Vector2(260, 200);
        panel.Position = new Vector2(-130, -100);

        var vbox = new VBoxContainer { Position = new Vector2(10, 10) };
        vbox.AddChild(new Label { Text = "Choose a seat (0-3):" });

        for (byte i = 0; i < 4; i++)
        {
            byte idx = i;
            var btn = new Button { Text = $"Seat {idx}" };
            btn.Pressed += () => OnSeatChosen(idx);
            vbox.AddChild(btn);
        }
        var leave = new Button { Text = "Leave Table" };
        leave.Pressed += () => {
            GameManager.Instance.Conn.Reducers.LeaveBlackjack(_machineId);
            _popup.Visible = false;
        };
        vbox.AddChild(leave);
        panel.AddChild(vbox);
        _popup.AddChild(panel);
        GetTree().Root.AddChild(_popup);
    }

    private void OnSeatChosen(byte seatIndex)
    {
        _popup.Visible = false;
        GameManager.Instance.Conn.Reducers.JoinBlackjack(_machineId, seatIndex);
        ShowBetPopup();
    }

    private void ShowBetPopup()
    {
        var layer = new CanvasLayer();
        var panel = new Panel();
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.Size = new Vector2(240, 160);
        panel.Position = new Vector2(-120, -80);
        var vbox = new VBoxContainer { Position = new Vector2(10, 10) };
        vbox.AddChild(new Label { Text = "Your bet (Copper):" });
        var spin = new SpinBox { MinValue = 1, MaxValue = 10000, Value = 10 };
        vbox.AddChild(spin);
        var betBtn = new Button { Text = "Place Bet" };
        betBtn.Pressed += () => {
            GameManager.Instance.Conn.Reducers.PlaceBet(_machineId, (ulong)spin.Value);
            layer.QueueFree();
        };
        var startBtn = new Button { Text = "Start Round" };
        startBtn.Pressed += () => {
            GameManager.Instance.Conn.Reducers.StartBlackjackRound(_machineId);
            layer.QueueFree();
        };
        vbox.AddChild(betBtn);
        vbox.AddChild(startBtn);
        panel.AddChild(vbox);
        layer.AddChild(panel);
        GetTree().Root.AddChild(layer);
    }

    public void RefreshCards()
    {
        foreach (var node in _cardNodes.Values) node.QueueFree();
        _cardNodes.Clear();

        var game = GameManager.Instance.Conn.Db.BlackjackGame.MachineId.Find(_machineId);
        if (game == null) return;

        string dealerHand = game.State >= 2 ? game.DealerHand : game.DealerHandHidden;
        SpawnCardRow(dealerHand, new Vector3(-0.5f, 0f, -0.3f));

        var seats = new List<BlackjackSeat>();
        foreach (var s in GameManager.Instance.Conn.Db.BlackjackSeat.Iter())
            if (s.MachineId == _machineId && s.RoundId == game.RoundId)
                seats.Add(s);

        for (int si = 0; si < seats.Count; si++)
        {
            float xOffset = -0.6f + si * 0.4f;
            SpawnCardRow(seats[si].Hand, new Vector3(xOffset, 0f, 0.4f));
        }
    }

    private void SpawnCardRow(string hand, Vector3 origin)
    {
        if (string.IsNullOrEmpty(hand)) return;
        var cards = hand.Split(',');
        for (int i = 0; i < cards.Length; i++)
        {
            var node = CreateCardMesh(cards[i]);
            node.Position = origin + new Vector3(i * CardSpacing, 0, 0);
            AddChild(node);
            _cardNodes[$"{origin}_{i}"] = node;
        }
    }

    private static MeshInstance3D CreateCardMesh(string code)
    {
        var mesh = new MeshInstance3D();
        var box = new BoxMesh { Size = new Vector3(0.15f, 0.002f, 0.22f) };
        mesh.Mesh = box;
        var mat = new StandardMaterial3D();
        bool isHidden = code == "??";
        mat.AlbedoColor = isHidden ? new Color(0.1f, 0.1f, 0.8f) : Colors.White;
        if (!isHidden)
        {
            var label = new Label3D { Text = code, FontSize = 16 };
            label.Position = new Vector3(0, 0.01f, 0);
            label.RotationDegrees = new Vector3(-90, 0, 0);
            mesh.AddChild(label);
        }
        mesh.MaterialOverride = mat;
        return mesh;
    }

    public override void _ExitTree()
    {
        _instances.Remove(_machineId);
    }
}
#endif
