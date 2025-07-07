// TODO: Fix issues with Core and then re-add this
//global using Log = CommonUtils.Core.Logger;

//using CommonUtils.Core;
using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.CustomItems.API.Features;
using System;
using System.Collections.Generic;
using UserSettings.ServerSpecific;
using Random = System.Random;

namespace Boombox;

public class MainPlugin : Plugin<Config>
{
    public override string Author { get; } = "DeadServer Team";

    public override string Name { get; } = "Boombox";

    public override string Prefix { get; } = "Boombox";

    public override Version Version { get; } = new(1, 2, 2);

    public override Version RequiredExiledVersion { get; } = new(9, 6, 1);

    public override PluginPriority Priority { get; } = PluginPriority.Medium;

    public static MainPlugin Singleton { get; private set; }

    public static Config Configs => Singleton.Config;

    public static Boombox Boombox => Singleton.Config.Boombox;

    public static Random Random { get; private set; }

    public override void OnEnabled()
    {
        Singleton = this;
        Random = new();
        //if (Configs.Debug)
        //{
        //    Log.EnableDebug();
        //}

        // Validate config i guess
        Configs.Validate();

        // Load audio files
        // TODO: Organize files by playlist directory
        Log.Info($"Loading audio clips from directory: {Configs.AudioPath}");
        bool allLoaded = true;
        foreach (Playlist playlist in Boombox.Playlists.Values)
        {
            List<string> failedClips = AudioHelper.LoadAudioClips(Configs.AudioPath, playlist.Songs, log: Configs.AudioDebug);
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
        if (setting is SSKeybindSetting ssKeybind && ssKeybind.SyncIsPressed)
        {
            DebugKeybind($"-- SS was a keybind setting: {ssKeybind.OriginalDefinition.Label} (id={ssKeybind.SettingId}, suggested={ssKeybind.SuggestedKey}, assigned={ssKeybind.AssignedKeyCode})");
            if (Configs.ServerSettings.CheckSSInput(setting))
            {
                if (Boombox.Check(player.CurrentItem))
                {
                    if (CustomItem.TryGet(player.CurrentItem, out CustomItem item) && item is not null && item is Boombox boombox)
                    {
                        DebugKeybind($"-- found a boombox: {boombox.Name} (item-serial={player.CurrentItem.Serial}, is-tracked-serial={(boombox.TrackedSerials.Contains(player.CurrentItem.Serial) ? "true" : "false")})");
                    }

                    bool shuffle = ssKeybind.SettingId == Configs.ServerSettings.ShuffleSongKeybind.Base.SettingId;
                    string keyType = ssKeybind.SettingId == Configs.ServerSettings.ShuffleSongKeybind.Base.SettingId ? "ShuffleSong" : "ChangeSong";
                    DebugKeybind($"-- player {player.Nickname} pressed the {keyType} key while holding {Boombox.Identifier(player.CurrentItem.Serial)}");
                    Boombox.OnBoomboxKeyPressed(player, player.CurrentItem, shuffle);
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
        if (Configs.KeybindDebug)
        {
            Log.Debug(message);
        }
    }
}