using System.Reflection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Punch.CLI;

internal sealed class PunchCommand : Command<PunchCommandSettings>
{
    public override int Execute(CommandContext context, PunchCommandSettings settings)
    {
        if (settings.Version)
        {
            var version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
            Console.WriteLine(version);
            return 0;
        }

        DateOnly workingDate;
        if (settings.Date != null)
        {
            if (!DateOnly.TryParseExact(settings.Date, "yyyy-MM-dd", out workingDate))
            {
                Console.Error.WriteLine($"Invalid date format: '{settings.Date}'. Expected yyyy-MM-dd.");
                return 1;
            }
        }
        else
        {
            workingDate = DateOnly.FromDateTime(DateTime.Now);
        }

        var filePath = PunchStorage.GetDisplayPath(workingDate);
        DaySchedule schedule;
        try
        {
            schedule = new DaySchedule(PunchStorage.Load(workingDate));
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[bold red]Error:[/] Failed to load time log from [bold cyan]{Markup.Escape(filePath)}[/].");
            AnsiConsole.MarkupLine($"[bold yellow]Reason:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }

        var cursorSlot = 34; // default to 8:30am
        if (schedule.Count > 0)
        {
            var lastEnd = schedule.Blocks.Max(b => b.StartSlot + b.Length);
            if (lastEnd < 96)
                cursorSlot = lastEnd;
        }

        var session = new PunchSession(schedule, workingDate, filePath, cursorSlot);

        AnsiConsole.AlternateScreen(() =>
        {
            try
            {
                AnsiConsole.Cursor.Hide();

            var layout = new Layout("Root")
                .SplitRows(
                    new Layout("Timeline").Size(5),
                    new Layout("Messages").Ratio(4),
                    new Layout("Input").Size(4),
                    new Layout("StatusBar").Size(1));
            var view = new PunchView(layout);

            AnsiConsole.Live(layout).Start(ctx =>
            {
                view.Render(session);
                ctx.Refresh();

                var confirming = false;
                var confirmingDelete = false;

                while (true)
                {
                    var key = System.Console.ReadKey(true);

                    if (confirming)
                    {
                        if (key.Key == ConsoleKey.Q)
                            break;

                        confirming = false;
                        view.Render(session);
                        ctx.Refresh();
                        continue;
                    }

                    if (confirmingDelete)
                    {
                        if (key.Key == ConsoleKey.D)
                        {
                            schedule.Remove(session.SelectedBlock!);
                            PunchStorage.Save(workingDate, schedule.Blocks);
                            session.SelectedBlock = null;
                            session.SelectionLength = 1;
                            session.Editing = false;
                            session.InputBuffer.Clear();
                            session.InputCursor = 0;
                            session.TicketBuffer.Clear();
                            session.TicketCursor = 0;
                            session.ActiveField = 0;
                            confirmingDelete = false;
                        }
                        else
                        {
                            confirmingDelete = false;
                        }

                        view.Render(session);
                        ctx.Refresh();
                        continue;
                    }

                    if (key.Key == ConsoleKey.Q && key.Modifiers.HasFlag(ConsoleModifiers.Control))
                    {
                        confirming = true;
                        view.Render(session, confirming: true);
                        ctx.Refresh();
                        continue;
                    }

                    if (session.ShowHelp)
                    {
                        session.ShowHelp = false;
                        view.Render(session);
                        ctx.Refresh();
                        continue;
                    }

                    if (session.ShowTicketSummary)
                    {
                        if (key.Key == ConsoleKey.F3)
                        {
                            session.ShowTicketSummary = false;
                            view.Render(session);
                            ctx.Refresh();
                        }
                        continue;
                    }

                    if (key.Key == ConsoleKey.F3)
                    {
                        session.ShowTicketSummary = true;
                        view.Render(session);
                        ctx.Refresh();
                        continue;
                    }

                    if (key.KeyChar == '?' && session.SelectedBlock == null && !session.Editing && session.InputBuffer.Length == 0 && session.TicketBuffer.Length == 0)
                    {
                        session.ShowHelp = !session.ShowHelp;
                        view.Render(session);
                        ctx.Refresh();
                        continue;
                    }

                    var currentBuffer = session.ActiveField == 0 ? session.InputBuffer : session.TicketBuffer;
                    var currentCursor = session.ActiveField == 0 ? session.InputCursor : session.TicketCursor;

                    if (key.Key == ConsoleKey.LeftArrow && currentBuffer.Length > 0)
                    {
                        if (currentCursor > 0)
                            currentCursor--;
                    }
                    else if (key.Key == ConsoleKey.RightArrow && currentBuffer.Length > 0)
                    {
                        if (currentCursor < currentBuffer.Length)
                            currentCursor++;
                    }
                    else if (key.Key == ConsoleKey.LeftArrow)
                    {
                        // If abandoning an edit, restore the block's original slots in the schedule
                        // (they were freed on Ctrl+E to allow live resize).
                        if (session.Editing && session.SelectedBlock != null)
                        {
                            schedule.FillSlots(session.SelectedBlock);
                        }
                        if (session.SelectedBlock != null)
                        {
                            // On an existing block: jump to slot just before it
                            var target = session.SelectedBlock.StartSlot - 1;
                            if (target >= 0)
                            {
                                var adj = schedule.FindAt(target);
                                if (adj != null)
                                {
                                    session.CursorSlot = adj.StartSlot;
                                    session.SelectionLength = adj.Length;
                                    session.SelectedBlock = adj;
                                }
                                else
                                {
                                    session.CursorSlot = target;
                                    session.SelectionLength = 1;
                                    session.SelectedBlock = null;
                                }
                            }
                        }
                        else if (session.CursorSlot > 0)
                        {
                            // Free selection: slide left by 1, keeping length
                            var newStart = session.CursorSlot - 1;
                            var adj = schedule.FindAt(newStart);
                            if (adj != null)
                            {
                                session.CursorSlot = adj.StartSlot;
                                session.SelectionLength = adj.Length;
                                session.SelectedBlock = adj;
                            }
                            else if (schedule.IsFree(newStart))
                            {
                                session.CursorSlot = newStart;
                            }
                        }
                        session.Editing = false;
                        session.ActiveField = 0;
                        session.InputBuffer.Clear(); session.InputCursor = 0;
                        session.TicketBuffer.Clear(); session.TicketCursor = 0;
                    }
                    else if (key.Key == ConsoleKey.RightArrow)
                    {
                        // If abandoning an edit, restore the block's original slots in the schedule
                        // (they were freed on Ctrl+E to allow live resize).
                        if (session.Editing && session.SelectedBlock != null)
                        {
                            schedule.FillSlots(session.SelectedBlock);
                        }
                        if (session.SelectedBlock != null)
                        {
                            // On an existing block: jump to slot just after it
                            var target = session.SelectedBlock.StartSlot + session.SelectedBlock.Length;
                            if (target < 96)
                            {
                                var adj = schedule.FindAt(target);
                                if (adj != null)
                                {
                                    session.CursorSlot = adj.StartSlot;
                                    session.SelectionLength = adj.Length;
                                    session.SelectedBlock = adj;
                                }
                                else
                                {
                                    session.CursorSlot = target;
                                    session.SelectionLength = 1;
                                    session.SelectedBlock = null;
                                }
                            }
                        }
                        else if (session.CursorSlot + session.SelectionLength < 96)
                        {
                            // Free selection: slide right by 1, keeping length
                            var newEnd = session.CursorSlot + session.SelectionLength; // the slot that would become the new last slot
                            var adj = schedule.FindAt(newEnd);
                            if (adj != null)
                            {
                                session.CursorSlot = adj.StartSlot;
                                session.SelectionLength = adj.Length;
                                session.SelectedBlock = adj;
                            }
                            else if (schedule.IsFree(newEnd))
                            {
                                session.CursorSlot++;
                            }
                        }
                        session.Editing = false;
                        session.ActiveField = 0;
                        session.InputBuffer.Clear(); session.InputCursor = 0;
                        session.TicketBuffer.Clear(); session.TicketCursor = 0;
                    }
                    else if (key.Key == ConsoleKey.UpArrow)
                    {
                        if (session.SelectedBlock == null)
                        {
                            var newLen = session.SelectionLength + 1;
                            if (session.CursorSlot + newLen <= 96 && !schedule.IsOverlapping(session.CursorSlot, newLen))
                                session.SelectionLength = newLen;
                        }
                        else if (session.Editing && schedule.CanGrow(session.SelectedBlock.StartSlot, session.SelectionLength))
                        {
                            session.SelectionLength++;
                        }
                    }
                    else if (key.Key == ConsoleKey.DownArrow)
                    {
                        if (session.SelectedBlock == null && session.SelectionLength > 1)
                            session.SelectionLength--;
                        else if (session.Editing && session.SelectedBlock != null && session.SelectionLength > 1)
                            session.SelectionLength--;
                    }
                    else if (key.Key == ConsoleKey.PageUp)
                    {
                        session.LogScrollOffset = Math.Max(0, session.LogScrollOffset - 5);
                    }
                    else if (key.Key == ConsoleKey.PageDown)
                    {
                        var maxOffset = Math.Max(0, schedule.Count - 1);
                        session.LogScrollOffset = Math.Min(maxOffset, session.LogScrollOffset + 5);
                    }
                    else if (key.Key == ConsoleKey.D && key.Modifiers.HasFlag(ConsoleModifiers.Control))
                    {
                        if (session.SelectedBlock != null)
                        {
                            confirmingDelete = true;
                            view.Render(session, confirmingDelete: true);
                            ctx.Refresh();
                            continue;
                        }
                    }
                    else if (key.Key == ConsoleKey.E && key.Modifiers.HasFlag(ConsoleModifiers.Control))
                    {
                        // Ctrl+E: edit selected block's label, ticket, and length.
                        // Free the block's slots so resize can reuse them; Enter re-marks per
                        // SelectionLength, abandon paths re-mark per the block's original length.
                        if (session.SelectedBlock != null && !session.Editing)
                        {
                            session.Editing = true;
                            session.CursorSlot = session.SelectedBlock.StartSlot;
                            session.SelectionLength = session.SelectedBlock.Length;
                            schedule.FreeSlots(session.SelectedBlock);
                            session.InputBuffer.Clear();
                            session.InputBuffer.Append(session.SelectedBlock.Label);
                            session.InputCursor = session.InputBuffer.Length;
                            session.TicketBuffer.Clear();
                            session.TicketBuffer.Append(session.SelectedBlock.Ticket);
                            session.TicketCursor = session.TicketBuffer.Length;
                            session.ActiveField = 0;
                            currentCursor = session.InputBuffer.Length;
                        }
                    }
                    else if (key.Key == ConsoleKey.Enter)
                    {
                        if (session.Editing && session.SelectedBlock != null && session.InputBuffer.Length > 0)
                        {
                            // Save edited label, ticket, and (possibly resized) length.
                            // The block's original slots were freed on Ctrl+E.
                            var newLabel = session.InputBuffer.ToString();
                            var newTicket = session.TicketBuffer.ToString();
                            var edited = session.SelectedBlock with { Length = session.SelectionLength, Label = newLabel, Ticket = newTicket };
                            session.SelectedBlock = schedule.Replace(session.SelectedBlock, edited);
                            PunchStorage.Save(workingDate, schedule.Blocks);
                            session.Editing = false;
                            session.InputBuffer.Clear();
                            session.InputCursor = 0;
                            session.TicketBuffer.Clear();
                            session.TicketCursor = 0;
                            session.ActiveField = 0;
                            currentCursor = 0;
                        }
                        else if (session.SelectedBlock == null && session.InputBuffer.Length > 0)
                        {
                            var label = session.InputBuffer.ToString();
                            var ticket = session.TicketBuffer.ToString();
                            var block = new TimeBlock(session.CursorSlot, session.SelectionLength, label, ticket);
                            schedule.Add(block);
                            PunchStorage.Save(workingDate, schedule.Blocks);

                            session.InputBuffer.Clear();
                            session.InputCursor = 0;
                            session.TicketBuffer.Clear();
                            session.TicketCursor = 0;
                            session.ActiveField = 0;
                            currentCursor = 0;

                            (session.CursorSlot, session.SelectionLength, session.SelectedBlock) = schedule.AdvanceAfterAdd(session.CursorSlot);
                        }
                    }
                    else if (key.Key == ConsoleKey.Backspace)
                    {
                        if ((session.SelectedBlock == null || session.Editing) && currentCursor > 0)
                        {
                            currentBuffer.Remove(currentCursor - 1, 1);
                            currentCursor--;
                        }
                    }
                    else if (key.Key == ConsoleKey.Delete)
                    {
                        if ((session.SelectedBlock == null || session.Editing) && currentCursor < currentBuffer.Length)
                            currentBuffer.Remove(currentCursor, 1);
                    }
                    else if (key.Key == ConsoleKey.End)
                    {
                        if ((session.SelectedBlock == null || session.Editing) && currentBuffer.Length > 0)
                            currentCursor = currentBuffer.Length;
                    }
                    else if (key.Key == ConsoleKey.Home)
                    {
                        if ((session.SelectedBlock == null || session.Editing) && currentBuffer.Length > 0)
                            currentCursor = 0;
                    }
                    else if (key.Key == ConsoleKey.Tab)
                    {
                        if (session.SelectedBlock == null || session.Editing)
                        {
                            // Write back current cursor before switching
                            if (session.ActiveField == 0) session.InputCursor = currentCursor;
                            else session.TicketCursor = currentCursor;

                            session.ActiveField = session.ActiveField == 0 ? 1 : 0;
                            // Place cursor at end of newly focused field
                            currentCursor = session.ActiveField == 0 ? session.InputBuffer.Length : session.TicketBuffer.Length;
                        }
                    }
                    else if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
                    {
                        if (session.SelectedBlock == null || session.Editing)
                        {
                            var ch = session.ActiveField == 1 ? char.ToUpperInvariant(key.KeyChar) : key.KeyChar;
                            currentBuffer.Insert(currentCursor, ch);
                            currentCursor++;
                        }
                    }

                    // Write back cursor position to the active field
                    if (session.ActiveField == 0) session.InputCursor = currentCursor;
                    else session.TicketCursor = currentCursor;

                    // Auto-scroll to keep selected block visible
                    if (session.SelectedBlock != null)
                    {
                        var sorted = schedule.Blocks.OrderBy(b => b.StartSlot).ToList();
                        var selIdx = sorted.FindIndex(b => b.StartSlot == session.SelectedBlock.StartSlot && b.Length == session.SelectedBlock.Length);
                        if (selIdx >= 0)
                        {
                            // When scrolled, "above" indicator takes 1 line
                            var visHeight = Math.Max(1, System.Console.WindowHeight - 10 - 2 - 1);
                            if (selIdx < session.LogScrollOffset)
                                session.LogScrollOffset = selIdx;
                            else if (selIdx >= session.LogScrollOffset + visHeight)
                                session.LogScrollOffset = selIdx - visHeight + 1;
                        }
                    }

                    // Clamp scroll offset after block additions/deletions
                    // When scrolled down, the "▲ more above" indicator takes 1 line,
                    // so only viewHeight-1 block rows are visible at the bottom.
                    var viewHeight = Math.Max(1, System.Console.WindowHeight - 10 - 2);
                    var maxOff = Math.Max(0, schedule.Count - (viewHeight - 1));
                    session.LogScrollOffset = Math.Clamp(session.LogScrollOffset, 0, maxOff);

                    view.Render(session);
                    ctx.Refresh();
                }
            });
            }
            finally
            {
                AnsiConsole.Cursor.Show();
            }
        });

        return 0;
    }
}
