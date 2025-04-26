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

    [Description("Configure the Boombox's properties here")]
    public Boombox Boombox { get; set; } = new();
}