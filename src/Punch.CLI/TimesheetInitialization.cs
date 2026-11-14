namespace Punch.CLI;

internal sealed record TimesheetInitialization(
    DaySchedule Schedule, string Note, bool TemplatesApplied, IReadOnlyList<string> Warnings)
{
    public (int CursorSlot, int SelectionLength, TimeBlock? SelectedBlock) GetInitialSelection()
    {
        const int defaultSlot = 34; // 08:30
        if (TemplatesApplied)
        {
            var block = Schedule.FindAt(defaultSlot);
            return block == null ? (defaultSlot, 1, null) : (block.StartSlot, block.Length, block);
        }

        // Preserve the cursor policy for existing timesheets.
        var lastEnd = Schedule.Count > 0 ? Schedule.Blocks.Max(b => b.StartSlot + b.Length) : 96;
        return (lastEnd < 96 ? lastEnd : defaultSlot, 1, null);
    }
}
