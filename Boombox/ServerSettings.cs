using Exiled.API.Features.Core.UserSettings;
using UnityEngine;

namespace Boombox;

public static class ServerSettings
{
    public static HeaderSetting BoomboxHeader { get; set; } = new HeaderSetting("Boombox");

    public static KeybindSetting ChangeSongKeybind { get; private set; }
    public static KeybindSetting ShuffleSongKeybind { get; private set; }

    //public static bool ShouldShowX(Player player) => !(player.SessionVariables.TryGetValue("X", out var value) && value is bool enabled && !enabled);

    public static void RegisterSettings()
    {
        ChangeSongKeybind = new KeybindSetting(
            id: 80081,
            label: $"Change Song - {KeyCode.F}",
            suggested: KeyCode.F,
            preventInteractionOnGUI: true,
            allowSpectatorTrigger: false,
            header: BoomboxHeader
        );
        ShuffleSongKeybind = new KeybindSetting(
            id: 80082,
            label: $"Shuffle Song - {KeyCode.G}",
            suggested: KeyCode.G,
            preventInteractionOnGUI: true,
            allowSpectatorTrigger: false,
            header: BoomboxHeader
        );

        SettingBase.Register(settings: new[]
        {
            ChangeSongKeybind,
            ShuffleSongKeybind,
        });
    }
    public static void UnregisterSettings()
    {
        SettingBase.Unregister(settings: new[]
        {
            ChangeSongKeybind,
            ShuffleSongKeybind,
        });
    }
}