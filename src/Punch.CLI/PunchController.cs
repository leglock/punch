using Spectre.Console;

namespace Punch.CLI;

// Drives the TUI event loop: reads keys and mutates the PunchSession, persisting
// and re-rendering after each keypress. Rendering lives in PunchView; the data
// model and occupancy invariants live in DaySchedule.
internal sealed class PunchController
{
    private readonly PunchSession _session;
    private readonly PunchView _view;

    // Two-step confirmations for quit (Ctrl+Q then Q) and delete (Ctrl+D then D).
    private bool _confirming;
    private bool _confirmingDelete;

    public PunchController(PunchSession session, PunchView view)
    {
        _session = session;
        _view = view;
    }

    private DaySchedule Schedule => _session.Schedule;

    public void Run(LiveDisplayContext ctx)
    {
        Render(ctx);

        while (true)
        {
            var key = System.Console.ReadKey(true);

            // Two-step quit: Q confirms, anything else cancels.
            if (_confirming)
            {
                if (key.Key == ConsoleKey.Q)
                    break;
                _confirming = false;
                Render(ctx);
                continue;
            }

            // Two-step delete: D confirms, anything else cancels.
            if (_confirmingDelete)
            {
                if (key.Key == ConsoleKey.D)
                    DeleteSelected();
                _confirmingDelete = false;
                Render(ctx);
                continue;
            }

            if (key.Key == ConsoleKey.Q && key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                _confirming = true;
                Render(ctx);
                continue;
            }

            // Any key dismisses help.
            if (_session.ShowHelp)
            {
                _session.ShowHelp = false;
                Render(ctx);
                continue;
            }

            // Ticket picker is modal: arrows move the highlight, Enter assigns,
            // Esc/F4 cancel; all other keys are swallowed.
            if (_session.ShowTicketPicker)
            {
                switch (key.Key)
                {
                    case ConsoleKey.UpArrow:
                        if (_session.TicketPickerCursor > 0)
                            _session.TicketPickerCursor--;
                        break;
                    case ConsoleKey.DownArrow:
                        if (_session.TicketPickerCursor < _session.Tickets.Count - 1)
                            _session.TicketPickerCursor++;
                        break;
                    case ConsoleKey.Enter:
                        ApplyTicketPick();
                        break;
                    case ConsoleKey.Escape:
                    case ConsoleKey.F4:
                        _session.ShowTicketPicker = false;
                        break;
                }
                Render(ctx);
                continue;
            }

            // F4 opens the ticket picker for the selected block (not while editing).
            if (key.Key == ConsoleKey.F4 && _session.SelectedBlock != null && !_session.Editing)
            {
                _session.Tickets = PunchStorage.LoadTickets();
                _session.TicketPickerCursor = 0;
                _session.ShowTicketPicker = true;
                Render(ctx);
                continue;
            }

            // Esc/F3 dismiss the ticket summary; other keys are swallowed.
            if (_session.ShowTicketSummary)
            {
                if (key.Key == ConsoleKey.F3 || key.Key == ConsoleKey.Escape)
                {
                    _session.ShowTicketSummary = false;
                    Render(ctx);
                }
                continue;
            }

            if (key.Key == ConsoleKey.F3)
            {
                _session.ShowTicketSummary = true;
                Render(ctx);
                continue;
            }

            // Toggle help when not typing: a selected block parks the input
            // fields, so '?' is free over an occupied slot. With a free cursor
            // it only toggles when the input fields are empty so '?' can still
            // be typed into a description.
            if (key.KeyChar == '?' && !_session.Editing
                && (_session.SelectedBlock != null
                    || (_session.InputBuffer.Length == 0 && _session.TicketBuffer.Length == 0)))
            {
                _session.ShowHelp = !_session.ShowHelp;
                Render(ctx);
                continue;
            }

            if (key.Key == ConsoleKey.D && key.Modifiers.HasFlag(ConsoleModifiers.Control) && _session.SelectedBlock != null)
            {
                _confirmingDelete = true;
                Render(ctx);
                continue;
            }

            DispatchKey(key);

            AutoScrollToSelection();
            ClampScrollOffset();
            Render(ctx);
        }
    }

