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
}
