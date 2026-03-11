using Godot;

/// <summary>
/// Centralised scene-transition helper.
/// All ChangeSceneToFile calls go through here — no string literals elsewhere.
/// Registered as an autoload in project.godot.
/// </summary>
public enum GameScene
{
    MainMenu,
    CharacterSetup,
    Game,
}

public partial class SceneRouter : Node
{
    // Singleton access (autoload)
    public static SceneRouter Instance { get; private set; } = null!;

    // =========================================================================
    // SCENE PATHS
    // =========================================================================

    private static readonly System.Collections.Generic.Dictionary<GameScene, string> Paths = new()
    {
        { GameScene.MainMenu,       "res://scenes/MainMenu.tscn"       },
        { GameScene.CharacterSetup, "res://scenes/CharacterSetup.tscn" },
        { GameScene.Game,           "res://scenes/Main.tscn"           },
    };

    // =========================================================================
    // GODOT LIFECYCLE
    // =========================================================================

    public override void _Ready()
    {
        Instance = this;
    }

    // =========================================================================
    // PUBLIC API
    // =========================================================================

    /// <summary>Transition to the given scene. Safe to call from any node.</summary>
    public static void GoTo(GameScene scene)
    {
        if (Instance is null)
        {
            GD.PrintErr("[SceneRouter] Not yet ready.");
            return;
        }

        if (!Paths.TryGetValue(scene, out var path))
        {
            GD.PrintErr($"[SceneRouter] No path registered for {scene}.");
            return;
        }

        GD.Print($"[SceneRouter] → {scene} ({path})");
        Instance.GetTree().ChangeSceneToFile(path);
    }
}
