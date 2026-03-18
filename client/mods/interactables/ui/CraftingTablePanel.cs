// client/mods/interactables/ui/CraftingTablePanel.cs
namespace SandboxRPG;

public partial class CraftingTablePanel : InventoryCraftingPanel
{
    protected override string PanelTitle => "Crafting Table";

    public CraftingTablePanel()
    {
        Station = "crafting_table";
    }
}
