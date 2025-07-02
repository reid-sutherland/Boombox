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

    public override Version Version { get; } = new(1, 1, 1);

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

        // Import any other boombox info that can't automatically be inferred from the config
        Boombox.Name = string.IsNullOrEmpty(Configs.HintManager.BoomboxName) ? Boombox.Name : Configs.HintManager.BoomboxName;
        Boombox.Description = string.IsNullOrEmpty(Configs.HintManager.BoomboxDescription) ? Boombox.Description : Configs.HintManager.BoomboxDescription;

        // Load audio files
        // TODO: Organize files by playlist directory
        Log.Info($"Loading audio clips from directory: {Configs.AudioPath}");
        bool allLoaded = true;
        foreach (Playlist playlist in Boombox.Playlists.Values)
        {
            List<string> failedClips = AudioHelper.LoadAudioClips(Configs.AudioPath, playlist.Songs);
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
        ServerSettings.RegisterSettings();
        ServerSpecificSettingsSync.ServerOnSettingValueReceived += OnSSInput;

        base.OnEnabled();
    }

    public override void OnDisabled()
    {
        base.OnDisabled();

        ServerSettings.UnregisterSettings();
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
        Log.Debug($"Player {(player is not null ? player.Nickname : "<NULL>")} triggered SS input: ({setting.SettingId}) {setting.DebugValue}");
        if ((setting as SSKeybindSetting).SyncIsPressed && setting.OriginalDefinition is SSKeybindSetting ssKeybind)
        {
            //Log.Debug($"-- SS was a keybind setting: {ssKeybind.OriginalDefinition.Label} (id={ssKeybind.SettingId}, suggested={ssKeybind.SuggestedKey}, assigned={ssKeybind.AssignedKeyCode})");
            if (ServerSettings.CheckSSInput(setting))
            {
                if (player.CurrentItem is not null && CustomItem.TryGet("JBL Speaker", out CustomItem boombox))
                {
                    //Log.Debug($"-- found a boombox: {boombox.Name} (item-serial={player.CurrentItem.Serial}, tracked-serial={boombox.TrackedSerials.FirstOrDefault()}, BoomboxSerial={Boombox.BoomboxSerial}/{(ushort)Boombox.BoomboxSerial}");
                    if (boombox.Check(player.CurrentItem))
                    {
                        //Log.Debug($"-- player's item appears to be a boombox: {boombox.Name}");

                        bool shuffle = ssKeybind.SettingId == ServerSettings.ShuffleSongKeybind.Base.SettingId;
                        string keyType = ssKeybind.SettingId == ServerSettings.ShuffleSongKeybind.Base.SettingId ? "ShuffleSong" : "ChangeSong";
                        Log.Debug($"-- player {player.Nickname} pressed the {keyType} key while holding the boombox");
                        Boombox.OnBoomboxKeyPressed(player, player.CurrentItem, shuffle);
                    }
                }
                else
                {
                    //Log.Debug($"-- did NOT find a boombox");
                }
            }
        }
    }
}