using Exiled.API.Enums;
using System.Collections.Generic;
using System.ComponentModel;
using YamlDotNet.Serialization;

namespace Boombox;

public class Playlists : Dictionary<RadioRange, Playlist>
{
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