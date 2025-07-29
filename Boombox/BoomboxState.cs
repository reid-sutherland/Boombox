using Exiled.API.Enums;
using System.Collections.Generic;

namespace Boombox;

/// Notes about Radio items:
/// - If the item was initially spawned as a pickup, then Item.Get(serial) seems to not work.
/// - Getters always return the same data from initialized, and Setters do not seem to stick.
/// - However if the item was spawned as an item (e.g. customitems.give) then the serial approach works for pickups/items.

// This tracks all of the BoomboxStates by item serial
internal class BoomboxStates : Dictionary<ushort, BoomboxState>
{
    public bool IsBoombox(ushort serial) => ContainsKey(serial);

    public string Identifier(ushort serial)
    {
        if (TryGetValue(serial, out BoomboxState state))
        {
            return state.Identifier;
        }
        return $"Untracked({serial})";
    }

    public AudioPlayer GetAudioPlayer(ushort serial) => TryGetValue(serial, out BoomboxState state) ? state.AudioPlayer : null;
}

// This represents all of the individual stateful properties of an individual Boombox
internal class BoomboxState
{
    public ushort Serial { get; set; }
    public Playlists Playlists { get; set; }
    public RadioRange Range { get; set; }
    public LoopMode LoopMode { get; set; }
    public AudioPlayer AudioPlayer { get; set; }
    public AudioClipPlayback CurrentPlayback { get; set; }

    public string Identifier => $"{nameof(Boombox)}({Serial})";

    public string AudioPlayerName => $"{Identifier}-AP";

    public Playlist CurrentPlaylist => Playlists[Range];

    // These parameters are required for construction, others can be defaulted
    public BoomboxState(ushort serial, Playlists playlists)
    {
        Serial = serial;
        Playlists = new Playlists(playlists);   // copy-construct the playlists from config

        Range = RadioRange.Short;
        LoopMode = LoopMode.None;
        AudioPlayer = null;
        CurrentPlayback = null;
    }

    public void StopCurrentPlayback()
    {
        if (AudioPlayer is not null && CurrentPlayback is not null)
        {
            AudioPlayer.RemoveClipByName(CurrentPlayback.Clip);
        }
    }

    public bool StartNewPlayback(string song)
    {
        if (AudioPlayer is not null)
        {
            CurrentPlayback = AudioPlayer.AddClip(song, loop: LoopMode == LoopMode.RepeatSong);
            return true;
        }
        return false;
    }
}