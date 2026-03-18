using Godot;
using System.Collections.Generic;
using SandboxRPG;

public partial class InventoryCraftingPanel : InteractionPanel
{
    private VBoxContainer _recipeList = null!;

    protected override string PanelTitle => "Inventory & Crafting";

    public string Station { get; set; } = "";

    protected override Control BuildContextSide()
    {
        var rightCol = UIFactory.MakeVBox(8);

        rightCol.AddChild(UIFactory.MakeLabel("Crafting", 14, UIFactory.ColourAccent));

        var craftScroll = new ScrollContainer();
        craftScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        craftScroll.CustomMinimumSize = new Vector2(0, 400);
        rightCol.AddChild(craftScroll);

        _recipeList = UIFactory.MakeVBox(6);
        craftScroll.AddChild(_recipeList);

        return rightCol;
    }

    protected override void RefreshContextSide()
    {
        RefreshRecipes();
    }

    private void RefreshRecipes()
    {
        foreach (Node child in _recipeList.GetChildren())
            child.QueueFree();

        var have = new Dictionary<string, uint>();
        foreach (var item in GameManager.Instance.GetMyInventory())
        {
            have.TryGetValue(item.ItemType, out uint cur);
            have[item.ItemType] = cur + item.Quantity;
        }

        foreach (var recipe in GameManager.Instance.GetAllRecipes())
        {
            bool stationLocked = !string.IsNullOrEmpty(recipe.Station) && recipe.Station != Station;

            var row = UIFactory.MakeHBox(8);
            row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _recipeList.AddChild(row);

            var infoCol = UIFactory.MakeVBox(2);
            infoCol.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(infoCol);

            infoCol.AddChild(UIFactory.MakeLabel(
                $"{recipe.ResultItemType.Replace('_', ' ')} \u00d7{recipe.ResultQuantity}", 13));

            var ingredientStr = FormatIngredients(recipe.Ingredients);
            infoCol.AddChild(UIFactory.MakeLabel(ingredientStr, 10, UIFactory.ColourMuted));

            if (stationLocked)
            {
                infoCol.AddChild(UIFactory.MakeLabel(
                    $"Requires: {recipe.Station.Replace('_', ' ')}", 10, UIFactory.ColourDanger));
            }

            bool canCraft = !stationLocked && CanCraft(recipe.Ingredients, have);

            var craftBtn = UIFactory.MakeButton("Craft", 13, new Vector2(70, 34));
            craftBtn.Disabled = !canCraft;
            if (!canCraft)
                craftBtn.Modulate = new Color(1, 1, 1, 0.4f);

            ulong recipeId = recipe.Id;
            string station = Station;
            craftBtn.Pressed += () => GameManager.Instance.CraftRecipe(recipeId, station);
            row.AddChild(craftBtn);

            _recipeList.AddChild(UIFactory.MakeSeparator());
        }
    }

    private static string FormatIngredients(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var parts = new List<string>();
        foreach (var part in raw.Split(','))
        {
            var kv = part.Trim().Split(':');
            if (kv.Length == 2)
                parts.Add($"{kv[0].Trim().Replace('_', ' ')} \u00d7{kv[1].Trim()}");
        }
        return string.Join("  ", parts);
    }

    private static bool CanCraft(string ingredients, Dictionary<string, uint> have)
    {
        foreach (var part in ingredients.Split(','))
        {
            var kv = part.Trim().Split(':');
            if (kv.Length != 2 || !uint.TryParse(kv[1], out uint need)) continue;
            string type = kv[0].Trim();
            have.TryGetValue(type, out uint owned);
            if (owned < need) return false;
        }
        return true;
    }
}
