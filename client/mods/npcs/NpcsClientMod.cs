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

            Vector3 pos;
            if (evt.TargetType == "npc")
            {
                var npc = GameManager.Instance.GetNpc(evt.TargetId);
                if (npc is null) return;
                pos = new Vector3(npc.PosX, npc.PosY, npc.PosZ);
            }
            else if (evt.TargetType == "world_object")
            {
                var obj = GameManager.Instance.GetWorldObject(evt.TargetId);
                if (obj is null) return;
                pos = new Vector3(obj.PosX, obj.PosY, obj.PosZ);
            }
            else
            {
                return; // Player damage — skip
            }

            var effect = DamageNumberEffect.Create(evt.Amount);
            sceneRoot.AddChild(effect);
            effect.GlobalPosition = pos + new Vector3(0, 2.5f, 0);
        };
    }
}
