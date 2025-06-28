// TODO: Switch back to Core logger once the debug bug is fixed -- then make sure Logger.PrintDebug is back in
//global using Log = CommonUtils.Core.Logger;

using CommonUtils.Core;
using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.CustomItems.API.Features;
using System;
using System.IO;
using UserSettings.ServerSpecific;
using Random = System.Random;

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

    public static string AudioPath => Path.Combine(Paths.Exiled, "Audio", "Boombox");

    public override void OnEnabled()
    {
        Singleton = this;
        Random = new();
        AudioHelper.AudioDebug = Configs.AudioDebug;

        // Validate config i guess
        Configs.Validate();
        if (Configs.EasterEggEnabled)
        {
            Log.Debug($"Easter egg is enabled :)");
        }

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
        // TODO: Organize files by playlist directory
        Log.Info($"Loading audio clips from directory: {AudioPath}");
        AudioHelper.LoadAudioClips(AudioPath, Boombox.Playlists[RadioRange.Short].Songs);
        AudioHelper.LoadAudioClips(AudioPath, Boombox.Playlists[RadioRange.Medium].Songs);
        AudioHelper.LoadAudioClips(AudioPath, Boombox.Playlists[RadioRange.Long].Songs);
        AudioHelper.LoadAudioClips(AudioPath, Boombox.Playlists[RadioRange.Ultra].Songs);
        Log.Info($"Finished loading audio clips");

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
        if (setting.OriginalDefinition is SSKeybindSetting ssKeybind && (setting as SSKeybindSetting).SyncIsPressed)
        {
            if (ssKeybind.SettingId == ServerSettings.ChangeSongKeybind.Base.SettingId
                || ssKeybind.SettingId == ServerSettings.ShuffleSongKeybind.Base.SettingId)
            {
                Player player = Player.Get(sender);
                if (player.CurrentItem is not null && player.CurrentItem.Serial == (ushort)Boombox.BoomboxSerial)
                {
                    bool shuffle = ssKeybind.SettingId == ServerSettings.ShuffleSongKeybind.Base.SettingId;
                    string keyType = ssKeybind.SettingId == ServerSettings.ShuffleSongKeybind.Base.SettingId ? "ShuffleSong" : "ChangeSong";
                    Log.Debug($"Player {player.Nickname} pressed the {keyType} key while holding the boombox");
                    Boombox.OnBoomboxKeyPressed(player, player.CurrentItem, shuffle);
                }
            }
        }
    }
}