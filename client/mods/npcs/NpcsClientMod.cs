// client/mods/npcs/NpcsClientMod.cs
using Godot;

namespace SandboxRPG;

public partial class NpcsClientMod : Node, IClientMod
{
    public string ModName => "npcs";
    public string[] Dependencies => new[] { "base" };

    public override void _Ready() => ModManager.Register(this);

    public void Initialize(Node sceneRoot)
    {
        NpcContent.RegisterAll();
        SetupDamageNumbers(sceneRoot);
        GD.Print("[NpcsClientMod] NPC content registered.");
    }

    private void SetupDamageNumbers(Node sceneRoot)
    {
        GameManager.Instance.DamageEventReceived += (long eventId) =>
        {
            if (!IsInstanceValid(sceneRoot)) return;

            var evt = GameManager.Instance.GetDamageEvent((ulong)eventId);
            if (evt is null) return;

            if (evt.TargetType != "npc") return; // Player damage — skip (player health bars show damage)

            var npc = GameManager.Instance.GetNpc(evt.TargetId);
            if (npc is null) return;

            var pos = new Vector3(npc.PosX, npc.PosY, npc.PosZ);
            var effect = DamageNumberEffect.Create(evt.Amount);
            sceneRoot.AddChild(effect);
            effect.GlobalPosition = pos + new Vector3(0, 2.5f, 0);
        };
    }
}
