// client/scripts/ui/HealthBarFactory.cs
using Godot;

namespace SandboxRPG;

/// <summary>
/// Shared factory for creating a 3D health bar composed of a SubViewport,
/// a ProgressBar, and a billboard Sprite3D. Used by NpcEntity and HarvestableObject.
/// </summary>
public static class HealthBarFactory
{
    /// <summary>
    /// Creates a health bar and returns the three components so callers can cache
    /// the references (avoiding repeated GetNodeOrNull calls).
    /// The caller is responsible for AddChild-ing viewport and sprite to their node.
    /// </summary>
    public static (SubViewport viewport, ProgressBar bar, Sprite3D sprite) Create(
        int width, int height, Color fillColor, float pixelSize,
        float yOffset, double maxValue, double currentValue, bool initiallyVisible,
        float bgAlpha = 0.8f)
    {
        var viewport = new SubViewport
        {
            Size = new Vector2I(width, height),
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };

        var bar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = maxValue,
            Value = currentValue,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(width, height),
            Position = Vector2.Zero,
        };
        var bgStyle = new StyleBoxFlat { BgColor = new Color(0.2f, 0.2f, 0.2f, bgAlpha) };
        var fillStyle = new StyleBoxFlat { BgColor = fillColor };
        bar.AddThemeStyleboxOverride("background", bgStyle);
        bar.AddThemeStyleboxOverride("fill", fillStyle);
        viewport.AddChild(bar);

        var sprite = new Sprite3D
        {
            Name = "HealthBarSprite",
            Texture = viewport.GetTexture(),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            PixelSize = pixelSize,
            Position = new Vector3(0, yOffset, 0),
            Visible = initiallyVisible,
        };

        return (viewport, bar, sprite);
    }
}
