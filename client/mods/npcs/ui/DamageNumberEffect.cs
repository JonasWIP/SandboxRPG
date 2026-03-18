// client/mods/npcs/ui/DamageNumberEffect.cs
using Godot;

namespace SandboxRPG;

public partial class DamageNumberEffect : Label3D
{
    private float _age;
    private Vector3 _velocity;

    public static DamageNumberEffect Create(int amount, Vector3 worldPos)
    {
        var effect = new DamageNumberEffect
        {
            Text = amount.ToString(),
            FontSize = 36,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            GlobalPosition = worldPos + new Vector3(0, 2.5f, 0),
            Modulate = amount > 15 ? Colors.Orange : Colors.White,
        };
        effect._velocity = new Vector3(
            (float)GD.RandRange(-0.5, 0.5),
            2.0f,
            (float)GD.RandRange(-0.5, 0.5)
        );
        return effect;
    }

    public override void _Process(double delta)
    {
        _age += (float)delta;
        GlobalPosition += _velocity * (float)delta;
        _velocity.Y -= 3.0f * (float)delta; // gravity

        // Fade out
        var c = Modulate;
        c.A = Mathf.MoveToward(c.A, 0f, (float)delta * 1.5f);
        Modulate = c;

        if (_age > 1.5f)
            QueueFree();
    }
}
