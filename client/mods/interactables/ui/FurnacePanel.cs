// client/mods/interactables/ui/FurnacePanel.cs
using Godot;

namespace SandboxRPG;

public partial class FurnacePanel : InteractionPanel
{
    private readonly ulong _structureId;
    private ProgressBar _progressBar = null!;
    private Label _statusLabel = null!;

    public FurnacePanel(ulong structureId)
    {
        _structureId = structureId;
    }

    protected override string PanelTitle => "Furnace";

    public override void OnPushed()
    {
        base.OnPushed();
        GameManager.Instance.ContainerSlotChanged += RefreshAll;
    }

    public override void OnPopped()
    {
        GameManager.Instance.ContainerSlotChanged -= RefreshAll;
        base.OnPopped();
    }

    protected override Control BuildContextSide()
    {
        var col = UIFactory.MakeVBox(10);

        col.AddChild(UIFactory.MakeLabel("Furnace", 14, UIFactory.ColourAccent));
        col.AddChild(UIFactory.MakeSeparator());

        _progressBar = new ProgressBar
        {
            MinValue = 0, MaxValue = 100, Value = 0,
            CustomMinimumSize = new Vector2(0, 20),
        };
        _progressBar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        col.AddChild(_progressBar);

        _statusLabel = UIFactory.MakeLabel("Idle", 13, UIFactory.ColourMuted);
        col.AddChild(_statusLabel);

        col.AddChild(UIFactory.MakeSeparator());

        var btnRow = UIFactory.MakeHBox(8);
        col.AddChild(btnRow);

        var smeltBtn = UIFactory.MakeButton("Smelt", 13, new Vector2(80, 34));
        smeltBtn.Pressed += () => GameManager.Instance.Conn?.Reducers.FurnaceStartSmelt(_structureId);
        btnRow.AddChild(smeltBtn);

        var collectBtn = UIFactory.MakeButton("Collect", 13, new Vector2(80, 34));
        collectBtn.Pressed += () => GameManager.Instance.Conn?.Reducers.FurnaceCollect(_structureId);
        btnRow.AddChild(collectBtn);

        var cancelBtn = UIFactory.MakeButton("Cancel", 13, new Vector2(80, 34));
        cancelBtn.Pressed += () => GameManager.Instance.Conn?.Reducers.FurnaceCancelSmelt(_structureId);
        btnRow.AddChild(cancelBtn);

        return col;
    }

    public override void _Process(double delta)
    {
        UpdateProgress();
    }

    private void UpdateProgress()
    {
        if (_progressBar == null || _statusLabel == null) return;
        if (GameManager.Instance.Conn == null) return;

        var state = GameManager.Instance.Conn.Db.FurnaceState.StructureId.Find(_structureId);
        if (state is null)
        {
            _progressBar.Value = 0;
            _statusLabel.Text = "Idle";
            return;
        }

        var now = (ulong)System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var elapsed = now > state.StartTimeMs ? now - state.StartTimeMs : 0;
        var progress = state.DurationMs > 0 ? (double)elapsed / state.DurationMs * 100.0 : 0;
        _progressBar.Value = System.Math.Min(progress, 100);

        if (progress >= 100)
            _statusLabel.Text = "Complete! Click Collect.";
        else
            _statusLabel.Text = $"Smelting... {(int)progress}%";
    }

    protected override void RefreshContextSide() { }
}
