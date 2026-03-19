// client/mods/interactables/content/InteractablesContent.cs
using Godot;

namespace SandboxRPG;

public static class InteractablesContent
{
    public static void RegisterAll()
    {
        RegisterItems();
        RegisterStructures();
    }

    private static void RegisterItems()
    {
        ItemRegistry.Register("raw_iron", new ItemDef
        {
            DisplayName = "Raw Iron",
            TintColor = new Color(0.5f, 0.4f, 0.35f),
            MaxStack = 50,
        });
    }

    private static void RegisterStructures()
    {
        StructureRegistry.Register("furnace", new StructureDef
        {
            DisplayName = "Furnace",
            ModelPath = "res://assets/models/survival/barrel.glb",
            TintColor = new Color(0.7f, 0.3f, 0.2f),
            Scale = 1.2f,
            CollisionSize = new Vector3(1.0f, 1.0f, 1.0f),
            CollisionCenter = new Vector3(0, 0.5f, 0),
            YOffset = 0.5f,
        });
        StructureRegistry.Register("crafting_table", new StructureDef
        {
            DisplayName = "Crafting Table",
            ModelPath = "res://assets/models/survival/workbench.glb",
            TintColor = new Color(0.9f, 0.75f, 0.5f),
            CollisionSize = new Vector3(1.2f, 0.8f, 0.6f),
            CollisionCenter = new Vector3(0, 0.4f, 0),
            YOffset = 0.4f,
        });
        StructureRegistry.Register("sign", new StructureDef
        {
            DisplayName = "Sign",
            ModelPath = "res://assets/models/survival/resource-planks.glb",
            TintColor = new Color(0.85f, 0.75f, 0.55f),
            Scale = 0.8f,
            CollisionSize = new Vector3(0.6f, 1.0f, 0.1f),
            CollisionCenter = new Vector3(0, 0.5f, 0),
            YOffset = 0.5f,
        });
    }
}
