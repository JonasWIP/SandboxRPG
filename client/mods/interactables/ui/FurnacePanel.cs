// client/mods/interactables/ui/FurnacePanel.cs
using Godot;

namespace SandboxRPG;

/// <summary>
/// Interaction panel for the furnace structure.
/// Left side: player inventory (from InteractionPanel base).
/// Right side: furnace controls with progress bar, status, and action buttons.
///
/// NOTE: Furnace server-side tables (FurnaceState) and reducers
/// (FurnaceStartSmelt, FurnaceCollect, FurnaceCancelSmelt) are not yet
/// present in the generated bindings — this panel compiles cleanly and
/// will display a "not yet implemented" status until those are added.
/// </summary>
public partial class FurnacePanel : InteractionPanel
{
    private readonly ulong _structureId;

    private ProgressBar _progressBar = null!;
    private Label _statusLabel = null!;
    private Button _smeltBtn = null!;
    private Button _collectBtn = null!;
    private Button _cancelBtn = null!;

    public FurnacePanel(ulong structureId)
    {
        _structureId = structureId;
    }

    protected override string PanelTitle => "Furnace";

    protected override Control BuildContextSide()
    {
        var col = UIFactory.MakeVBox(10);

        col.AddChild(UIFactory.MakeLabel("Furnace", 14, UIFactory.ColourAccent));

        col.AddChild(UIFactory.MakeSeparator());

        // Progress bar
        _progressBar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 1,
            Value = 0,
            CustomMinimumSize = new Vector2(0, 20),
        };
        _progressBar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        col.AddChild(_progressBar);

        // Status label
        _statusLabel = UIFactory.MakeLabel("Idle", 13, UIFactory.ColourMuted);
        col.AddChild(_statusLabel);

        col.AddChild(UIFactory.MakeSeparator());

        // Action buttons
        var btnRow = UIFactory.MakeHBox(8);
        col.AddChild(btnRow);

        _smeltBtn = UIFactory.MakeButton("Smelt", 13, new Vector2(80, 34));
        _smeltBtn.Pressed += OnSmelt;
        btnRow.AddChild(_smeltBtn);

        _collectBtn = UIFactory.MakeButton("Collect", 13, new Vector2(80, 34));
        _collectBtn.Pressed += OnCollect;
        btnRow.AddChild(_collectBtn);

        _cancelBtn = UIFactory.MakeButton("Cancel", 13, new Vector2(80, 34));
        _cancelBtn.Pressed += OnCancel;
        btnRow.AddChild(_cancelBtn);

        return col;
    }

    public override void _Process(double delta)
    {
        UpdateProgress();
    }

    private void UpdateProgress()
    {
        // FurnaceState table not yet generated — show idle until server side is added.
        if (_progressBar == null) return;

        _progressBar.Value = 0;
        _statusLabel.Text = "Idle";
        _smeltBtn.Disabled = false;
        _collectBtn.Disabled = true;
        _cancelBtn.Disabled = true;
    }

    private void OnSmelt()
    {
        // Calls FurnaceStartSmelt reducer once it is added to the server module and
        // client bindings are regenerated.
        // GameManager.Instance.Conn?.Reducers.FurnaceStartSmelt(_structureId);
        GD.Print($"[FurnacePanel] Smelt requested for structure {_structureId} (reducer not yet wired)");
    }

    private void OnCollect()
    {
        // GameManager.Instance.Conn?.Reducers.FurnaceCollect(_structureId);
        GD.Print($"[FurnacePanel] Collect requested for structure {_structureId} (reducer not yet wired)");
    }

    private void OnCancel()
    {
        // GameManager.Instance.Conn?.Reducers.FurnaceCancelSmelt(_structureId);
        GD.Print($"[FurnacePanel] Cancel requested for structure {_structureId} (reducer not yet wired)");
    }
}
