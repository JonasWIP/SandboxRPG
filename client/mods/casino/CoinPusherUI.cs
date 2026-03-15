#if MOD_CASINO
using Godot;
using SandboxRPG;
using System.Collections.Generic;
using SpacetimeDB.Types;

/// <summary>
/// Spawns RigidBody3D coin physics objects when CoinCount increases.
/// On join, scatters CoinCount coins using MachineId as RNG seed.
/// Shows a small "Push" button overlay on E-press.
/// </summary>
public partial class CoinPusherUI : Node3D
{
    private static readonly Dictionary<ulong, CoinPusherUI> _instances = new();

    private ulong _machineId;
    private uint _spawnedCount;
    private CanvasLayer? _popup;
    private static readonly Vector3 PushEntry = new(0f, 1.2f, 0f);

    public static void Open(ulong machineId)
    {
        if (!_instances.TryGetValue(machineId, out var ui)) return;
        ui.ShowPushPopup();
    }

    public static CoinPusherUI AttachToNode(Node3D pusherNode, ulong machineId)
    {
        var ui = new CoinPusherUI { _machineId = machineId };
        pusherNode.AddChild(ui);
        _instances[machineId] = ui;
        return ui;
    }

    public override void _Ready()
    {
        // Rebuild initial coin pile from current state
        var state = GameManager.Instance.Conn.Db.CoinPusherState.MachineId.Find(_machineId);
        if (state != null) ScatterCoins(state.CoinCount);

        GameManager.Instance.Conn.Db.CoinPusherState.OnUpdate += OnStateUpdate;
    }

    private void OnStateUpdate(SpacetimeDB.Types.EventContext ctx, CoinPusherState old, CoinPusherState newState)
    {
        if (newState.MachineId != _machineId) return;
        var capturedNew = newState;
        var capturedOld = old;
        Callable.From(() => HandleStateChange(capturedNew, capturedOld)).CallDeferred();
    }

    private void HandleStateChange(CoinPusherState newState, CoinPusherState old)
    {
        if (newState.CoinCount > old.CoinCount)
        {
            // Spawn one new coin per push
            SpawnCoin(PushEntry + new Vector3(0, 0.2f, 0), applyImpulse: true);
            _spawnedCount++;
        }
        else if (newState.CoinCount == 0 && old.CoinCount > 0)
        {
            // Jackpot reset — clear all coin nodes
            foreach (var child in GetChildren())
                if (child is RigidBody3D) child.QueueFree();
            _spawnedCount = 0;
        }
    }

    private void ScatterCoins(uint count)
    {
        var rng = new System.Random((int)_machineId); // deterministic per machine
        for (uint i = 0; i < count; i++)
        {
            var pos = new Vector3(
                (float)(rng.NextDouble() * 0.8 - 0.4),
                0.5f + (float)(rng.NextDouble() * 0.3),
                (float)(rng.NextDouble() * 0.8 - 0.4)
            );
            SpawnCoin(pos, applyImpulse: false);
        }
        _spawnedCount = count;
    }

    private void SpawnCoin(Vector3 localPos, bool applyImpulse)
    {
        var body = new RigidBody3D { Mass = 0.01f };
        var mesh = new MeshInstance3D();
        var cyl = new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.04f, Height = 0.008f };
        mesh.Mesh = cyl;
        var mat = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.65f, 0.1f) };
        mesh.MaterialOverride = mat;

        var col = new CollisionShape3D();
        var cShape = new CylinderShape3D { Radius = 0.04f, Height = 0.008f };
        col.Shape = cShape;

        body.AddChild(mesh);
        body.AddChild(col);
        body.Position = localPos;
        AddChild(body);

        if (applyImpulse)
        {
            var b = body;
            Callable.From(() => b.ApplyCentralImpulse(new Vector3(0, -0.5f, 0.1f))).CallDeferred();
        }
    }

    private void ShowPushPopup()
    {
        if (_popup != null) { _popup.Visible = true; return; }
        _popup = new CanvasLayer();
        var panel = new Panel();
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.Size = new Vector2(200, 120);
        panel.Position = new Vector2(-100, -60);

        var vbox = new VBoxContainer { Position = new Vector2(10, 10) };
        vbox.AddChild(new Label { Text = "Push coins (Copper per push):" });
        var spin = new SpinBox { MinValue = 1, MaxValue = 1000, Value = 5 };
        vbox.AddChild(spin);
        var btn = new Button { Text = "PUSH" };
        btn.Pressed += () => {
            _popup!.Visible = false;
            GameManager.Instance.Conn.Reducers.PushCoin(_machineId, (ulong)spin.Value);
        };
        vbox.AddChild(btn);
        panel.AddChild(vbox);
        _popup.AddChild(panel);
        GetTree().Root.AddChild(_popup);
    }

    public override void _ExitTree()
    {
        _instances.Remove(_machineId);
        GameManager.Instance.Conn.Db.CoinPusherState.OnUpdate -= OnStateUpdate;
    }
}
#endif
