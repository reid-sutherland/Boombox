using Exiled.API.Features.Core.UserSettings;
using System.Collections.Generic;
using System.ComponentModel;
using UnityEngine;
using UserSettings.ServerSpecific;
using YamlDotNet.Serialization;

namespace Boombox;

public class ServerSettings
{
    [YamlIgnore]
    public HeaderSetting BoomboxHeader { get; private set; } = new HeaderSetting("Boombox");

    [YamlIgnore]
    public KeybindSetting ChangeSongKeybind { get; private set; }

    [YamlIgnore]
    public KeybindSetting ShuffleSongKeybind { get; private set; }
    [YamlIgnore]
    public KeybindSetting LoopSongKeybind { get; private set; }

    private List<int> KeybindSettingIds { get; set; } = new();

    [Description("Modify ServerSpecificSettings properties for the ChangeSong keybind here.")]
    public int ChangeSongKeybindId { get; set; } = 80081;
    public string ChangeSongKeybindLabel { get; set; } = $"Change Song - {KeyCode.F}";
    public string ChangeSongKeybindHintDescription { get; set; } = "";

    [Description("Modify ServerSpecificSettings properties for the ShuffleSong keybind here.")]
    public int ShuffleSongKeybindId { get; set; } = 80082;
    public string ShuffleSongKeybindLabel { get; set; } = $"Shuffle Song - {KeyCode.G}";
    public string ShuffleSongKeybindHintDescription { get; set; } = "";
    public int LoopSongKeybindId { get; set; } = 80083;
    public string LoopSongKeybindLabel { get; set; } = $"Loop Song - {KeyCode.H}";
    public string LoopSongKeybindHintDescription { get; set; } = "";

    //public static bool ShouldShowX(Player player) => !(player.SessionVariables.TryGetValue("X", out var value) && value is bool enabled && !enabled);

    public void RegisterSettings()
    {
        ChangeSongKeybind = new(
            id: ChangeSongKeybindId,
            label: ChangeSongKeybindLabel,
            suggested: KeyCode.F,
            preventInteractionOnGUI: true,
            allowSpectatorTrigger: false,
            hintDescription: ChangeSongKeybindHintDescription,
            header: BoomboxHeader
        );
        ShuffleSongKeybind = new(
            id: ShuffleSongKeybindId,
            label: ShuffleSongKeybindLabel,
            suggested: KeyCode.G,
            preventInteractionOnGUI: true,
            allowSpectatorTrigger: false,
            hintDescription: ShuffleSongKeybindHintDescription,
            header: BoomboxHeader
        );
        LoopSongKeybind = new(
            id: LoopSongKeybindId,
            label: "Loop Song",
            suggested: KeyCode.H,
            preventInteractionOnGUI: true,
            allowSpectatorTrigger: false,
            hintDescription: LoopSongKeybindHintDescription,
            header: BoomboxHeader
        );

        KeybindSettingIds = new()
        {
            ChangeSongKeybind.Base.SettingId,
            ShuffleSongKeybind.Base.SettingId,
            LoopSongKeybind.Base.SettingId,
        };

        SettingBase.Register(settings: new[]
        {
            ChangeSongKeybind,
            ShuffleSongKeybind,
            LoopSongKeybind,
        });
    }

    public void UnregisterSettings()
    {
        SettingBase.Unregister(settings: new[]
        {
            ChangeSongKeybind,
            ShuffleSongKeybind,
            LoopSongKeybind,
        });
    }

    public bool CheckSSInput(ServerSpecificSettingBase setting)
    {
        if (setting.OriginalDefinition is SSKeybindSetting ssKeybind)
        {
            return KeybindSettingIds.Contains(ssKeybind.SettingId);
        }
        return false;
    }
}