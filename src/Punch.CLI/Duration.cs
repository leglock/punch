namespace Punch.CLI;

// Formats minute counts as human-readable durations.
internal static class Duration
{
    // Compact form for individual entries: drops the hours part below 60m,
    // and drops the minutes part when they are zero. e.g. 45 -> "45m",
    // 90 -> "1h 30m", 120 -> "2h".
    public static string Humanize(int minutes)
    {
        if (minutes < 60)
            return $"{minutes}m";
        var h = minutes / 60;
        var m = minutes % 60;
        return m == 0 ? $"{h}h" : $"{h}h {m}m";
    }

    // Total form: always leads with hours, even when zero. e.g. 45 -> "0h 45m",
    // 120 -> "2h 0m", 0 -> "0h 0m".
    public static string HumanizeTotal(int minutes)
    {
        var h = minutes / 60;
        var m = minutes % 60;
        return m > 0 ? $"{h}h {m}m" : $"{h}h 0m";
    }
}
