using Godot;
using SpacetimeDB.Types;
using System.Linq;

namespace SandboxRPG;

/// <summary>
/// Crafting panel: shows available recipes, lets player craft items.
/// Opens when interacting with a workbench (or can be toggled with C key for now).
/// </summary>
public partial class CraftingUI : Control
{
    private PanelContainer _panel = null!;
    private VBoxContainer _recipeList = null!;
    private bool _isOpen;

    public override void _Ready()
    {
        _panel = new PanelContainer
        {
            LayoutMode = 1,
            AnchorsPreset = (int)LayoutPreset.Center,
            OffsetLeft = -220,
            OffsetRight = 220,
            OffsetTop = -200,
            OffsetBottom = 200,
            Visible = false,
        };

        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.12f, 0.1f, 0.08f, 0.92f),
            BorderColor = new Color(0.5f, 0.4f, 0.2f),
            BorderWidthBottom = 2,
            BorderWidthTop = 2,
            BorderWidthLeft = 2,
            BorderWidthRight = 2,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 12,
            ContentMarginBottom = 12,
        };
        _panel.AddThemeStyleboxOverride("panel", style);
        AddChild(_panel);

        var mainVbox = new VBoxContainer();
        _panel.AddChild(mainVbox);

        var title = new Label
        {
            Text = "CRAFTING",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeColorOverride("font_color", new Color(1, 0.75f, 0.2f));
        title.AddThemeFontSizeOverride("font_size", 20);
        mainVbox.AddChild(title);

        mainVbox.AddChild(new HSeparator());

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        mainVbox.AddChild(scroll);

        _recipeList = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        scroll.AddChild(_recipeList);

        // Connect
        GameManager.Instance.RecipesLoaded += RefreshRecipes;
        GameManager.Instance.InventoryChanged += RefreshRecipes;
        GameManager.Instance.SubscriptionApplied += RefreshRecipes;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Toggle with C key (temporary - later tied to workbench interaction)
        if (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode == Key.C && !keyEvent.Echo)
        {
            ToggleCrafting();
            GetViewport().SetInputAsHandled();
        }
    }

    private void ToggleCrafting()
    {
        _isOpen = !_isOpen;
        _panel.Visible = _isOpen;

        if (_isOpen)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
            RefreshRecipes();
        }
        else
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    private void RefreshRecipes()
    {
        if (!_isOpen) return;

        foreach (var child in _recipeList.GetChildren())
        {
            child.QueueFree();
        }

        var recipes = GameManager.Instance.GetAllRecipes().ToList();
        var inventory = GameManager.Instance.GetMyInventory().ToList();

        if (recipes.Count == 0)
        {
            var empty = new Label { Text = "No recipes available" };
            empty.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
            _recipeList.AddChild(empty);
            return;
        }

        foreach (var recipe in recipes)
        {
            var container = new VBoxContainer();
            container.AddThemeConstantOverride("separation", 2);

            var row = new HBoxContainer();

            // Result
            var resultLabel = new Label
            {
                Text = $"{FormatName(recipe.ResultItemType)} x{recipe.ResultQuantity}",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            resultLabel.AddThemeColorOverride("font_color", new Color(1, 0.9f, 0.5f));
            resultLabel.AddThemeFontSizeOverride("font_size", 15);
            row.AddChild(resultLabel);

            // Craft button
            bool canCraft = CanCraft(recipe.Ingredients, inventory);
            var craftBtn = new Button
            {
                Text = "Craft",
                CustomMinimumSize = new Vector2(70, 0),
                Disabled = !canCraft,
            };
            var recipeId = recipe.Id;
            craftBtn.Pressed += () =>
            {
                GameManager.Instance.CraftRecipe(recipeId);
            };
            row.AddChild(craftBtn);
            container.AddChild(row);

            // Ingredients
            var ingredientLabel = new Label
            {
                Text = $"  Needs: {FormatIngredients(recipe.Ingredients)}",
            };
            ingredientLabel.AddThemeColorOverride("font_color", canCraft ? new Color(0.5f, 0.8f, 0.5f) : new Color(0.8f, 0.4f, 0.4f));
            ingredientLabel.AddThemeFontSizeOverride("font_size", 12);
            container.AddChild(ingredientLabel);

            container.AddChild(new HSeparator());
            _recipeList.AddChild(container);
        }
    }

    private bool CanCraft(string ingredients, System.Collections.Generic.List<InventoryItem> inventory)
    {
        if (string.IsNullOrEmpty(ingredients)) return true;

        foreach (var part in ingredients.Split(','))
        {
            var kv = part.Trim().Split(':');
            if (kv.Length != 2) continue;

            string itemType = kv[0].Trim();
            if (!uint.TryParse(kv[1], out uint needed)) continue;

            uint have = 0;
            foreach (var inv in inventory)
            {
                if (inv.ItemType == itemType) have += inv.Quantity;
            }
            if (have < needed) return false;
        }
        return true;
    }

    private static string FormatName(string name) => name.Replace("_", " ").ToUpper();

    private static string FormatIngredients(string ingredients)
    {
        if (string.IsNullOrEmpty(ingredients)) return "nothing";
        return string.Join(", ", ingredients.Split(',').Select(part =>
        {
            var kv = part.Trim().Split(':');
            return kv.Length == 2 ? $"{kv[1]}x {kv[0].Replace("_", " ")}" : part;
        }));
    }
}
