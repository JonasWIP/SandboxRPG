using System;
using System.Collections.Generic;
using SpacetimeDB;

namespace SandboxRPG.Server;

/// <summary>
/// Sandbox RPG - SpacetimeDB Server Module
/// All game logic runs here. Clients only display state and send actions.
/// </summary>
public static partial class Module
{
    // =========================================================================
    // TABLES
    // =========================================================================

    /// <summary>Player data - synced to all clients</summary>
    [Table(Name = "player", Public = true)]
    public partial struct Player
    {
        [PrimaryKey]
        public Identity Identity;
        public string Name;
        public float PosX;
        public float PosY;
        public float PosZ;
        public float RotY; // Y-axis rotation in radians
        public float Health;
        public float MaxHealth;
        public float Stamina;
        public float MaxStamina;
        public bool IsOnline;
    }

    /// <summary>Inventory items belonging to players</summary>
    [Table(Name = "inventory_item", Public = true)]
    public partial struct InventoryItem
    {
        [AutoInc]
        [PrimaryKey]
        public ulong Id;
        public Identity OwnerId;
        public string ItemType;
        public uint Quantity;
        public int Slot; // -1 = not in hotbar, 0-8 = hotbar slot
    }

    /// <summary>Items lying in the world (drops, resources)</summary>
    [Table(Name = "world_item", Public = true)]
    public partial struct WorldItem
    {
        [AutoInc]
        [PrimaryKey]
        public ulong Id;
        public string ItemType;
        public uint Quantity;
        public float PosX;
        public float PosY;
        public float PosZ;
    }

    /// <summary>Structures placed by players (buildings, furniture)</summary>
    [Table(Name = "placed_structure", Public = true)]
    public partial struct PlacedStructure
    {
        [AutoInc]
        [PrimaryKey]
        public ulong Id;
        public Identity OwnerId;
        public string StructureType;
        public float PosX;
        public float PosY;
        public float PosZ;
        public float RotY;
        public float Health;
        public float MaxHealth;
    }

    /// <summary>Crafting recipes - server-authoritative recipe definitions</summary>
    [Table(Name = "crafting_recipe", Public = true)]
    public partial struct CraftingRecipe
    {
        [AutoInc]
        [PrimaryKey]
        public ulong Id;
        public string ResultItemType;
        public uint ResultQuantity;
        // Ingredients stored as "item:qty,item:qty" format
        public string Ingredients;
        public float CraftTimeSeconds;
    }

    /// <summary>Chat messages - event table for real-time chat</summary>
    [Table(Name = "chat_message", Public = true)]
    public partial struct ChatMessage
    {
        [AutoInc]
        [PrimaryKey]
        public ulong Id;
        public Identity SenderId;
        public string SenderName;
        public string Text;
        public ulong Timestamp;
    }

    // =========================================================================
    // LIFECYCLE REDUCERS
    // =========================================================================

    [Reducer(ReducerKind.Init)]
    public static void Init(ReducerContext ctx)
    {
        Log.Info("SandboxRPG server module initialized!");

        // Seed default crafting recipes
        SeedRecipes(ctx);
    }

    [Reducer(ReducerKind.ClientConnected)]
    public static void ClientConnected(ReducerContext ctx)
    {
        var identity = ctx.Sender;
        var existing = ctx.Db.Player.Identity.Find(identity);

        if (existing is not null)
        {
            // Player reconnecting - mark as online
            var player = existing.Value;
            player.IsOnline = true;
            ctx.Db.Player.Identity.Update(player);
            Log.Info($"Player '{player.Name}' reconnected.");
        }
        else
        {
            // New player - create with default values
            ctx.Db.Player.Insert(new Player
            {
                Identity = identity,
                Name = $"Player_{identity.ToString()[..8]}",
                PosX = 0f,
                PosY = 1f,
                PosZ = 0f,
                RotY = 0f,
                Health = 100f,
                MaxHealth = 100f,
                Stamina = 100f,
                MaxStamina = 100f,
                IsOnline = true,
            });

            // Give starter items
            ctx.Db.InventoryItem.Insert(new InventoryItem
            {
                OwnerId = identity,
                ItemType = "wood_pickaxe",
                Quantity = 1,
                Slot = 0,
            });
            ctx.Db.InventoryItem.Insert(new InventoryItem
            {
                OwnerId = identity,
                ItemType = "wood_axe",
                Quantity = 1,
                Slot = 1,
            });

            Log.Info($"New player created: {identity}");
        }
    }

