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
            var events = GameManager.Instance.GetRecentDamageEvents();
            foreach (var evt in events)
            {
                if ((long)evt.Id != eventId) continue;

                Vector3 pos;
                if (evt.TargetType == "npc")
                {
                    var npc = GameManager.Instance.GetNpc(evt.TargetId);
                    if (npc is null) break;
                    pos = new Vector3(npc.PosX, npc.PosY, npc.PosZ);
                }
                else
                {
                    // Player damage — skip for now (player health bars show damage)
                    break;
                }

                var effect = DamageNumberEffect.Create(evt.Amount, pos);
                sceneRoot.AddChild(effect);
                break;
            }
        };
    }
}
