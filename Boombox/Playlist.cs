using Exiled.API.Enums;
using System.Collections.Generic;
using System.ComponentModel;
using YamlDotNet.Serialization;

namespace Boombox;

public enum QueueType
{
    Current = 0,
    Next,
    Last,
}

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
}

public class Playlist
{
    [Description("The name of the playlist.")]
    public string Name { get; set; } = "";

    [Description("A list of audio files to put in the playlist.")]
    public List<string> Songs { get; set; } = new();

    [YamlIgnore]
    public int SongIndex { get; set; } = 0;

    [YamlIgnore]
    public int Length => Songs.Count;

    [YamlIgnore]
    public string CurrentSong => Songs[SongIndex];

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