    [Reducer(ReducerKind.ClientDisconnected)]
    public static void ClientDisconnected(ReducerContext ctx)
    {
        var existing = ctx.Db.Player.Identity.Find(ctx.Sender);
        if (existing is not null)
        {
            var player = existing.Value;
            player.IsOnline = false;
            ctx.Db.Player.Identity.Update(player);
            Log.Info($"Player '{player.Name}' disconnected.");
        }
    }

    // =========================================================================
    // PLAYER REDUCERS
    // =========================================================================

    [Reducer]
    public static void SetName(ReducerContext ctx, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 32)
        {
            throw new Exception("Name must be between 1 and 32 characters.");
        }

        var existing = ctx.Db.Player.Identity.Find(ctx.Sender);
        if (existing is not null)
        {
            var player = existing.Value;
            player.Name = name;
            ctx.Db.Player.Identity.Update(player);
        }
    }

    [Reducer]
    public static void MovePlayer(ReducerContext ctx, float posX, float posY, float posZ, float rotY)
    {
        var existing = ctx.Db.Player.Identity.Find(ctx.Sender);
        if (existing is null) return;

        var player = existing.Value;

        // Basic server validation - prevent teleporting
        float dx = posX - player.PosX;
        float dy = posY - player.PosY;
        float dz = posZ - player.PosZ;
        float distSq = dx * dx + dy * dy + dz * dz;

        // Allow max ~20 units movement per tick (generous for now)
        if (distSq > 400f)
        {
            Log.Warn($"Player {player.Name} tried to teleport! Rejected.");
            return;
        }

        player.PosX = posX;
        player.PosY = posY;
        player.PosZ = posZ;
        player.RotY = rotY;
        ctx.Db.Player.Identity.Update(player);
    }

    // =========================================================================
    // CHAT REDUCER
    // =========================================================================

    [Reducer]
    public static void SendChat(ReducerContext ctx, string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 256) return;

        var player = ctx.Db.Player.Identity.Find(ctx.Sender);
        string senderName = player?.Name ?? "Unknown";

        ctx.Db.ChatMessage.Insert(new ChatMessage
        {
            SenderId = ctx.Sender,
            SenderName = senderName,
            Text = text,
            Timestamp = (ulong)((DateTimeOffset)ctx.Timestamp).ToUnixTimeMilliseconds() * 1000,
        });
    }

    // =========================================================================
    // INVENTORY REDUCERS
    // =========================================================================

    [Reducer]
    public static void PickupItem(ReducerContext ctx, ulong worldItemId)
    {
        var worldItem = ctx.Db.WorldItem.Id.Find(worldItemId);
        if (worldItem is null) return;

        var item = worldItem.Value;
        var identity = ctx.Sender;

        // Check distance to player
        var player = ctx.Db.Player.Identity.Find(identity);
        if (player is null) return;

        float dx = item.PosX - player.Value.PosX;
        float dz = item.PosZ - player.Value.PosZ;
        if (dx * dx + dz * dz > 25f) // max 5 units pickup range
        {
            throw new Exception("Too far away to pick up.");
        }

        // Try to stack with existing inventory
        bool stacked = false;
        foreach (var invItem in ctx.Db.InventoryItem.Iter())
        {
            if (invItem.OwnerId == identity && invItem.ItemType == item.ItemType)
            {
                var updated = invItem;
                updated.Quantity += item.Quantity;
                ctx.Db.InventoryItem.Id.Update(updated);
                stacked = true;
                break;
            }
        }

        if (!stacked)
        {
            ctx.Db.InventoryItem.Insert(new InventoryItem
            {
                OwnerId = identity,
                ItemType = item.ItemType,
                Quantity = item.Quantity,
                Slot = -1,
            });
        }

        // Remove from world
        ctx.Db.WorldItem.Delete(item);
    }

    [Reducer]
    public static void DropItem(ReducerContext ctx, ulong inventoryItemId, uint quantity)
    {
        var invItem = ctx.Db.InventoryItem.Id.Find(inventoryItemId);
        if (invItem is null) return;

        var item = invItem.Value;
        if (item.OwnerId != ctx.Sender) return;
        if (quantity == 0 || quantity > item.Quantity) return;

        var player = ctx.Db.Player.Identity.Find(ctx.Sender);
        if (player is null) return;

        // Spawn in world near player
        ctx.Db.WorldItem.Insert(new WorldItem
        {
            ItemType = item.ItemType,
            Quantity = quantity,
            PosX = player.Value.PosX + 1f,
            PosY = player.Value.PosY,
            PosZ = player.Value.PosZ + 1f,
        });

        // Update or remove from inventory
        if (quantity >= item.Quantity)
        {
            ctx.Db.InventoryItem.Delete(item);
        }
        else
        {
            item.Quantity -= quantity;
            ctx.Db.InventoryItem.Id.Update(item);
        }
    }

    [Reducer]
    public static void MoveItemToSlot(ReducerContext ctx, ulong inventoryItemId, int slot)
    {
        if (slot < -1 || slot > 8) return;

        var invItem = ctx.Db.InventoryItem.Id.Find(inventoryItemId);
        if (invItem is null) return;

        var item = invItem.Value;
        if (item.OwnerId != ctx.Sender) return;

        item.Slot = slot;
        ctx.Db.InventoryItem.Id.Update(item);
    }

    // =========================================================================
    // CRAFTING REDUCERS
    // =========================================================================

    [Reducer]
    public static void CraftItem(ReducerContext ctx, ulong recipeId)
    {
        var recipe = ctx.Db.CraftingRecipe.Id.Find(recipeId);
        if (recipe is null)
        {
            throw new Exception("Recipe not found.");
        }

        var r = recipe.Value;
        var identity = ctx.Sender;

        // Parse ingredients "wood:4,stone:2"
        var ingredients = ParseIngredients(r.Ingredients);

        // Check if player has all ingredients
        foreach (var (itemType, needed) in ingredients)
        {
            uint have = 0;
            foreach (var invItem in ctx.Db.InventoryItem.Iter())
            {
                if (invItem.OwnerId == identity && invItem.ItemType == itemType)
                {
                    have += invItem.Quantity;
                }
            }
            if (have < needed)
            {
                throw new Exception($"Not enough {itemType}. Need {needed}, have {have}.");
            }
        }

        // Consume ingredients
        foreach (var (itemType, needed) in ingredients)
        {
            uint remaining = needed;
            foreach (var invItem in ctx.Db.InventoryItem.Iter())
            {
                if (remaining == 0) break;
                if (invItem.OwnerId == identity && invItem.ItemType == itemType)
                {
                    if (invItem.Quantity <= remaining)
                    {
                        remaining -= invItem.Quantity;
                        ctx.Db.InventoryItem.Delete(invItem);
                    }
                    else
                    {
                        var updated = invItem;
                        updated.Quantity -= remaining;
                        ctx.Db.InventoryItem.Id.Update(updated);
                        remaining = 0;
                    }
                }
            }
        }

        // Give crafted item
        ctx.Db.InventoryItem.Insert(new InventoryItem
        {
            OwnerId = identity,
            ItemType = r.ResultItemType,
            Quantity = r.ResultQuantity,
            Slot = -1,
        });

        Log.Info($"Player crafted {r.ResultQuantity}x {r.ResultItemType}");
    }

    // =========================================================================
    // BUILDING REDUCERS
    // =========================================================================

    [Reducer]
    public static void PlaceStructure(ReducerContext ctx, string structureType, float posX, float posY, float posZ, float rotY)
    {
        var identity = ctx.Sender;

        // Check player has the item
        InventoryItem? foundItem = null;
        foreach (var invItem in ctx.Db.InventoryItem.Iter())
        {
            if (invItem.OwnerId == identity && invItem.ItemType == structureType)
            {
                foundItem = invItem;
                break;
            }
        }

        if (foundItem is null)
        {
            throw new Exception($"You don't have a {structureType} to place.");
        }

        // Determine structure health based on type
        float maxHealth = structureType switch
        {
            "wood_wall" => 100f,
            "stone_wall" => 250f,
            "wood_floor" => 80f,
            "stone_floor" => 200f,
            "wood_door" => 60f,
            "campfire" => 50f,
            "workbench" => 100f,
            "chest" => 80f,
            _ => 100f,
        };

        // Place the structure
        ctx.Db.PlacedStructure.Insert(new PlacedStructure
        {
            OwnerId = identity,
            StructureType = structureType,
            PosX = posX,
            PosY = posY,
            PosZ = posZ,
            RotY = rotY,
            Health = maxHealth,
            MaxHealth = maxHealth,
        });

        // Consume the item
        var item = foundItem.Value;
        if (item.Quantity <= 1)
        {
            ctx.Db.InventoryItem.Delete(item);
        }
        else
        {
            item.Quantity -= 1;
            ctx.Db.InventoryItem.Id.Update(item);
        }

        Log.Info($"Player placed {structureType} at ({posX}, {posY}, {posZ})");
    }

    [Reducer]
    public static void RemoveStructure(ReducerContext ctx, ulong structureId)
    {
        var structure = ctx.Db.PlacedStructure.Id.Find(structureId);
        if (structure is null) return;

        var s = structure.Value;

        // Only owner can remove
        if (s.OwnerId != ctx.Sender)
        {
            throw new Exception("You can only remove your own structures.");
        }

        // Give back the item
        ctx.Db.InventoryItem.Insert(new InventoryItem
        {
            OwnerId = ctx.Sender,
            ItemType = s.StructureType,
            Quantity = 1,
            Slot = -1,
        });

        ctx.Db.PlacedStructure.Delete(s);
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    private static List<(string itemType, uint quantity)> ParseIngredients(string ingredients)
    {
        var result = new List<(string, uint)>();
        if (string.IsNullOrEmpty(ingredients)) return result;

        foreach (var part in ingredients.Split(','))
        {
            var kv = part.Trim().Split(':');
            if (kv.Length == 2 && uint.TryParse(kv[1], out uint qty))
            {
                result.Add((kv[0].Trim(), qty));
            }
        }
        return result;
    }

    private static void SeedRecipes(ReducerContext ctx)
    {
        // Basic recipes
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe
        {
            ResultItemType = "wood_wall",
            ResultQuantity = 1,
            Ingredients = "wood:4",
            CraftTimeSeconds = 2f,
        });
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe
        {
            ResultItemType = "stone_wall",
            ResultQuantity = 1,
            Ingredients = "stone:6",
            CraftTimeSeconds = 3f,
        });
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe
        {
            ResultItemType = "wood_floor",
            ResultQuantity = 1,
            Ingredients = "wood:3",
            CraftTimeSeconds = 1.5f,
        });
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe
        {
            ResultItemType = "wood_door",
            ResultQuantity = 1,
            Ingredients = "wood:3,iron:1",
            CraftTimeSeconds = 2f,
        });
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe
        {
            ResultItemType = "campfire",
            ResultQuantity = 1,
            Ingredients = "wood:5,stone:3",
            CraftTimeSeconds = 3f,
        });
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe
        {
            ResultItemType = "workbench",
            ResultQuantity = 1,
            Ingredients = "wood:8,stone:4",
            CraftTimeSeconds = 5f,
        });
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe
        {
            ResultItemType = "chest",
            ResultQuantity = 1,
            Ingredients = "wood:6,iron:2",
            CraftTimeSeconds = 4f,
        });
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe
        {
            ResultItemType = "stone_pickaxe",
            ResultQuantity = 1,
            Ingredients = "wood:2,stone:3",
            CraftTimeSeconds = 2f,
        });
        ctx.Db.CraftingRecipe.Insert(new CraftingRecipe
        {
            ResultItemType = "iron_pickaxe",
            ResultQuantity = 1,
            Ingredients = "wood:2,iron:3",
            CraftTimeSeconds = 3f,
        });

        // Spawn some starter world items
        ctx.Db.WorldItem.Insert(new WorldItem { ItemType = "wood", Quantity = 5, PosX = 3f, PosY = 0.5f, PosZ = 3f });
        ctx.Db.WorldItem.Insert(new WorldItem { ItemType = "stone", Quantity = 3, PosX = -4f, PosY = 0.5f, PosZ = 2f });
        ctx.Db.WorldItem.Insert(new WorldItem { ItemType = "wood", Quantity = 8, PosX = 7f, PosY = 0.5f, PosZ = -5f });
        ctx.Db.WorldItem.Insert(new WorldItem { ItemType = "iron", Quantity = 2, PosX = -8f, PosY = 0.5f, PosZ = -6f });
        ctx.Db.WorldItem.Insert(new WorldItem { ItemType = "stone", Quantity = 5, PosX = 10f, PosY = 0.5f, PosZ = 8f });

        Log.Info("Seeded crafting recipes and starter world items.");
    }
}
