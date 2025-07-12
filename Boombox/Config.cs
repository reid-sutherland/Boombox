using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.API.Interfaces;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;

namespace Boombox;

public sealed class Config : IConfig
{
    [Description("Whether the Boombox is enabled and should spawn in SCP-914.")]
    public bool IsEnabled { get; set; } = true;

    [Description("Whether debug logs will print to the console.")]
    public bool Debug { get; set; } = false;
    [Description("Enable this if you want to see extra debug logs for server-specific keybinds.")]
    public bool KeybindDebug { get; set; } = false;
    [Description("Whether AudioAPI routines will write logs to debug.")]
    public bool AudioDebug { get; set; } = false;

    [Description("Path to the directory containing .ogg audio files. If left empty or invalid, will default to %APPDATA%/Roaming/EXILED/Audio/Boombox")]
    public string AudioPath { get; set; } = "";

    [Description("Customize ServerSpecificSettings-related configurations here. By default this should not be necessary.")]
    public ServerSettings ServerSettings { get; set; } = new();

    [Description("Configure hints here")]
    public HintManager HintManager { get; set; } = new();

    [Description("Configure the Boombox's properties here")]
    public Boombox Boombox { get; set; } = new();

    [Description("If someone is being naughty then put their SteamID here to block them from Boombox interaction.")]
    public List<string> BannedPlayerIds { get; set; } = new();
    [Description("Hint to be shown to a banned player that tries to pick up the Boombox.")]
    public string BannedMessage { get; set; } = "You are currently banned from using the Boombox :)";

    [Description("Whether the J A R E D warhead easter egg is active. While enabled, once per round, a fake warhead animation will trigger when the easter egg conditions are met, for the memes.")]
    public bool EasterEggEnabled { get; set; } = false;
    [Description("Song name that should trigger the easter egg. Do not include file extension.")]
    public string EasterEggSong { get; set; } = "";
    [Description("SteamID of player that can trigger the easter egg.")]
    public string EasterEggPlayerId { get; set; } = "";
    [Description("How long (in seconds) to delay the animation once the easter egg is activated. This should line up with a sick drop within the song for maximum effect." +
        "Note that if the song is changed before the animation triggers, the sequence will abort and the easter egg can still be re-activated.")]
    public float EasterEggDelay { get; set; } = 0.0f;

    // TODO: Return a Result class and have OnEnabled() throw instead if any fatal errors
    public void Validate()
    {
        // Check audio path
        string defaultPath = Path.Combine(Paths.Exiled, "Audio", "Boombox");
        if (string.IsNullOrEmpty(AudioPath))
        {
            AudioPath = defaultPath;
        }
        else if (!Directory.Exists(AudioPath))
        {
            Log.Warn($"AudioPath does not exist: '{AudioPath}' - using default path: '{defaultPath}'");
            AudioPath = defaultPath;
        }
        if (!Directory.Exists(AudioPath))
        {
            Log.Error($"No valid AudioPath provided and the default AudioPath does not exist: {AudioPath}");
        }

        // Check the count of each range's playlist
        // - if any of these throw, then the plugin will throw anyways so might as well catch it here
        // - the rest of the plugin assumes that each range has a non-null Playlist object (probably bad design)
        int total = 0;
        total += Boombox.Playlists[RadioRange.Short].Length;
        total += Boombox.Playlists[RadioRange.Medium].Length;
        total += Boombox.Playlists[RadioRange.Long].Length;
        total += Boombox.Playlists[RadioRange.Ultra].Length;
        if (total == 0)
        {
            Log.Warn($"Config has no songs in any playlists, so the boombox will not function properly");
        }

        // Check Boombox properties
        if (HintManager is null)
        {
            HintManager = new();
            Log.Warn($"HintManager is null in config. Using default Hint settings.");
        }
        if (Boombox.SpeakerVolume < 0.0 || Boombox.SpeakerVolume > 1.0)
        {
            Boombox.SpeakerVolume = 1.0f;
            Log.Warn($"Config had invalid value for SpeakerVolume: {Boombox.SpeakerVolume} - defaulting to {Boombox.SpeakerVolume}");
        }
        if (Boombox.SpeakerCount < 0 || Boombox.SpeakerCount > 10)
        {
            Boombox.SpeakerCount = 1;
            Log.Warn($"Config had invalid value for SpeakerCount: {Boombox.SpeakerCount} - defaulting to {Boombox.SpeakerCount}");
        }
        if (Boombox.MinDistance < 1.0)
        {
            Boombox.MinDistance = 1.0f;
            Log.Warn($"Config had invalid value for MinDistance: {Boombox.MinDistance} - defaulting to {Boombox.MinDistance}");
        }
        if (Boombox.MaxDistance <= Boombox.MinDistance)
        {
            Boombox.MaxDistance = Boombox.MinDistance + 20.0f;
            Log.Warn($"Config had invalid value for MaxDistance: {Boombox.MaxDistance} - defaulting to {Boombox.MaxDistance}");
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
            else if (!File.Exists(Path.Combine(AudioPath, EasterEggSong + ".ogg")))
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
        if (EasterEggEnabled)
        {
            Log.Debug($"Easter egg is enabled :)");
        }
    }
}