using LabApi.Features.Wrappers;
using System.Collections.Generic;
using System.Linq;

namespace Boombox;

// These are here because there's already too much in Boombox.cs
public enum QueueType
{
    Current = 0,
    Next,
    Last,
}

public enum LoopMode
{
    None = 0,
    RepeatSong,
    CyclePlaylist,
    ShuffleAll,
}

public static class Extensions
{
    // Convenience extension to 'cycle' to the next loop mode
    public static LoopMode Next(this LoopMode loopMode)
    {
        return loopMode switch
        {
            LoopMode.None => LoopMode.RepeatSong,
            LoopMode.RepeatSong => LoopMode.CyclePlaylist,
            LoopMode.CyclePlaylist => LoopMode.ShuffleAll,
            LoopMode.ShuffleAll => LoopMode.None,
            _ => LoopMode.None,
        };
    }
}

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