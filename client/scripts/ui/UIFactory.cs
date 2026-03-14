using Godot;

/// <summary>
/// Static factory for creating consistently-styled UI controls.
/// All UI scripts use these instead of duplicating theme overrides.
/// </summary>
public static class UIFactory
{
    // =========================================================================
    // COLOURS & SIZES
    // =========================================================================

    public static readonly Color ColourAccent    = new Color("#3CB4E5");
    public static readonly Color ColourDanger    = new Color("#E55454");
    public static readonly Color ColourText      = new Color("#EFEFEF");
    public static readonly Color ColourMuted     = new Color("#AAAAAA");
    public static readonly Color ColourPanel     = new Color(0.05f, 0.05f, 0.08f, 0.92f);
    public static readonly Color ColourOverlay   = new Color(0f, 0f, 0f, 0.6f);

    // =========================================================================
    // CONTROLS
    // =========================================================================

    public static Label MakeLabel(string text, int fontSize = 14, Color? colour = null)
    {
        var label = new Label();
        label.Text = text;
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", colour ?? ColourText);
        return label;
    }

    public static Label MakeTitle(string text, int fontSize = 28)
    {
        var label = new Label();
        label.Text = text;
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", ColourAccent);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        return label;
    }

    public static Button MakeButton(string text, int fontSize = 16, Vector2 minSize = default)
    {
        var btn = new Button();
        btn.Text = text;
        btn.AddThemeFontSizeOverride("font_size", fontSize);
        if (minSize != default)
            btn.CustomMinimumSize = minSize;
        return btn;
    }

    public static LineEdit MakeLineEdit(string placeholder = "", int fontSize = 15, float minWidth = 260f)
    {
        var edit = new LineEdit();
        edit.PlaceholderText = placeholder;
        edit.AddThemeFontSizeOverride("font_size", fontSize);
        edit.CustomMinimumSize = new Vector2(minWidth, 0);
        return edit;
    }

    /// <summary>Dark semi-transparent StyleBoxFlat for HUD overlay panels (chat, bars, etc.).</summary>
    public static StyleBoxFlat MakeDarkPanelStyle(float alpha = 0.4f, int cornerRadius = 4, int margin = 8)
    {
        return new StyleBoxFlat
        {
            BgColor = new Color(0f, 0f, 0f, alpha),
            CornerRadiusTopLeft    = cornerRadius, CornerRadiusTopRight    = cornerRadius,
            CornerRadiusBottomLeft = cornerRadius, CornerRadiusBottomRight = cornerRadius,
            ContentMarginLeft = margin, ContentMarginRight  = margin,
            ContentMarginTop  = margin, ContentMarginBottom = margin,
        };
    }

    /// <summary>Full-rect dark semi-transparent background — used as panel backdrop.</summary>
    public static ColorRect MakeDarkBackground(Color? colour = null)
    {
        var rect = new ColorRect();
        rect.Color = colour ?? ColourPanel;
        rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        return rect;
    }

    /// <summary>Full-screen dim overlay (e.g. behind a modal).</summary>
    public static ColorRect MakeOverlay()
    {
        var rect = new ColorRect();
        rect.Color = ColourOverlay;
        rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        rect.MouseFilter = Control.MouseFilterEnum.Stop; // block clicks through
        return rect;
    }

    /// <summary>Centred panel with fixed size.</summary>
    public static PanelContainer MakePanel(Vector2 size)
    {
        var panel = new PanelContainer();
        panel.CustomMinimumSize = size;
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        return panel;
    }

    /// <summary>VBoxContainer with even separation.</summary>
    public static VBoxContainer MakeVBox(int separation = 12)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", separation);
        return box;
    }

    /// <summary>HBoxContainer with even separation.</summary>
    public static HBoxContainer MakeHBox(int separation = 12)
    {
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", separation);
        return box;
    }

    /// <summary>Horizontal separator line.</summary>
    public static HSeparator MakeSeparator()
    {
        var sep = new HSeparator();
        sep.CustomMinimumSize = new Vector2(0, 8);
        return sep;
    }

    /// <summary>
    /// Wraps <paramref name="content"/> in a centred full-rect Control so it
    /// renders on top of all other nodes in a scene.
    /// </summary>
    public static Control MakeFullRectHost(Control content)
    {
        var host = new Control();
        host.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        host.AddChild(content);
        return host;
    }

    // =========================================================================
    // INVENTORY / HOTBAR SLOTS
    // =========================================================================

    public static readonly Color ColourSlotBg       = new Color(0.12f, 0.13f, 0.17f, 0.95f);
    public static readonly Color ColourSlotActive   = new Color("#3CB4E5");
    public static readonly Color ColourSlotBorder   = new Color(0.25f, 0.27f, 0.32f, 1f);
    public static readonly Vector2 SlotSize         = new Vector2(60, 60);

    /// <summary>Square inventory slot button with coloured item indicator and label.</summary>
    public static Button MakeSlotButton(string itemName, uint qty, Color itemColour)
    {
        var btn = new Button();
        btn.CustomMinimumSize = SlotSize;
        btn.ClipContents = true;

        // Item colour square (top-left)
        var dot = new ColorRect
        {
            Color = itemColour,
            CustomMinimumSize = new Vector2(10, 10),
            OffsetLeft = 4, OffsetTop = 4, OffsetRight = 14, OffsetBottom = 14,
        };
        btn.AddChild(dot);

        // Item name
        var nameLbl = new Label
        {
            Text = itemName.Replace('_', ' '),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        nameLbl.AddThemeFontSizeOverride("font_size", 10);
        nameLbl.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        btn.AddChild(nameLbl);

        // Qty badge (bottom-right)
        var qtyLbl = new Label
        {
            Text = qty > 1 ? $"×{qty}" : "",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        qtyLbl.AddThemeFontSizeOverride("font_size", 10);
        qtyLbl.AddThemeColorOverride("font_color", ColourMuted);
        qtyLbl.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        qtyLbl.OffsetLeft = -30; qtyLbl.OffsetTop = -18;
        btn.AddChild(qtyLbl);

        return btn;
    }

    /// <summary>Hotbar slot panel. Pass active=true to highlight with accent border.</summary>
    public static PanelContainer MakeHotbarSlot(bool active = false)
    {
        var panel = new PanelContainer();
        panel.CustomMinimumSize = SlotSize;

        var style = new StyleBoxFlat
        {
            BgColor = ColourSlotBg,
            BorderWidthBottom = 2, BorderWidthTop = 2,
            BorderWidthLeft = 2, BorderWidthRight = 2,
            BorderColor = active ? ColourSlotActive : ColourSlotBorder,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
        };
        panel.AddThemeStyleboxOverride("panel", style);
        return panel;
    }

    /// <summary>
    /// Small context-menu popup at the given viewport position.
    /// Caller adds buttons as children and must call QueueFree() when done.
    /// </summary>
    public static PanelContainer MakeContextMenu(Vector2 screenPos)
    {
        var panel = new PanelContainer();
        panel.Position = screenPos;
        // Adjust so menu doesn't go offscreen (caller can refine)
        return panel;
    }
}
