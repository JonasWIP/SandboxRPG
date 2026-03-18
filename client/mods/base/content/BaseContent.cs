// client/mods/base/content/BaseContent.cs
using Godot;

namespace SandboxRPG;

/// <summary>
/// Registers all base-game content definitions into the registries.
/// Called from BaseClientMod.Initialize().
/// Add .tres files to mods/base/content/ subdirectories for visual editor editing;
/// until then, registration happens here in code.
/// </summary>
public static class BaseContent
{
    public static void RegisterAll()
    {
        RegisterItems();
        RegisterStructures();
        RegisterObjects();
    }

    private static void RegisterItems()
    {
        ItemRegistry.Register("wood",          new ItemDef { ModelPath = "res://assets/models/survival/resource-wood.glb",  DisplayName = "Wood",          MaxStack = 50 });
        ItemRegistry.Register("stone",         new ItemDef { ModelPath = "res://assets/models/survival/resource-stone.glb", DisplayName = "Stone",         MaxStack = 50 });
        ItemRegistry.Register("iron",          new ItemDef { DisplayName = "Iron",          TintColor = new Color(0.7f, 0.7f, 0.75f), MaxStack = 50 });
        ItemRegistry.Register("wood_pickaxe",  new ItemDef { DisplayName = "Wood Pickaxe",  MaxStack = 1 });
        ItemRegistry.Register("wood_axe",      new ItemDef { DisplayName = "Wood Axe",      MaxStack = 1 });
        ItemRegistry.Register("stone_pickaxe", new ItemDef { DisplayName = "Stone Pickaxe", MaxStack = 1 });
        ItemRegistry.Register("iron_pickaxe",  new ItemDef { DisplayName = "Iron Pickaxe",  MaxStack = 1 });
        ItemRegistry.Register("furnace",        new ItemDef { DisplayName = "Furnace",        MaxStack = 1 });
        ItemRegistry.Register("crafting_table", new ItemDef { DisplayName = "Crafting Table", MaxStack = 1 });
        ItemRegistry.Register("sign",           new ItemDef { DisplayName = "Sign",           MaxStack = 10 });
    }

    private static void RegisterStructures()
    {
        // Tints from original StructureSpawner.CreateStructureVisual
        var woodTint  = new Color(1.0f, 0.78f, 0.55f);
        var stoneTint = new Color(0.82f, 0.82f, 0.88f);

        // Collision sizes + centers from original GetBoxShape — must match exactly
        StructureRegistry.Register("wood_wall",   new StructureDef { ModelPath = "res://assets/models/building/wall.glb",               TintColor = woodTint,  CollisionSize = new Vector3(0.25f, 2.4f, 2.0f), CollisionCenter = new Vector3(0, 1.2f,  0), YOffset = 1.25f });
        StructureRegistry.Register("stone_wall",  new StructureDef { ModelPath = "res://assets/models/building/wall.glb",               TintColor = stoneTint, CollisionSize = new Vector3(0.25f, 2.4f, 2.0f), CollisionCenter = new Vector3(0, 1.2f,  0), YOffset = 1.25f });
        StructureRegistry.Register("wood_floor",  new StructureDef { ModelPath = "res://assets/models/building/floor.glb",              TintColor = woodTint,  CollisionSize = new Vector3(2.0f,  0.1f, 2.0f), CollisionCenter = new Vector3(0, 0.05f, 0), YOffset = 0.05f });
        StructureRegistry.Register("stone_floor", new StructureDef { ModelPath = "res://assets/models/building/floor.glb",              TintColor = stoneTint, CollisionSize = new Vector3(2.0f,  0.1f, 2.0f), CollisionCenter = new Vector3(0, 0.05f, 0), YOffset = 0.05f });
        StructureRegistry.Register("wood_door",   new StructureDef { ModelPath = "res://assets/models/building/wall-doorway-square.glb", TintColor = woodTint, CollisionSize = new Vector3(0.25f, 2.4f, 2.0f), CollisionCenter = new Vector3(0, 1.2f,  0), YOffset = 1.1f  });
        StructureRegistry.Register("campfire",    new StructureDef { ModelPath = "res://assets/models/survival/campfire-pit.glb",  CollisionSize = new Vector3(0.8f, 0.4f, 0.8f), CollisionCenter = new Vector3(0, 0.2f, 0), YOffset = 0.15f });
        StructureRegistry.Register("workbench",   new StructureDef { ModelPath = "res://assets/models/survival/workbench.glb",    CollisionSize = new Vector3(1.2f, 0.8f, 0.6f), CollisionCenter = new Vector3(0, 0.4f, 0), YOffset = 0.4f  });
        StructureRegistry.Register("chest",       new StructureDef { ModelPath = "res://assets/models/survival/chest.glb",        CollisionSize = new Vector3(0.8f, 0.6f, 0.6f), CollisionCenter = new Vector3(0, 0.3f, 0), YOffset = 0.3f  });
    }

    private static void RegisterObjects()
    {
        // Scales + tints match original WorldObjectSpawner exactly
        var rockTint = new Color(0.6f, 0.6f, 0.6f); // applied via ModelRegistry.ApplyMaterials
        ObjectRegistry.Register("tree_pine",  new ObjectDef { ModelPath = "res://assets/models/nature/tree_pineRoundA.glb",  Scale = 2.5f });
        ObjectRegistry.Register("tree_dead",  new ObjectDef { ModelPath = "res://assets/models/nature/tree_thin_dark.glb",   Scale = 2.0f }); // 2.0 not 2.5
        ObjectRegistry.Register("tree_palm",  new ObjectDef { ModelPath = "res://assets/models/nature/tree_palmTall.glb",    Scale = 2.5f });
        ObjectRegistry.Register("rock_large", new ObjectDef { ModelPath = "res://assets/models/nature/rock_largeA.glb",      Scale = 2.0f, TintColor = rockTint });
        ObjectRegistry.Register("rock_small", new ObjectDef { ModelPath = "res://assets/models/nature/rock_smallA.glb",      Scale = 1.8f, TintColor = rockTint }); // 1.8 not 2.0
        ObjectRegistry.Register("bush",       new ObjectDef { ModelPath = "res://assets/models/nature/plant_bush.glb",       Scale = 1.5f, UseConvexCollision = false });
    }
}
