using Exiled.API.Enums;
using System.Collections.Generic;
using System.ComponentModel;
using YamlDotNet.Serialization;

namespace Boombox;

public class Playlists : Dictionary<RadioRange, Playlist>
{
    // Default constructor so a generated config will have all keys with empty playlists
    public Playlists()
    {
        Add(RadioRange.Short, new Playlist() { Name = RadioRange.Short.ToString() });
        Add(RadioRange.Medium, new Playlist() { Name = RadioRange.Medium.ToString() });
        Add(RadioRange.Long, new Playlist() { Name = RadioRange.Long.ToString() });
        Add(RadioRange.Ultra, new Playlist() { Name = RadioRange.Ultra.ToString() });
    }

    // Copy-constructor so that Boomboxes don't affect each others' playlists
    public Playlists(Playlists other)
    {
        Add(RadioRange.Short, new Playlist(other[RadioRange.Short]));
        Add(RadioRange.Medium, new Playlist(other[RadioRange.Medium]));
        Add(RadioRange.Long, new Playlist(other[RadioRange.Long]));
        Add(RadioRange.Ultra, new Playlist(other[RadioRange.Ultra]));
    }
}

public class Playlist
{
    [Description("The name of the playlist.")]
    public string Name { get; set; } = "";

    // TODO: Try replacing this with a circular buffer or something
    [Description("A list of audio files to put in the playlist.")]
    public List<string> Songs { get; set; } = new();

    [YamlIgnore]
    public int SongIndex { get; set; } = 0;

    [YamlIgnore]
    public int Length => Songs.Count;

    [YamlIgnore]
    public string CurrentSong => (Length > 0 ? Songs[SongIndex] : "(no songs)").Replace(".ogg", "");

    public Playlist()
    {
    }

    // Copy-constructor - doesn't need to deep copy because the values from 'other' do not change in-game
    public Playlist(Playlist other)
    {
        Name = other.Name;
        Songs = other.Songs;
        SongIndex = 0;
    }

    public void NextSong()
    {
        SongIndex++;
        if (SongIndex >= Length)
        {
            SongIndex = 0;
        }
    }

    public void PreviousSong()
    {
        SongIndex--;
        if (SongIndex < 0)
        {
            SongIndex = Length - 1;
        }
    }
}