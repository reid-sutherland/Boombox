using LabApi.Features.Wrappers;
using System.Collections.Generic;
using System.Linq;

namespace Boombox;

public static class LabApiExtensions
{
    public static IEnumerable<Item> GetItems(this Player player)
    {
        return player.Inventory.UserInventory.Items.Values.Select(Item.Get);
    }

    public static bool TryGetRadio(this Player player, out RadioItem radioItem)
    {
        radioItem = null;
        Item item = player.GetItems().FirstOrDefault(it => it.Type == ItemType.Radio);
        if (item is not null)
        {
            radioItem = (RadioItem)item;
        }
        return radioItem != null;
    }
}