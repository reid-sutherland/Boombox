using Exiled.API.Enums;
using Exiled.API.Interfaces;
using System.Collections.Generic;
using System;
using System.ComponentModel;
using System.IO;

namespace Boombox;

public sealed class Config : IConfig
{
    [Description("Whether the Boombox is enabled and should spawn in SCP-914.")]
    public bool IsEnabled { get; set; } = true;

    public bool Debug { get; set; } = false;

    [Description("Whether AudioAPI routines will write logs to debug.")]
    public bool AudioDebug { get; set; } = false;

    [Description("Whether hints should be shown with the song title, playlist title, etc.")]
    public bool ShowHints { get; set; } = true;

    [Description("Configure the Boombox's properties here")]
    public Boombox Boombox { get; set; } = new();

    [Description("Whether the J A R E D warhead easter egg is active.")]
    public bool EasterEggEnabled { get; set; } = false;

    [Description("Do not include file extension here.")]
    public string EasterEggSong { get; set; } = "";

    public string EasterEggPlayerId { get; set; } = "";

    public float EasterEggDelay { get; set; } = 0.0f;

    [Description("If someone is being naughty then put their SteamID here to block them from Boombox interaction.")]
    public List<string> BannedPlayerIds { get; set; } = new();

    [Description("Hint to be shown to a banned player that tries to pick up the Boombox.")]
    public string BannedMessage { get; set; } = "You are currently banned from using the Boombox :)";

    public void Validate()
    {
        if (Boombox.SpawnProperties.Limit > 1)
        {
            throw new Exception("Boombox has a maximum item limit of 1.");
        }
        if (Boombox.SpeakerVolume <= 0.0 || Boombox.SpeakerVolume > 1.0)
        {
            Boombox.SpeakerVolume = 1.0f;
            Log.Warn($"Config had invalid value for SpeakerVolume: defaulting to {Boombox.SpeakerVolume}");
        }
        if (Boombox.SpeakerCount <= 0 || Boombox.SpeakerCount > 20)
        {
            Boombox.SpeakerCount = 1;
            Log.Warn($"Config had invalid value for SpeakerCount: defaulting to {Boombox.SpeakerCount}");
        }
        if (Boombox.MinDistance <= 1.0)
        {
            Boombox.MinDistance = 1.0f;
            Log.Warn($"Config had invalid value for MinDistance: defaulting to {Boombox.MinDistance}");
        }
        if (Boombox.MaxDistance <= Boombox.MinDistance)
        {
            Boombox.MaxDistance = Boombox.MinDistance + 20.0f;
            Log.Warn($"Config had invalid value for MaxDistance: defaulting to {Boombox.MaxDistance}");
        }

        // Try getting each RadioRange value from the Boombox
        // - if any of these throw, then the plugin will throw anyways so might as well throw here
        // - it's okay for one of these to be empty or even null, but the dictionary key must exist
        string testName = Boombox.PlaylistNames[RadioRange.Short];
        testName = Boombox.PlaylistNames[RadioRange.Medium];
        testName = Boombox.PlaylistNames[RadioRange.Long];
        testName = Boombox.PlaylistNames[RadioRange.Ultra];
        // Check the counts as well, if there are no songs then the Boombox cannot function
        int total = 0;
        List<string> playlistTest = Boombox.Playlists[RadioRange.Short];
        total += playlistTest.Count;
        playlistTest = Boombox.Playlists[RadioRange.Medium];
        total += playlistTest.Count;
        playlistTest = Boombox.Playlists[RadioRange.Long];
        total += playlistTest.Count;
        playlistTest = Boombox.Playlists[RadioRange.Ultra];
        total += playlistTest.Count;
        if (total == 0)
        {
            throw new Exception("There are no songs in any of the playlists so the Boombox cannot function");
        }

        // Check easter egg
        if (EasterEggEnabled)
        {
            EasterEggSong = EasterEggSong.Replace(".ogg", "");
            if (string.IsNullOrEmpty(EasterEggSong))
            {
                EasterEggEnabled = false;
                Log.Warn($"Config had EasterEggEnabled but EasterEggSong is invalid: {EasterEggSong}");
            }
            else if (!File.Exists(Path.Combine(MainPlugin.AudioPath, EasterEggSong + ".ogg")))
            {
                EasterEggEnabled = false;
                Log.Warn($"Config had EasterEggEnabled but EasterEggSong does not exist: {EasterEggSong}");
            }
            if (string.IsNullOrEmpty(EasterEggPlayerId) || !EasterEggPlayerId.Contains("@steam"))
            {
                EasterEggEnabled = false;
                Log.Warn($"Config had EasterEggEnabled but EasterEggSteamId is invalid: {EasterEggPlayerId}");
            }
            if (EasterEggDelay < 0.0f)
            {
                EasterEggEnabled = false;
                Log.Warn($"Config had EasterEggEnabled but EasterEggDelay is invalid: {EasterEggDelay}");
            }
        }
    }
}