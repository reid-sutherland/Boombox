using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.CustomItems.API.Features;
using MEC;
using System;
using UserSettings.ServerSpecific;
using Random = System.Random;

namespace Boombox;

public class MainPlugin : Plugin<Config>
{
    public override string Author { get; } = "DeadServer Team";

    public override string Name { get; } = "Boombox";

    public override string Prefix { get; } = "Boombox";

    public override Version Version { get; } = new(1, 0, 0);

    public override Version RequiredExiledVersion { get; } = new(9, 5, 0);

    public override PluginPriority Priority { get; } = PluginPriority.Last;

    public static MainPlugin Singleton { get; private set; }

    public static Config Configs => Singleton.Config;

    public static Random Random { get; private set; }

    public const int EVALUE = 101;

    public override void OnEnabled()
    {
        Singleton = this;
        Random = new();

        // Validate config i guess
        if (Configs.Boombox.SpawnProperties.Limit > 1)
        {
            throw new Exception("Boombox has a maximum item limit of 1.");
        }

        ServerSpecificSettingsSync.ServerOnSettingValueReceived += OnSSInput;

        // Register custom items here
        Timing.CallDelayed(5f, () =>
        {
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
        });

        base.OnEnabled();
    }

    public override void OnDisabled()
    {
        base.OnDisabled();

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

        ServerSpecificSettingsSync.ServerOnSettingValueReceived -= OnSSInput;

        Singleton = null;
    }

    public void OnSSInput(ReferenceHub sender, ServerSpecificSettingBase setting)
    {
    }
}