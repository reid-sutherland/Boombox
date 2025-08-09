using CommandSystem;
using Exiled.API.Features;
using Exiled.API.Features.Items;
using System;

namespace Boombox;

[CommandHandler(typeof(ClientCommandHandler))]
public class Test : ICommand
{
    public string Command { get; } = "boombox";

    public string[] Aliases { get; } = new[] { "bb" };

    public string Description { get; } = "Boombox controls that can be used instead of server-specific keybinds";

    public string Usage { get; set; } = "Usage: `.boombox [change | shuffle | loop]`";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        // Parse arguments
        if (arguments.IsEmpty())
        {
            response = Usage;
            return false;
        }
        string arg = arguments.FirstElement().ToString().ToLower();

        // Check the player's boombox
        Player player = Player.Get(sender);
        if (!MainPlugin.Boombox.Check(player.CurrentItem))
        {
            response = "Boombox is not equipped";
            return false;
        }
        Radio radio = (Radio)player.CurrentItem;
        if (radio is null || !radio.IsEnabled)
        {
            response = "Boombox is not on";
            return false;
        }

        // Do the action
        ushort itemSerial = player.CurrentItem.Serial;
        Log.Debug($"Player '{player.Nickname}' issued a boombox command for {MainPlugin.Boombox.Identifier(itemSerial)} with argument: {arg}");
        if (arg == "change" || arg == "next")
        {
            string changed = MainPlugin.Boombox.ChangeSong(itemSerial, QueueType.Next, player);
            response = $"Changed song to '{changed}'";
        }
        else if (arg == "shuffle")
        {
            Tuple<Playlist, string> shuffled = MainPlugin.Boombox.ShuffleSong(itemSerial, player);
            response = $"Shuffled song to '{shuffled.Item2}' from playlist '{shuffled.Item1}'";
        }
        else if (arg == "loop")
        {
            LoopMode toggled = MainPlugin.Boombox.SwitchLoopMode(itemSerial, player);
            response = $"Toggled loop mode to '{toggled}'";
        }
        else
        {
            response = $"Invalid option: {arg} - {Usage}";
            return false;
        }

        return true;
    }
}