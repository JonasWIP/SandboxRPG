// client/mods/hello-world/ui/HelloWorldPanel.cs
using Godot;
using SpacetimeDB.Types;

namespace SandboxRPG;

/// <summary>
/// Small HUD label showing a greeting from the server.
/// Calls SayHello on connect; updates the label when the server row arrives.
/// </summary>
public partial class HelloWorldPanel : Control
{
    private Label _label = null!;

    public override void _Ready()
    {
        _label = GetNode<Label>("Label");
        _label.Text = "Hello from HelloWorld Mod!";

        var conn = GameManager.Instance.Conn!;
        conn.Db.HelloWorldMessage.OnInsert += OnMessageInsert;
        conn.Db.HelloWorldMessage.OnUpdate += OnMessageUpdate;

        if (GameManager.Instance.IsConnected)
            conn.Reducers.SayHello("Hello from HelloWorld Mod!");
        else
            GameManager.Instance.Connected += OnConnected;
    }

    public override void _ExitTree()
    {
        GameManager.Instance.Connected -= OnConnected;
        if (GameManager.Instance.Conn is { } conn)
        {
            conn.Db.HelloWorldMessage.OnInsert -= OnMessageInsert;
            conn.Db.HelloWorldMessage.OnUpdate -= OnMessageUpdate;
        }
    }

    private void OnConnected() => GameManager.Instance.Conn!.Reducers.SayHello("Hello from HelloWorld Mod!");

    private void OnMessageInsert(EventContext _, HelloWorldMessage row) => OnMessageChanged(row);
    private void OnMessageUpdate(EventContext _, HelloWorldMessage _old, HelloWorldMessage row) => OnMessageChanged(row);

    private void OnMessageChanged(HelloWorldMessage row)
    {
        if (row.PlayerId == GameManager.Instance.LocalIdentity)
            _label.Text = row.Message;
    }
}