    private void DispatchKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.LeftArrow:
                HandleLeftArrow();
                break;
            case ConsoleKey.RightArrow:
                HandleRightArrow();
                break;
            case ConsoleKey.UpArrow:
                HandleGrow();
                break;
            case ConsoleKey.DownArrow:
                HandleShrink();
                break;
            case ConsoleKey.PageUp:
                _session.LogScrollOffset = Math.Max(0, _session.LogScrollOffset - 5);
                break;
            case ConsoleKey.PageDown:
                _session.LogScrollOffset = Math.Min(Math.Max(0, Schedule.Count - 1), _session.LogScrollOffset + 5);
                break;
            case ConsoleKey.E when key.Modifiers.HasFlag(ConsoleModifiers.Control):
                HandleEditStart();
                break;
            case ConsoleKey.Enter:
                HandleEnter();
                break;
            case ConsoleKey.Backspace:
                if (_session.IsInputActive && _session.ActiveCursor > 0)
                {
                    _session.ActiveBuffer.Remove(_session.ActiveCursor - 1, 1);
                    _session.ActiveCursor--;
                }
                break;
            case ConsoleKey.Delete:
                if (_session.IsInputActive && _session.ActiveCursor < _session.ActiveBuffer.Length)
                    _session.ActiveBuffer.Remove(_session.ActiveCursor, 1);
                break;
            case ConsoleKey.End:
                if (_session.IsInputActive && _session.ActiveBuffer.Length > 0)
                    _session.ActiveCursor = _session.ActiveBuffer.Length;
                break;
            case ConsoleKey.Home:
                if (_session.IsInputActive && _session.ActiveBuffer.Length > 0)
                    _session.ActiveCursor = 0;
                break;
            case ConsoleKey.Tab:
                if (_session.IsInputActive)
                {
                    _session.ActiveField = _session.ActiveField == 0 ? 1 : 0;
                    _session.ActiveCursor = _session.ActiveBuffer.Length;
                }
                break;
            default:
                HandleTextInput(key);
                break;
        }
    }

    private void HandleLeftArrow()
    {
        // While typing, Left moves the text cursor within the active field.
        if (_session.ActiveBuffer.Length > 0)
        {
            if (_session.ActiveCursor > 0)
                _session.ActiveCursor--;
            return;
        }

        // Otherwise it moves along the timeline.
        // If abandoning an edit, restore the block's original slots (freed on Ctrl+E).
        if (_session.Editing && _session.SelectedBlock != null)
            Schedule.FillSlots(_session.SelectedBlock);

        if (_session.SelectedBlock != null)
        {
            // On an existing block: jump to the slot just before it.
            var target = _session.SelectedBlock.StartSlot - 1;
            if (target >= 0)
                MoveTo(target);
        }
        else if (_session.CursorSlot > 0)
        {
            // Free selection: slide left by 1, snapping onto an adjacent block.
            var newStart = _session.CursorSlot - 1;
            var adj = Schedule.FindAt(newStart);
            if (adj != null)
                SelectBlock(adj);
            else if (Schedule.IsFree(newStart))
                _session.CursorSlot = newStart;
        }

        _session.ResetInput();
    }

    private void HandleRightArrow()
    {
        // While typing, Right moves the text cursor within the active field.
        if (_session.ActiveBuffer.Length > 0)
        {
            if (_session.ActiveCursor < _session.ActiveBuffer.Length)
                _session.ActiveCursor++;
            return;
        }

        // Otherwise it moves along the timeline.
        // If abandoning an edit, restore the block's original slots (freed on Ctrl+E).
        if (_session.Editing && _session.SelectedBlock != null)
            Schedule.FillSlots(_session.SelectedBlock);

        if (_session.SelectedBlock != null)
        {
            // On an existing block: jump to the slot just after it.
            var target = _session.SelectedBlock.StartSlot + _session.SelectedBlock.Length;
            if (target < 96)
                MoveTo(target);
        }
        else if (_session.CursorSlot + _session.SelectionLength < 96)
        {
            // Free selection: slide right by 1, snapping onto an adjacent block.
            var newEnd = _session.CursorSlot + _session.SelectionLength;
            var adj = Schedule.FindAt(newEnd);
            if (adj != null)
                SelectBlock(adj);
            else if (Schedule.IsFree(newEnd))
                _session.CursorSlot++;
        }

        _session.ResetInput();
    }

    // Lands the cursor on the given slot, selecting a block if one covers it.
    private void MoveTo(int slot)
    {
        var adj = Schedule.FindAt(slot);
        if (adj != null)
        {
            SelectBlock(adj);
        }
        else
        {
            _session.CursorSlot = slot;
            _session.SelectionLength = 1;
            _session.SelectedBlock = null;
        }
    }

    private void SelectBlock(TimeBlock block)
    {
        _session.CursorSlot = block.StartSlot;
        _session.SelectionLength = block.Length;
        _session.SelectedBlock = block;
    }

    private void HandleGrow()
    {
        if (_session.SelectedBlock == null)
        {
            var newLen = _session.SelectionLength + 1;
            if (_session.CursorSlot + newLen <= 96 && !Schedule.IsOverlapping(_session.CursorSlot, newLen))
                _session.SelectionLength = newLen;
        }
        else if (_session.Editing && Schedule.CanGrow(_session.SelectedBlock.StartSlot, _session.SelectionLength))
        {
            _session.SelectionLength++;
        }
    }

    private void HandleShrink()
    {
        if (_session.SelectedBlock == null && _session.SelectionLength > 1)
            _session.SelectionLength--;
        else if (_session.Editing && _session.SelectedBlock != null && _session.SelectionLength > 1)
            _session.SelectionLength--;
    }

    private void HandleEditStart()
    {
        // Ctrl+E: edit the selected block's label, ticket, and length. Free the
        // block's slots so a live resize can reuse them; the abandon paths (Left/
        // Right) re-fill them, and Enter re-derives occupancy via Replace.
        if (_session.SelectedBlock == null || _session.Editing)
            return;

        var block = _session.SelectedBlock;
        _session.Editing = true;
        _session.CursorSlot = block.StartSlot;
        _session.SelectionLength = block.Length;
        Schedule.FreeSlots(block);
        _session.InputBuffer.Clear();
        _session.InputBuffer.Append(block.Label);
        _session.InputCursor = _session.InputBuffer.Length;
        _session.TicketBuffer.Clear();
        _session.TicketBuffer.Append(block.Ticket);
        _session.TicketCursor = _session.TicketBuffer.Length;
        _session.ActiveField = 0;
    }

    private void HandleEnter()
    {
        if (_session.Editing && _session.SelectedBlock != null && _session.InputBuffer.Length > 0)
        {
            // Save the edited label, ticket, and (possibly resized) length. The
            // block's original slots were freed on Ctrl+E; Replace re-derives them.
            var edited = _session.SelectedBlock with
            {
                Length = _session.SelectionLength,
                Label = _session.InputBuffer.ToString(),
                Ticket = _session.TicketBuffer.ToString(),
            };
            _session.SelectedBlock = Schedule.Replace(_session.SelectedBlock, edited);
            Save();
            _session.ResetInput();
        }
        else if (_session.SelectedBlock == null && _session.InputBuffer.Length > 0)
        {
            var block = new TimeBlock(_session.CursorSlot, _session.SelectionLength,
                _session.InputBuffer.ToString(), _session.TicketBuffer.ToString());
            Schedule.Add(block);
            Save();
            _session.ResetInput();
            (_session.CursorSlot, _session.SelectionLength, _session.SelectedBlock) =
                Schedule.AdvanceAfterAdd(_session.CursorSlot);
        }
    }

    private void ApplyTicketPick()
    {
        // Write only the picked ticket onto the selected block; the label is left
        // untouched. The block keeps its slots, so Replace re-derives occupancy.
        if (_session.SelectedBlock != null && _session.Tickets.Count > 0)
        {
            var picked = _session.Tickets[_session.TicketPickerCursor];
            var updated = _session.SelectedBlock with { Ticket = picked.Ticket };
            _session.SelectedBlock = Schedule.Replace(_session.SelectedBlock, updated);
            Save();
        }
        _session.ShowTicketPicker = false;
    }

    private void HandleTextInput(ConsoleKeyInfo key)
    {
        if (key.KeyChar == '\0' || char.IsControl(key.KeyChar) || !_session.IsInputActive)
            return;

        // Ticket numbers are auto-uppercased.
        var ch = _session.ActiveField == 1 ? char.ToUpperInvariant(key.KeyChar) : key.KeyChar;
        _session.ActiveBuffer.Insert(_session.ActiveCursor, ch);
        _session.ActiveCursor++;
    }

    private void DeleteSelected()
    {
        Schedule.Remove(_session.SelectedBlock!);
        Save();
        _session.SelectedBlock = null;
        _session.SelectionLength = 1;
        _session.ResetInput();
    }

    private void AutoScrollToSelection()
    {
        var selected = _session.SelectedBlock;
        if (selected == null)
            return;

        var sorted = Schedule.Blocks.OrderBy(b => b.StartSlot).ToList();
        var selIdx = sorted.FindIndex(b => b.StartSlot == selected.StartSlot && b.Length == selected.Length);
        if (selIdx < 0)
            return;

        // When scrolled, the "more above" indicator takes 1 line.
        var visHeight = Math.Max(1, System.Console.WindowHeight - 10 - 2 - 1);
        if (selIdx < _session.LogScrollOffset)
            _session.LogScrollOffset = selIdx;
        else if (selIdx >= _session.LogScrollOffset + visHeight)
            _session.LogScrollOffset = selIdx - visHeight + 1;
    }

    private void ClampScrollOffset()
    {
        // When scrolled down, the "▲ more above" indicator takes 1 line, so only
        // viewHeight-1 block rows are visible at the bottom.
        var viewHeight = Math.Max(1, System.Console.WindowHeight - 10 - 2);
        var maxOff = Math.Max(0, Schedule.Count - (viewHeight - 1));
        _session.LogScrollOffset = Math.Clamp(_session.LogScrollOffset, 0, maxOff);
    }

    private void Save() => PunchStorage.Save(_session.WorkingDate, Schedule.Blocks);

    private void Render(LiveDisplayContext ctx)
    {
        _view.Render(_session, _confirming, _confirmingDelete);
        ctx.Refresh();
    }
}
