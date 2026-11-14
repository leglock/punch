using System.Text.Json;

namespace Punch.CLI;

// Tolerant parsing is confined to this optional settings subsection.
internal static class DayTemplateResolver
{
    public static (DaySchedule Schedule, IReadOnlyList<string> Warnings) Resolve(
        JsonElement? templates, DayOfWeek day)
    {
        var schedule = new DaySchedule([]);
        var warnings = new List<string>();
        if (templates is null || templates.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return (schedule, warnings);

        if (templates.Value.ValueKind != JsonValueKind.Object)
        {
            warnings.Add("dayTemplates: expected an object keyed by full weekday name.");
            return (schedule, warnings);
        }

        foreach (var weekday in templates.Value.EnumerateObject())
        {
            // Enum.TryParse also accepts numeric strings; only full names are valid.
            var matchingDay = Enum.GetNames<DayOfWeek>()
                .FirstOrDefault(name => string.Equals(name, weekday.Name, StringComparison.OrdinalIgnoreCase));
            if (matchingDay is null)
            {
                warnings.Add($"dayTemplates '{weekday.Name}': unknown weekday; use Monday through Sunday.");
                continue;
            }

            if (weekday.Value.ValueKind == JsonValueKind.Null)
                continue;
            if (weekday.Value.ValueKind != JsonValueKind.Array)
            {
                warnings.Add($"dayTemplates {weekday.Name}: expected an entry list.");
                continue;
            }
            if (matchingDay != day.ToString())
                continue;

            var index = 0;
            foreach (var entry in weekday.Value.EnumerateArray())
            {
                index++;
                var reason = Validate(entry, out var block);
                if (reason == null && schedule.IsOverlapping(block!.StartSlot, block.Length))
                    reason = "overlaps an earlier accepted entry.";
                if (reason != null)
                    warnings.Add($"dayTemplates {weekday.Name} entry {index}: {reason}");
                else
                    schedule.Add(block!);
            }
        }

        return (schedule, warnings);
    }

    private static string? Validate(JsonElement entry, out TimeBlock? block)
    {
        block = null;
        if (entry.ValueKind != JsonValueKind.Object)
            return "expected an entry object.";
        if (!TryString(entry, "label", out var label) || string.IsNullOrWhiteSpace(label))
            return "label must be a nonblank string.";
        if (!TryString(entry, "ticket", out var ticket, optional: true))
            return "ticket must be a string or null.";
        if (!TryString(entry, "start", out var start) || !TrySlot(start, allowEndOfDay: false, out var startSlot))
            return "start must be HH:mm, 00:00–23:45, on a quarter hour.";
        if (!TryString(entry, "end", out var end) || !TrySlot(end, allowEndOfDay: true, out var endSlot))
            return "end must be HH:mm, 00:15–24:00, on a quarter hour.";
        if (endSlot <= startSlot)
            return "end must be strictly after start (no overnight ranges).";

        block = new TimeBlock(startSlot, endSlot - startSlot, label.Trim(), ticket.Trim().ToUpperInvariant());
        return null;
    }

    private static bool TryString(JsonElement entry, string name, out string value, bool optional = false)
    {
        // Last occurrence wins, consistent with case-insensitive settings deserialization.
        var field = default(JsonElement);
        foreach (var property in entry.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                field = property.Value;
        value = "";
        if (optional && field.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return true;
        if (field.ValueKind != JsonValueKind.String)
            return false;
        value = field.GetString()!;
        return true;
    }

    private static bool TrySlot(string value, bool allowEndOfDay, out int slot)
    {
        slot = 0;
        // ASCII digits only, no culture-dependent parsing, whitespace, or rounding.
        if (value.Length != 5 || value[2] != ':' ||
            value[0] is < '0' or > '9' || value[1] is < '0' or > '9' ||
            value[3] is < '0' or > '9' || value[4] is < '0' or > '9')
            return false;
        var hours = (value[0] - '0') * 10 + value[1] - '0';
        var minutes = (value[3] - '0') * 10 + value[4] - '0';
        if (allowEndOfDay && hours == 24 && minutes == 0)
        {
            slot = 96;
            return true;
        }
        if (hours > 23 || minutes > 59 || minutes % 15 != 0)
            return false;
        slot = hours * 4 + minutes / 15;
        return !allowEndOfDay || slot > 0;
    }
}
