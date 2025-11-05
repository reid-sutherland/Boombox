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
        string mode = arguments.FirstElement().ToString().ToLower();

        // Check the player's boombox
        Player player = Player.Get(sender);
        if (player is null)
        {
            Log.Debug($"Unknown player tried to do a bb command");
            response = "Player not found.. who are you?";
            return false;
        }

        Log.Debug($"Player '{player.Nickname}' is trying to do a boombox command: {mode}");
        Log.Debug($"- CI check: {MainPlugin.Boombox.Check(player.CurrentItem)}");
        Log.Debug($"- IsBB: {MainPlugin.Boombox.BoomboxStates.IsBoombox(player.CurrentItem.Serial)}");
        Log.Debug($"- currentItem: {player.CurrentItem}");
        foreach (var serial in MainPlugin.Boombox.TrackedSerials)
        {
            Log.Debug($"- found bb serial: {serial} {(player.CurrentItem.Serial == serial ? "MATCH" : "")}");
        }

        if (!MainPlugin.Boombox.Check(player.CurrentItem) && !MainPlugin.Boombox.BoomboxStates.IsBoombox(player.CurrentItem.Serial))
        {
            Log.Debug($"Player '{player.Nickname}' tried to do a bb command but does not appear to be holding a boombox");
            response = "Boombox is not equipped";
            return false;
        }
        Radio radio = (Radio)player.CurrentItem;
        if (radio is null || !radio.IsEnabled)
        {
            Log.Debug($"Player '{player.Nickname}' tried to do a bb command but their boombox is off");
            response = "Boombox is not on";
            return false;
        }

        // Do the action
        ushort itemSerial = player.CurrentItem.Serial;
        Log.Debug($"Player '{player.Nickname}' issued a boombox command for {MainPlugin.Boombox.Identifier(itemSerial)} with arg: {mode}");
        if (mode == "change" || mode == "next")
        {
            string changed = MainPlugin.Boombox.ChangeSong(itemSerial, QueueType.Next, player);
            response = $"Changed song to '{changed}'";
        }
        else if (mode == "shuffle")
        {
            Tuple<Playlist, string> shuffled = MainPlugin.Boombox.ShuffleSong(itemSerial, player);
            response = $"Shuffled song to '{shuffled.Item2}' from playlist '{shuffled.Item1}'";
        }
        else if (mode == "loop")
        {
            LoopMode toggled = MainPlugin.Boombox.SwitchLoopMode(itemSerial, player);
            response = $"Toggled loop mode to '{toggled}'";
        }
        else
        {
            response = $"Invalid option: {mode} - {Usage}";
            return false;
        }

        return true;
    }
}