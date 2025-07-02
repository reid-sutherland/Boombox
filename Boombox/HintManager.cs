using Exiled.API.Features;
using System.ComponentModel;

namespace Boombox;

public class HintManager
{
    [Description("Whether hints should be shown with the song title, playlist title, etc.")]
    public bool ShowHints { get; set; } = true;

    [Description("Whether players that don't have an assigned keybind should be given a hint-reminder to set the boombox controls via ServerSpecific Settings, on round start.")]
    public bool ShowSSWarningHints { get; set; } = false;

    [Description("How long to display hints for.")]
    public float HintDuration { get; set; } = 0.75f;

    [Description("The Boombox's CustomItem Name. The name and description are shown to the player as a hint via CustomItem API when they pick up or equip it.")]
    public string BoomboxName { get; set; } = "JBL Speaker";

    [Description("The Boombox's CustomItem Description. See above.")]
    public string BoomboxDescription { get; set; } = "It looks like it's bass boosted!";

    [Description("Hint message when the playlist (Radio Range) is changed. This may conflict slightly with the other hints if all are non-empty.")]
    public string ChangePlaylistHint { get; set; } = "{playlistname}: {songname}";

    [Description("Hint message when the ChangeSong key is pressed. Use {playlistname} and {songname} for formatting.")]
    public string ChangeSongHint { get; set; } = "{playlistname}: {songname}";

    [Description("Hint message when the ShuffleSong key is pressed.")]
    public string ShuffleSongHint { get; set; } = "Shuffled song to {songname}";

    public void ShowChangePlaylist(Player player, Playlist playlist)
    {
        player.ShowHint(
            ChangePlaylistHint
            .Replace("{playlistname}", playlist.Name)
            .Replace("{songname}", playlist.CurrentSong),
            HintDuration);
    }

    public void ShowChangeSong(Player player, Playlist playlist)
    {
        player.ShowHint(
            ChangeSongHint
            .Replace("{playlistname}", playlist.Name)
            .Replace("{songname}", playlist.CurrentSong),
            HintDuration);
    }

    public void ShowShuffleSong(Player player, Playlist playlist)
    {
        player.ShowHint(
            ShuffleSongHint
            .Replace("{playlistname}", playlist.Name)
            .Replace("{songname}", playlist.CurrentSong),
            HintDuration);
    }
}