using Godot;

/// <summary>
/// Lightweight local preference store using Godot's ConfigFile.
/// Persists at user://player_prefs.cfg.
/// This caches values for use before the network connection is established.
/// </summary>
public static class PlayerPrefs
{
    private const string FilePath   = "user://player_prefs.cfg";
    private const string Section    = "player";
    private const string KeyName    = "name";
    private const string KeyColor   = "color_hex";

    private static readonly ConfigFile _cfg = new();

    // =========================================================================
    // LOAD
    // =========================================================================

    public static string LoadName()
    {
        _cfg.Load(FilePath);
        return (string)_cfg.GetValue(Section, KeyName, "");
    }

    public static string LoadColorHex()
    {
        _cfg.Load(FilePath);
        return (string)_cfg.GetValue(Section, KeyColor, "#3CB4E5");
    }

    // =========================================================================
    // SAVE
    // =========================================================================

    public static void SaveName(string name)
    {
        _cfg.Load(FilePath);
        _cfg.SetValue(Section, KeyName, name);
        _cfg.Save(FilePath);
    }

    public static void SaveColorHex(string colorHex)
    {
        _cfg.Load(FilePath);
        _cfg.SetValue(Section, KeyColor, colorHex);
        _cfg.Save(FilePath);
    }

    public static void SaveAll(string name, string colorHex)
    {
        _cfg.Load(FilePath);
        _cfg.SetValue(Section, KeyName, name);
        _cfg.SetValue(Section, KeyColor, colorHex);
        _cfg.Save(FilePath);
    }
}
