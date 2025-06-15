using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.CustomItems.API.Features;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UserSettings.ServerSpecific;
using Boombox.Utils;
using Random = System.Random;
using ServerEvents = Exiled.Events.Handlers.Server;

namespace Boombox;

public class MainPlugin : Plugin<Config>
{
    public override string Author { get; } = "DeadServer Team";

    public override string Name { get; } = "Boombox";

    public override string Prefix { get; } = "Boombox";

    public override Version Version { get; } = new(1, 1, 0);

    public override Version RequiredExiledVersion { get; } = new(9, 6, 1);

    public override PluginPriority Priority { get; } = PluginPriority.Medium;

    public static MainPlugin Singleton { get; private set; }

    public static Config Configs => Singleton.Config;

    public static Boombox Boombox => Singleton.Config.Boombox;

    public static Random Random { get; private set; }

    public SSKeybindSetting ChangeSongKeybind { get; private set; } = new SSKeybindSetting(null, $"BOOMBOX - Change Song", KeyCode.F, preventInteractionOnGui: true, allowSpectatorTrigger: false);

    public override void OnEnabled()
    {
        Singleton = this;
        Random = new();

        // Validate config i guess
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
        List<string> playlistTest = Boombox.Playlists[RadioRange.Short];
        playlistTest = Boombox.Playlists[RadioRange.Medium];
        playlistTest = Boombox.Playlists[RadioRange.Long];
        playlistTest = Boombox.Playlists[RadioRange.Ultra];

        // Register custom items here
        Log.Debug("Registering custom items...");
        try
        {
            CustomItem.RegisterItems(overrideClass: Configs);
            Log.Info("All custom items registered successfully");
        }
        catch (Exception ex)
        {
            Log.Error("Some custom items failed to register");
            Log.Debug(ex);
        }

        // Load audio files
        // TODO: If any files don't load, need to remove them from the playlists
        Log.Info($"Loading audio clips from directory: {Boombox.AudioPath}");
        AudioHelper.LoadAudioClips(Boombox.AudioPath, Boombox.Playlists[RadioRange.Short]);
        AudioHelper.LoadAudioClips(Boombox.AudioPath, Boombox.Playlists[RadioRange.Medium]);
        AudioHelper.LoadAudioClips(Boombox.AudioPath, Boombox.Playlists[RadioRange.Long]);
        AudioHelper.LoadAudioClips(Boombox.AudioPath, Boombox.Playlists[RadioRange.Ultra]);
        Log.Info($"Finished loading audio clips");

        // Register events
        ServerSpecificSettingsSync.ServerOnSettingValueReceived += OnSSInput;
        ServerEvents.RoundStarted += OnRoundStarted;

        base.OnEnabled();
    }

    public override void OnDisabled()
    {
        base.OnDisabled();
        ServerSpecificSettingsSync.ServerOnSettingValueReceived -= OnSSInput;
        ServerEvents.RoundStarted -= OnRoundStarted;

        Log.Debug("Un-registering custom items...");
        try
        {
            CustomItem.UnregisterItems();
            Log.Info("All custom items un-registered successfully");
        }
        catch (Exception ex)
        {
            Log.Error("Some custom items failed to un-register");
            Log.Debug(ex);
        }
        Singleton = null;
    }

    public void OnRoundStarted()
    {
        // Set up server-specific settings for the change-song key
        ServerSpecificSettingsSync.DefinedSettings = ServerSpecificSettingsSync.DefinedSettings.Append(ChangeSongKeybind).ToArray();
        ServerSpecificSettingsSync.SendToAll();
        Log.Debug($"Added SSKeybindSetting to server: {ChangeSongKeybind.Label}");
    }

    public void OnSSInput(ReferenceHub sender, ServerSpecificSettingBase setting)
    {
        if (setting.OriginalDefinition is SSKeybindSetting ssKeybind && (setting as SSKeybindSetting).SyncIsPressed)
        {
            if (ssKeybind.SettingId == ChangeSongKeybind.SettingId)
            {
                Player player = Player.Get(sender);
                if (player.CurrentItem is not null && player.CurrentItem.Serial == (ushort)Boombox.BoomboxSerial)
                {
                    Log.Debug($"Player {player.Nickname} pressed the ChangeSong key while holding the boombox");
                    Boombox.OnRadioUsed(player, player.CurrentItem);
                }
            }
        }
    }
}