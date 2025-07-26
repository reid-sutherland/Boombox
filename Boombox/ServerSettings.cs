using Exiled.API.Features.Core.UserSettings;
using System.Collections.Generic;
using System.ComponentModel;
using UnityEngine;
using UserSettings.ServerSpecific;
using YamlDotNet.Serialization;

namespace Boombox;

public class ServerSettings
{
    private List<int> KeybindSettingIds { get; set; } = new();

    [YamlIgnore]
    public HeaderSetting BoomboxHeader { get; private set; } = new HeaderSetting("Boombox");

    [YamlIgnore]
    public KeybindSetting ChangeSongKeybind { get; private set; }
    [YamlIgnore]
    public KeybindSetting ShuffleSongKeybind { get; private set; }
    [YamlIgnore]
    public KeybindSetting SwitchLoopKeybind { get; private set; }

    [Description("Modify ServerSpecificSettings properties for the ChangeSong keybind here.")]
    public int ChangeSongKeybindId { get; set; } = 80081;
    public string ChangeSongKeybindLabel { get; set; } = $"Change Song - {KeyCode.F}";
    public string ChangeSongKeybindHintDescription { get; set; } = "";

    [Description("Modify ServerSpecificSettings properties for the ShuffleSong keybind here.")]
    public int ShuffleSongKeybindId { get; set; } = 80082;
    public string ShuffleSongKeybindLabel { get; set; } = $"Shuffle Song - {KeyCode.G}";
    public string ShuffleSongKeybindHintDescription { get; set; } = "";
    [Description("Modify ServerSpecificSettings properties for the SwitchLoop keybind here.")]
    public int SwitchLoopKeybindId { get; set; } = 80083;
    public string SwitchLoopKeybindLabel { get; set; } = $"Switch Loop Mode - {KeyCode.L}";
    public string SwitchLoopKeybindHintDescription { get; set; } = "";

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
        SwitchLoopKeybind = new(
            id: SwitchLoopKeybindId,
            label: SwitchLoopKeybindLabel,
            suggested: KeyCode.L,
            preventInteractionOnGUI: true,
            allowSpectatorTrigger: false,
            hintDescription: SwitchLoopKeybindHintDescription,
            header: BoomboxHeader
        );

        KeybindSettingIds = new()
        {
            ChangeSongKeybind.Base.SettingId,
            ShuffleSongKeybind.Base.SettingId,
            SwitchLoopKeybind.Base.SettingId,
        };

        SettingBase.Register(settings: new[]
        {
            ChangeSongKeybind,
            ShuffleSongKeybind,
            SwitchLoopKeybind,
        });
    }

    public void UnregisterSettings()
    {
        SettingBase.Unregister(settings: new[]
        {
            ChangeSongKeybind,
            ShuffleSongKeybind,
            SwitchLoopKeybind,
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

    public string GetKeyType(int settingId)
    {
        if (settingId == ChangeSongKeybindId)
        {
            return "ChangeSong";
        }
        else if (settingId == ShuffleSongKeybindId)
        {
            return "ShuffleSong";
        }
        else if (settingId == SwitchLoopKeybindId)
        {
            return "SwitchLoop";
        }
        return "Unknown";
    }
}