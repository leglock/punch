using System;
using System.Collections.Generic;

namespace Punch.CLI;

// User-configurable settings, loaded from ~/.punch/settings.json. Also serves as
// the JSON serialization shape. Missing or invalid values fall back to defaults.
internal sealed class PunchSettings
{
    // The daily workday goal in whole hours, used for the status-bar percentage.
    public int TargetHours { get; set; } = 8;

    // Optional per-day overrides keyed by day name ("monday".."sunday",
    // case-insensitive). Days not listed fall back to TargetHours. A value of 0
    // marks a day off (the status bar then omits the percentage).
    public Dictionary<string, int>? TargetHoursByDay { get; set; }

    // Resolves the target for a given weekday: the per-day override if one is
    // present (clamped to >= 0), otherwise the flat TargetHours.
    public int GetTargetHours(DayOfWeek day)
    {
        if (TargetHoursByDay is not null)
        {
            foreach (var (key, value) in TargetHoursByDay)
            {
                if (string.Equals(key, day.ToString(), StringComparison.OrdinalIgnoreCase))
                    return Math.Max(0, value);
            }
        }
        return TargetHours;
    }
}
