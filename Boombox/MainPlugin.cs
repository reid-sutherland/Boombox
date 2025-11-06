global using Log = CommonUtils.Core.Logger;

using CommonUtils.Core;
using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.CustomItems.API.Features;
using System;
using System.Collections.Generic;
using System.Linq;
using UserSettings.ServerSpecific;
using Random = System.Random;

namespace Boombox;

public class MainPlugin : Plugin<Config>
{
    public override string Author { get; } = "DeadServer Team";

    public override string Name { get; } = "Boombox";

    public override string Prefix { get; } = "Boombox";

    public override Version Version { get; } = new(1, 3, 1);

    public override Version RequiredExiledVersion { get; } = new(9, 10, 1);

    public override PluginPriority Priority { get; } = PluginPriority.Medium;

    public static MainPlugin Singleton { get; private set; }

    public static Config Configs => Singleton.Config;

    public static Boombox Boombox => Singleton.Config.Boombox;

    public static Random Random { get; private set; }

    public override void OnEnabled()
    {
        Singleton = this;
        Random = new();
        if (Configs.Debug)
        {
            Log.EnableDebug();
        }

        // Validate config i guess
        Configs.Validate();

        // Load audio files
        // TODO: Organize files by playlist directory
        Log.Info($"Loading audio clips from directory: {Configs.AudioPath}");
        bool allLoaded = true;
        foreach (Playlist playlist in Boombox.Playlists.Values)
        {
            // Skip loading any clip names that already exist in storage (or duplicates in the same list) to avoid duplicate errors
            List<string> newSongs = playlist.Songs.Where(x => !AudioClipStorage.AudioClips.ContainsKey(x.Replace(".ogg", ""))).Distinct().ToList();
            List<string> failedClips = AudioHelper.LoadAudioClips(Configs.AudioPath, newSongs, log: Configs.AudioDebug);
            if (failedClips.Count > 0)
            {
                allLoaded = false;
                foreach (string fail in failedClips)
                {
                    playlist.Songs.Remove(fail);
                }
            }
        }
        if (!allLoaded)
        {
            Log.Warn($"Removed all clips that failed to load from playlists");
        }
        Log.Info($"Finished loading audio clips");

        // At this point we don't need the filepaths, so change all file names to clip names i.e. 'boombox.ogg' -> 'boombox'
        foreach (Playlist playlist in Boombox.Playlists.Values)
        {
            playlist.Songs = playlist.Songs.Select(song => song.Replace(".ogg", "")).ToList();
        }
        Config.EasterEggSong = Config.EasterEggSong.Replace(".ogg", "");

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

        // Register events
        Configs.ServerSettings.RegisterSettings();
        ServerSpecificSettingsSync.ServerOnSettingValueReceived += OnSSInput;

        base.OnEnabled();
    }

    public override void OnDisabled()
    {
        base.OnDisabled();

        Configs.ServerSettings.UnregisterSettings();
        ServerSpecificSettingsSync.ServerOnSettingValueReceived -= OnSSInput;

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

    public void OnSSInput(ReferenceHub sender, ServerSpecificSettingBase setting)
    {
        Player player = Player.Get(sender);
        DebugKeybind($"Player {(player is not null ? player.Nickname : "<NULL>")} triggered SS input: {setting.SettingId} ({setting.DebugValue}): {setting.OriginalDefinition.Label}");
        if (player is null)
        {
            return;
        }

        if (setting is SSKeybindSetting ssKeybind && ssKeybind.SyncIsPressed)
        {
            DebugKeybind($"-- SS was a keybind setting: {ssKeybind.OriginalDefinition.Label} (id={ssKeybind.SettingId}, suggested={ssKeybind.SuggestedKey}, assigned={ssKeybind.AssignedKeyCode})");
            if (Configs.ServerSettings.CheckSSInput(setting))
            {
                if (Boombox.Check(player.CurrentItem))
                {
                    Boombox.OnBoomboxKeyPressed(player, player.CurrentItem, ssKeybind.SettingId);
                }
                else
                {
                    DebugKeybind($"-- player was NOT holding a boombox");
                }
            }
        }
    }

    public void DebugKeybind(string message)
    {
        Log.Debug(message, print: Configs.KeybindDebug);
    }
}