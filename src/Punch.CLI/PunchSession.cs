using System.Text;

namespace Punch.CLI;

// Holds the mutable editor state for a single day's TUI session: the schedule
// being edited, the input fields, the cursor/selection, and the view toggles.
// The event loop mutates these in place and the renderer reads them.
internal sealed class PunchSession
{
    public PunchSession(DaySchedule schedule, DateOnly workingDate, string filePath, int cursorSlot, int targetHours = 8)
    {
        Schedule = schedule;
        WorkingDate = workingDate;
        FilePath = filePath;
        CursorSlot = cursorSlot;
        TargetHours = targetHours;
    }

    public DaySchedule Schedule { get; }
    public DateOnly WorkingDate { get; }
    public string FilePath { get; }

    // The daily workday goal in whole hours, used for the status-bar percentage.
    public int TargetHours { get; }

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

    // Ticket picker overlay. Tickets is reloaded from disk each time the picker
    // is opened, so edits to ~/.punch/tickets.txt are picked up without a restart.
    public bool ShowTicketPicker { get; set; }
    public int TicketPickerCursor { get; set; }
    public List<TicketEntry> Tickets { get; set; } = new();

    // Text input is editable when composing a new entry (no block selected) or
    // editing an existing one.
    public bool IsInputActive => SelectedBlock == null || Editing;

    // The buffer/cursor for whichever field currently has focus.
    public StringBuilder ActiveBuffer => ActiveField == 0 ? InputBuffer : TicketBuffer;

    public int ActiveCursor
    {
        get => ActiveField == 0 ? InputCursor : TicketCursor;
        set
        {
            if (ActiveField == 0) InputCursor = value;
            else TicketCursor = value;
        }
    }

    // Clears both input fields and returns focus to the Description field,
    // leaving edit mode. Does not touch the cursor selection on the timeline.
    public void ResetInput()
    {
        Editing = false;
        ActiveField = 0;
        InputBuffer.Clear();
        InputCursor = 0;
        TicketBuffer.Clear();
        TicketCursor = 0;
    }
}
