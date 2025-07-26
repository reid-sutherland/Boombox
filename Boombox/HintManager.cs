﻿using Exiled.API.Features;
using Exiled.CustomItems;
using System.ComponentModel;

namespace Boombox;

public class HintManager
{
    [Description("Whether hints should be shown with the song title, playlist title, etc.")]
    public bool ShowHints { get; set; } = true;

    [Description("Whether players that don't have an assigned keybind should be given a hint-reminder to set the boombox controls via ServerSpecific Settings, on round start.")]
    public bool ShowSSWarningHints { get; set; } = false;

    [Description("The Boombox's Name/Description for CustomItem-related hints. If ShowHints is false or if either value is null/empty then the default CustomItem behavior is used.")]
    public string BoomboxName { get; set; } = "JBL Speaker";
    public string BoomboxDescription { get; set; } = "It looks like it's bass boosted!";

    [Description("How long to display Boombox song hints for.")]
    public float HintDuration { get; set; } = 0.75f;

    [Description("Hint message when the playlist (Radio Range) is changed. Use {playlistname} and {songname} for formatting.")]
    public string ChangePlaylistHint { get; set; } = "{playlistname}: {songname}";
    [Description("Hint message when the ChangeSong key is pressed. Use {playlistname} and {songname} for formatting.")]
    public string ChangeSongHint { get; set; } = "{playlistname}: {songname}";
    [Description("Hint message when the ShuffleSong key is pressed. Use {playlistname} and {songname} for formatting.")]
    public string ShuffleSongHint { get; set; } = "Shuffled song to {songname}";
    [Description("Hint message when the SwitchLoop key is pressed. Use {loopmode} for formatting.")]
    public string SwitchLoopHint { get; set; } = "Switched loop mode to {loopmode}";

    public bool TryShowPickedUpHint(Player player = null)
    {
        if (player is not null)
        {
            if (ShowHints && (!string.IsNullOrEmpty(BoomboxName) || !string.IsNullOrEmpty(BoomboxDescription)))
            {
                player.ShowHint(string.Format(CustomItems.Instance.Config.PickedUpHint.Content, BoomboxName, BoomboxDescription, CustomItems.Instance.Config.PickedUpHint.Duration));
                return true;
            }
        }
        return false;
    }

    public bool TryShowSelectedHint(Player player = null)
    {
        if (player is not null)
        {
            if (ShowHints && (!string.IsNullOrEmpty(BoomboxName) || !string.IsNullOrEmpty(BoomboxDescription)))
            {
                player.ShowHint(string.Format(CustomItems.Instance.Config.SelectedHint.Content, BoomboxName, BoomboxDescription), (int)CustomItems.Instance.Config.SelectedHint.Duration);
                return true;
            }
        }
        return false;
    }

    public void ShowChangePlaylist(Playlist playlist, Player player = null)
    {
        if (player is not null)
        {
            if (ShowHints && !string.IsNullOrEmpty(ChangePlaylistHint))
            {
                player.ShowHint(FormatHint(ChangePlaylistHint, playlist), HintDuration);
            }
        }
    }

    public void ShowChangeSong(Playlist playlist, Player player = null)
    {
        if (player is not null)
        {
            if (ShowHints && !string.IsNullOrEmpty(ChangeSongHint))
            {
                player.ShowHint(FormatHint(ChangeSongHint, playlist), HintDuration);
            }
        }
    }

    public void ShowShuffleSong(Playlist playlist, Player player = null)
    {
        if (player is not null)
        {
            if (ShowHints && !string.IsNullOrEmpty(ShuffleSongHint))
            {
                player.ShowHint(FormatHint(ShuffleSongHint, playlist), HintDuration);
            }
        }
    }

    public void ShowSwitchLoop(LoopMode loopMode, Player player = null)
    {
        if (player is not null)
        {
            if (ShowHints && !string.IsNullOrEmpty(SwitchLoopHint))
            {
                player.ShowHint(SwitchLoopHint.Replace("{loopmode}", loopMode.ToString()));
            }
        }
    }

    private string FormatHint(string hint, Playlist playlist)
    {
        return hint
            .Replace("{playlistname}", playlist.Name)
            .Replace("{songname}", playlist.CurrentSong);
    }
}