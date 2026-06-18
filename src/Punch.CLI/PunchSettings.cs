namespace Punch.CLI;

// User-configurable settings, loaded from ~/.punch/settings.json. Also serves as
// the JSON serialization shape. Missing or invalid values fall back to defaults.
internal sealed class PunchSettings
{
    // The daily workday goal in whole hours, used for the status-bar percentage.
    public int TargetHours { get; set; } = 8;
}
