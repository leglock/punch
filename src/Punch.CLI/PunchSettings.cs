using System;
using System.Collections.Generic;

namespace Punch.CLI;

// User-configurable settings, loaded from ~/.punch/settings.json. Also serves as
// the JSON serialization shape. Missing or invalid values fall back to defaults.
internal sealed class PunchSettings
{
    // The daily workday goal in hours (15-minute increments, e.g. 7.5 or 4.25),
    // used for the status-bar percentage.
    public decimal TargetHours { get; set; } = 8;

    // Optional per-day overrides keyed by day name ("monday".."sunday",
    // case-insensitive). Days not listed fall back to TargetHours. A value of 0
    // marks a day off (the status bar then omits the percentage).
    public Dictionary<string, decimal>? TargetHoursByDay { get; set; }

    // Targets are booked in quarter-hour slots, so only multiples of 0.25 are
    // valid; anything else falls back.
    internal static bool IsQuarterIncrement(decimal value) => value % 0.25m == 0m;

    // Resolves the target for a given weekday: the per-day override if one is
    // present (clamped to >= 0), otherwise the flat TargetHours. A per-day value
    // that isn't a 15-minute increment is invalid and falls back to TargetHours.
    public decimal GetTargetHours(DayOfWeek day)
    {
        if (TargetHoursByDay is not null)
        {
            foreach (var (key, value) in TargetHoursByDay)
            {
                if (string.Equals(key, day.ToString(), StringComparison.OrdinalIgnoreCase))
                    return IsQuarterIncrement(value) ? Math.Max(0m, value) : TargetHours;
            }
        }
        return TargetHours;
    }
}
