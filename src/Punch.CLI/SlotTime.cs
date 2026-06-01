namespace Punch.CLI;

// Helpers for converting 15-minute slot indices (0..96) into clock times.
internal static class SlotTime
{
    public static (int Hours, int Minutes) ToTime(int slot)
    {
        return (slot / 4, (slot % 4) * 15);
    }

    // Formats a slot range as "HH:MM–HH:MM" (en-dash separator).
    public static string FormatRange(int startSlot, int endSlot)
    {
        var (sh, sm) = ToTime(startSlot);
        var (eh, em) = ToTime(endSlot);
        return $"{sh:D2}:{sm:D2}–{eh:D2}:{em:D2}";
    }
}
