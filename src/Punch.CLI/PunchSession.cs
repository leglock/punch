using System.Text;

namespace Punch.CLI;

// Holds the mutable editor state for a single day's TUI session: the schedule
// being edited, the input fields, the cursor/selection, and the view toggles.
// The event loop mutates these in place and the renderer reads them.
internal sealed class PunchSession
{
    public PunchSession(DaySchedule schedule, DateOnly workingDate, string filePath, int cursorSlot)
    {
        Schedule = schedule;
        WorkingDate = workingDate;
        FilePath = filePath;
        CursorSlot = cursorSlot;
    }

    public DaySchedule Schedule { get; }
    public DateOnly WorkingDate { get; }
    public string FilePath { get; }

    public IReadOnlyList<TimeBlock> Blocks => Schedule.Blocks;

    // Input fields. ActiveField: 0 = Description, 1 = Ticket.
    public StringBuilder InputBuffer { get; } = new();
    public int InputCursor { get; set; }
    public StringBuilder TicketBuffer { get; } = new();
    public int TicketCursor { get; set; }
    public int ActiveField { get; set; }

    // Cursor / selection on the timeline.
    public int CursorSlot { get; set; }
    public int SelectionLength { get; set; } = 1; // number of 15-min slots (min 1)
    public TimeBlock? SelectedBlock { get; set; }
    public bool Editing { get; set; }

    // View toggles.
    public bool ShowHelp { get; set; }
    public bool ShowTicketSummary { get; set; }
    public int LogScrollOffset { get; set; }
}
