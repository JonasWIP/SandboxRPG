using Godot;

namespace SandboxRPG;

public abstract partial class InteractionPanel : BasePanel
{
    protected InventoryGrid _inventoryGrid = null!;

    public override void OnPushed()
    {
        GameManager.Instance.InventoryChanged += RefreshAll;
        GameManager.Instance.RecipesLoaded += RefreshAll;
        GameManager.Instance.SubscriptionApplied += RefreshAll;
        RefreshAll();
    }

    public override void OnPopped()
    {
        GameManager.Instance.InventoryChanged -= RefreshAll;
        GameManager.Instance.RecipesLoaded -= RefreshAll;
        GameManager.Instance.SubscriptionApplied -= RefreshAll;
    }

    protected override void BuildUI()
    {
        var backdrop = new ColorRect { Color = new Color(0f, 0f, 0f, 0.55f) };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var outerPanel = UIFactory.MakePanel(new Vector2(860, 560));
        center.AddChild(outerPanel);

        var root = UIFactory.MakeVBox(10);
        outerPanel.AddChild(root);

        var titleRow = UIFactory.MakeHBox(16);
        titleRow.Alignment = BoxContainer.AlignmentMode.Center;
        root.AddChild(titleRow);

        titleRow.AddChild(UIFactory.MakeTitle(PanelTitle, 20));

        var closeBtn = UIFactory.MakeButton("\u2715", 14, new Vector2(32, 32));
        closeBtn.Pressed += () => UIManager.Instance.Pop();
        titleRow.AddChild(closeBtn);

        root.AddChild(UIFactory.MakeSeparator());

        var columns = UIFactory.MakeHBox(16);
        columns.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        root.AddChild(columns);

        _inventoryGrid = new InventoryGrid();
        _inventoryGrid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        columns.AddChild(_inventoryGrid);

        var rightSide = BuildContextSide();
        rightSide.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        columns.AddChild(rightSide);
    }

    protected virtual string PanelTitle => "Interaction";
    protected abstract Control BuildContextSide();

    protected virtual void RefreshAll()
    {
        _inventoryGrid?.Refresh();
        RefreshContextSide();
    }

    protected virtual void RefreshContextSide() { }
}
