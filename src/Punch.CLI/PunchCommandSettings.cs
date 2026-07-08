using System.ComponentModel;
using Spectre.Console.Cli;

namespace Punch.CLI;

internal sealed class PunchCommandSettings : CommandSettings
{
    [Description("Show version information")]
    [CommandOption("-v|--version")]
    public bool Version { get; set; }

    [Description("Working date override (format: yyyy-MM-dd)")]
    [CommandOption("-d|--date")]
    public string? Date { get; set; }

    [Description("Open yesterday's timesheet")]
    [CommandOption("-y|--yesterday")]
    public bool Yesterday { get; set; }
}
