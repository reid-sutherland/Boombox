using Exiled.API.Interfaces;
using System.ComponentModel;

namespace Boombox;

public sealed class Config : IConfig
{
    [Description("Whether the Boombox is enabled and should spawn in SCP-914.")]
    public bool IsEnabled { get; set; } = true;

    public bool Debug { get; set; } = false;

    [Description("Whether AudioAPI routines will write logs to debug.")]
    public bool AudioDebug { get; set; } = false;

    [Description("Whether hints should be shown with the song title, playlist title, etc.")]
    public bool ShowHints { get; set; } = true;

    [Description("Configure the Boombox's properties here")]
    public Boombox Boombox { get; set; } = new();

    [Description("Whether the J A R E D warhead easter egg is active.")]
    public bool EasterEggEnabled { get; set; } = false;

    public string EasterEggSong { get; set; } = "";

    public string EasterEggPlayerId { get; set; } = "";

    public float EasterEggDelay { get; set; } = 0.0f;
}