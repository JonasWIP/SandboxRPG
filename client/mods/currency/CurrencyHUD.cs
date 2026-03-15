#if MOD_CURRENCY
using Godot;
using SandboxRPG;
using SpacetimeDB.Types;

/// <summary>
/// Small top-right overlay showing Copper / Silver / Gold balance.
/// Added as a child of HUD by ModManager when currency mod is enabled.
/// </summary>
public partial class CurrencyHUD : Control
{
    private Label _label;

    public override void _Ready()
    {
        AnchorRight = 1; AnchorTop = 0;
        OffsetLeft = -220; OffsetRight = -10; OffsetTop = 10; OffsetBottom = 40;

        _label = new Label { HorizontalAlignment = HorizontalAlignment.Right };
        AddChild(_label);

        GameManager.Instance.SubscriptionApplied += Refresh;
        GameManager.Instance.Conn.Db.CurrencyBalance.OnInsert += (_, row) =>
        {
            var p = GameManager.Instance.GetLocalPlayer();
            if (p != null && row.PlayerId == p.Identity) CallDeferred("Refresh");
        };
        GameManager.Instance.Conn.Db.CurrencyBalance.OnUpdate += (_, _, row) =>
        {
            var p = GameManager.Instance.GetLocalPlayer();
            if (p != null && row.PlayerId == p.Identity) CallDeferred("Refresh");
        };
    }

    public void Refresh()
    {
        var player = GameManager.Instance.GetLocalPlayer();
        if (player == null) return;
        var bal = GameManager.Instance.Conn.Db.CurrencyBalance
            .PlayerId.Find(player.Identity);
        if (bal == null) { _label.Text = ""; return; }
        ulong copper = bal.Copper;
        ulong silver = copper / 100;
        ulong gold   = copper / 10000;
        _label.Text = $"Cu {copper % 100}  Ag {silver % 100}  Au {gold}";
    }
}
#endif
