using System.Reflection;
using System.Text;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Rendering;

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

            AnsiConsole.Live(layout).Start(ctx =>
            {
                UpdateLayout(layout, session);
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
                        UpdateLayout(layout, session);
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

                        UpdateLayout(layout, session);
                        ctx.Refresh();
                        continue;
                    }

                    if (key.Key == ConsoleKey.Q && key.Modifiers.HasFlag(ConsoleModifiers.Control))
                    {
                        confirming = true;
                        UpdateLayout(layout, session, confirming: true);
                        ctx.Refresh();
                        continue;
                    }

                    if (session.ShowHelp)
                    {
                        session.ShowHelp = false;
                        UpdateLayout(layout, session);
                        ctx.Refresh();
                        continue;
                    }

                    if (session.ShowTicketSummary)
                    {
                        if (key.Key == ConsoleKey.F3)
                        {
                            session.ShowTicketSummary = false;
                            UpdateLayout(layout, session);
                            ctx.Refresh();
                        }
                        continue;
                    }

                    if (key.Key == ConsoleKey.F3)
                    {
                        session.ShowTicketSummary = true;
                        UpdateLayout(layout, session);
                        ctx.Refresh();
                        continue;
                    }

                    if (key.KeyChar == '?' && session.SelectedBlock == null && !session.Editing && session.InputBuffer.Length == 0 && session.TicketBuffer.Length == 0)
                    {
                        session.ShowHelp = !session.ShowHelp;
                        UpdateLayout(layout, session);
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
                            UpdateLayout(layout, session, confirmingDelete: true);
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

                    UpdateLayout(layout, session);
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

    private static void UpdateLayout(Layout layout, PunchSession session, bool confirming = false, bool confirmingDelete = false)
    {
        var bookedBlocks = session.Blocks;
        var filePath = session.FilePath;
        var cursorSlot = session.CursorSlot;
        var selectionLength = session.SelectionLength;
        var selectedBlock = session.SelectedBlock;
        var editing = session.Editing;
        var showHelp = session.ShowHelp;
        var showTicketSummary = session.ShowTicketSummary;
        var inputBuffer = session.InputBuffer;
        var inputCursor = session.InputCursor;
        var ticketBuffer = session.TicketBuffer;
        var ticketCursor = session.TicketCursor;
        var activeField = session.ActiveField;
        var logScrollOffset = session.LogScrollOffset;

        // Timeline pane
        var consoleWidth = System.Console.WindowWidth;
        var barWidth = Math.Max(1, consoleWidth - 4); // account for panel border + padding
        var endSlot = cursorSlot + selectionLength;
        var timeLabel = SlotTime.FormatRange(cursorSlot, endSlot);

        var sorted = bookedBlocks.OrderBy(b => b.StartSlot).ToList();

        // Build the bar line: mark booked slots, then overlay selection
        // Fixed pixels-per-slot for uniform block widths
        var pixelsPerSlot = Math.Max(1, barWidth / 96);
        var totalBarWidth = pixelsPerSlot * 96;
        var pixelState = new int[totalBarWidth]; // 0=free, 1=booked, 2=selected, 3=selected-existing
        var pixelBlockIndex = new int[totalBarWidth];
        Array.Fill(pixelBlockIndex, -1);
        for (var blockIdx = 0; blockIdx < sorted.Count; blockIdx++)
        {
            var block = sorted[blockIdx];
            var bStart = block.StartSlot * pixelsPerSlot;
            var bEndExcl = (block.StartSlot + block.Length) * pixelsPerSlot;
            bStart = Math.Clamp(bStart, 0, totalBarWidth);
            bEndExcl = Math.Clamp(bEndExcl, bStart, totalBarWidth);
            for (var px = bStart; px < bEndExcl; px++)
            {
                pixelState[px] = 1;
                pixelBlockIndex[px] = blockIdx;
            }
        }

        var selStartPos = cursorSlot * pixelsPerSlot;
        var selEndExcl = endSlot * pixelsPerSlot;
        if (selEndExcl == selStartPos) selEndExcl = selStartPos + 1;
        selStartPos = Math.Clamp(selStartPos, 0, totalBarWidth - 1);
        var selEndPos = Math.Clamp(selEndExcl - 1, selStartPos, totalBarWidth - 1);
        // State 3 = selected existing block (cyan), State 2 = free selection (yellow)
        var selPixelState = selectedBlock != null ? 3 : 2;
        for (var i = selStartPos; i <= selEndPos; i++)
            pixelState[i] = selPixelState;

        // Build hour labels line
        var labelChars = new char[totalBarWidth];
        Array.Fill(labelChars, ' ');
        var hourMarkers = new[] { 0, 6, 12, 18, 24 };
        foreach (var h in hourMarkers)
        {
            var pos = h * 4 * pixelsPerSlot;
            var label = h == 12 ? "12pm" : h < 12 ? $"{h}am" : $"{h - 12}pm";
            if (h == 0 || h == 24) label = "12am";
            // Right-align the end marker so it doesn't overflow
            if (h == 24) pos = totalBarWidth - label.Length;
            for (var i = 0; i < label.Length && pos + i < totalBarWidth; i++)
                labelChars[pos + i] = label[i];
        }

        // Position the time label above the selection midpoint
        var midPos = (selStartPos + selEndPos) / 2;
        var timeLabelStart = Math.Max(0, Math.Min(midPos - timeLabel.Length / 2, totalBarWidth - timeLabel.Length));
        var topLine = new string(' ', timeLabelStart) + timeLabel;

        // Build bar markup with colored segments (alternating colors for adjacent blocks)
        var barMarkup = new StringBuilder();
        var currentState = -1;
        var currentBlockIndex = -1;
        for (var i = 0; i < totalBarWidth; i++)
        {
            var needNewTag = pixelState[i] != currentState ||
                             (pixelState[i] == 1 && pixelBlockIndex[i] != currentBlockIndex);
            if (needNewTag)
            {
                if (currentState >= 0) barMarkup.Append("[/]");
                currentState = pixelState[i];
                currentBlockIndex = pixelBlockIndex[i];
                barMarkup.Append(currentState switch
                {
                    1 => currentBlockIndex >= 0 && currentBlockIndex < sorted.Count && sorted[currentBlockIndex].IsUnpaid
                            ? "[grey50]"
                            : currentBlockIndex % 2 == 0 ? "[orangered1]" : "[orange3]",
                    2 => "[bold yellow]",
                    3 => "[bold white]",
                    _ => "[dim]"
                });
            }
            barMarkup.Append(currentState switch
            {
                0 => '─',
                3 => '▒',
                _ => '█'
            });
        }
        if (currentState >= 0) barMarkup.Append("[/]");

        // Center the bar within the panel width
        var pad = Math.Max(0, (barWidth - totalBarWidth) / 2);
        var padStr = new string(' ', pad);

        var timelineContent = new Rows(
            new Markup($"[bold]{Markup.Escape(padStr + topLine)}[/]"),
            new Markup(padStr + barMarkup.ToString()),
            new Markup($"[dim]{Markup.Escape(padStr + new string(labelChars))}[/]"));

        layout["Timeline"].Update(
            new Panel(timelineContent)
                .Header("Timeline")
                .Expand()
                .Border(BoxBorder.Rounded));

        // Messages pane: show booked blocks sorted chronologically with scrolling
        var consoleHeight = System.Console.WindowHeight;
        var messagesHeight = Math.Max(1, consoleHeight - 10 - 2); // 10 = fixed panes (5+4+1), 2 = panel border
        var sortedBlocks = sorted;

        // Clamp scroll offset to valid range and reserve lines for scroll indicators
        var availableLines = messagesHeight;
        var clampedOffset = Math.Clamp(logScrollOffset, 0, Math.Max(0, sortedBlocks.Count - 1));

        var hasMoreAbove = clampedOffset > 0;
        if (hasMoreAbove) availableLines--;

        var hasMoreBelow = clampedOffset + availableLines < sortedBlocks.Count;
        if (hasMoreBelow) availableLines--;

        availableLines = Math.Max(1, availableLines);
        // Re-clamp offset so we don't scroll past the end
        var maxScrollOffset = Math.Max(0, sortedBlocks.Count - availableLines);
        clampedOffset = Math.Min(clampedOffset, maxScrollOffset);
        // Recalculate indicators after clamping
        hasMoreAbove = clampedOffset > 0;
        hasMoreBelow = clampedOffset + availableLines < sortedBlocks.Count;

        var visibleBlocks = sortedBlocks.Skip(clampedOffset).Take(availableLines).ToList();

        IRenderable messagesContent;
        if (visibleBlocks.Count == 0)
        {
            messagesContent = new Markup("[dim]No entries yet. Select a time range and press Enter.[/]");
        }
        else
        {
            var renderables = new List<IRenderable>();
            if (hasMoreAbove)
                renderables.Add(new Markup($"[dim]  ▲ {clampedOffset} more above (PgUp)[/]"));
            foreach (var b in visibleBlocks)
            {
                var timeRange = SlotTime.FormatRange(b.StartSlot, b.StartSlot + b.Length);
                var escaped = Markup.Escape(b.Label);
                var isSelected = selectedBlock != null && b.StartSlot == selectedBlock.StartSlot && b.Length == selectedBlock.Length;
                var blockIdx = sorted.IndexOf(b);
                var squareColor = isSelected
                    ? "white"
                    : b.IsUnpaid
                        ? "grey50"
                        : blockIdx % 2 == 0 ? "orangered1" : "orange3";
                var durationText = Duration.Humanize(b.Length * 15);
                var ticketDisplay = string.IsNullOrEmpty(b.Ticket) ? "" : $"[cyan]{Markup.Escape(b.Ticket)}[/] ";
                renderables.Add(new Markup($"[{squareColor}]■[/] [bold]{timeRange}[/] {ticketDisplay}{escaped} [dim grey]{durationText}[/]"));
            }
            if (hasMoreBelow)
            {
                var belowCount = sortedBlocks.Count - clampedOffset - availableLines;
                renderables.Add(new Markup($"[dim]  ▼ {belowCount} more below (PgDn)[/]"));
            }
            messagesContent = new Rows(renderables);
        }

        if (showHelp)
        {
            var version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
            var titleLine = new Markup($"[bold][red]p[/][orangered1]u[/][darkorange]n[/][orange3]c[/][orange1]h[/][/] [dim]v{Markup.Escape(version)}[/]");
            var helpText = new Markup(
                "[bold]Left/Right[/]  Move cursor / Jump between blocks\n" +
                "[bold]Up/Down[/]     Resize selection\n" +
                "[bold]PgUp/PgDn[/]   Scroll time log\n" +
                "[bold]Enter[/]       Log time entry\n" +
                "[bold]Tab[/]         Switch input field\n" +
                "[bold]Ctrl+E[/]      Edit selected entry\n" +
                "[bold]Ctrl+D[/]      Delete selected entry\n" +
                "[bold]Ctrl+Q, Q[/]   Quit\n" +
                "[bold]?[/]           Toggle this help\n" +
                "[bold]F3[/]          Ticket summary");
            var helpContent = new Rows(
                Align.Center(titleLine),
                new Text(" "),
                helpText);
            var helpPanel = new Panel(helpContent)
                .Border(BoxBorder.Rounded)
                .Expand();
            layout["Messages"].Update(
                new Panel(Align.Center(helpPanel, VerticalAlignment.Middle))
                    .Expand()
                    .NoBorder());
        }
        else if (showTicketSummary)
        {
            var ticketGroups = bookedBlocks
                .GroupBy(b => string.IsNullOrEmpty(b.Ticket) ? "" : b.Ticket)
                .Select(g => new { Ticket = g.Key, TotalMinutes = g.Sum(b => b.Length * 15) })
                .OrderBy(g => g.Ticket == "" ? 1 : 0)
                .ThenBy(g => g.Ticket)
                .ToList();

            var summaryLines = new List<IRenderable>();
            foreach (var g in ticketGroups)
            {
                var dur = Duration.Humanize(g.TotalMinutes);
                var visibleName = g.Ticket == "" ? "Other" : g.Ticket;
                var paddedName = visibleName.PadRight(20);
                var ticketLabel = g.Ticket == "" ? $"[dim]{paddedName}[/]" : $"[cyan]{Markup.Escape(paddedName)}[/]";
                summaryLines.Add(new Markup($"  {ticketLabel} {dur}"));
            }

            var totalDur = Duration.HumanizeTotal(bookedBlocks.Sum(b => b.Length * 15));
            summaryLines.Add(new Markup($"  [dim]{new string('─', 28)}[/]"));
            summaryLines.Add(new Markup($"  [bold]{"Total".PadRight(20)} {totalDur}[/]"));

            var summaryPanel = new Panel(new Rows(summaryLines))
                .Header("Ticket Summary")
                .Border(BoxBorder.Rounded)
                .Expand();
            layout["Messages"].Update(
                new Panel(Align.Center(summaryPanel, VerticalAlignment.Middle))
                    .Expand()
                    .NoBorder());
        }
        else
        {
            layout["Messages"].Update(
                new Panel(messagesContent)
                    .Header("Time Logged")
                    .Expand()
                    .Border(BoxBorder.Rounded));
        }

        // Input pane
        if (confirming)
        {
            layout["Input"].Update(
                new Panel(new Markup("[bold yellow]Press Q again to quit[/]"))
                    .Expand()
                    .Border(BoxBorder.Rounded));
        }
        else if (confirmingDelete)
        {
            layout["Input"].Update(
                new Panel(new Markup("[bold yellow]Press D again to delete[/]"))
                    .Expand()
                    .Border(BoxBorder.Rounded));
        }
        else if (selectedBlock != null && editing)
        {
            var descLine = RenderFieldLine("Description", inputBuffer, inputCursor, activeField == 0);
            var tickLine = RenderFieldLine("Ticket", ticketBuffer, ticketCursor, activeField == 1);
            layout["Input"].Update(
                new Panel(new Rows(new Markup(descLine), new Markup(tickLine)))
                    .Header("Input [cyan](editing)[/]")
                    .Expand()
                    .Border(BoxBorder.Rounded));
        }
        else if (selectedBlock != null)
        {
            var labelText = Markup.Escape(selectedBlock.Label);
            var ticketText = Markup.Escape(selectedBlock.Ticket);
            var descLine = $"[bold]Description:[/] {labelText}";
            var tickLine = $"[bold]Ticket:[/]      {(string.IsNullOrEmpty(ticketText) ? "[dim]none[/]" : ticketText)}";
            layout["Input"].Update(
                new Panel(new Rows(new Markup(descLine), new Markup(tickLine)))
                    .Header("Input")
                    .Expand()
                    .Border(BoxBorder.Rounded));
        }
        else
        {
            var descLine = RenderFieldLine("Description", inputBuffer, inputCursor, activeField == 0);
            var tickLine = RenderFieldLine("Ticket", ticketBuffer, ticketCursor, activeField == 1);
            layout["Input"].Update(
                new Panel(new Rows(new Markup(descLine), new Markup(tickLine)))
                    .Header("Input")
                    .Expand()
                    .Border(BoxBorder.Rounded));
        }

        // Status bar
        var totalMinutesAll = bookedBlocks.Where(b => !b.IsUnpaid).Sum(b => b.Length * 15);
        var totalFormatted = Duration.HumanizeTotal(totalMinutesAll);
        var percent = totalMinutesAll * 100 / 480;
        var statusLeftPlain = $"  {filePath}  ?=help F3=summary";
        var statusRight = $"{totalFormatted}    {percent}% of 8h  ";
        var padding = Math.Max(0, consoleWidth - statusLeftPlain.Length - statusRight.Length);
        var statusBar = $"[white on orangered1]  {Markup.Escape(filePath)}  [bold yellow]?=help F3=summary[/]{new string(' ', padding)}[bold white]{Markup.Escape(statusRight)}[/][/]";
        layout["StatusBar"].Update(new Markup(statusBar));
    }

    private static string RenderFieldLine(string fieldName, StringBuilder buffer, int cursor, bool isActive)
    {
        var paddedName = fieldName.PadRight(11);
        if (isActive)
        {
            var text = buffer.ToString();
            var beforeCursor = Markup.Escape(text[..cursor]);
            var cursorChar = cursor < text.Length ? Markup.Escape(text[cursor].ToString()) : " ";
            var afterCursor = cursor < text.Length ? Markup.Escape(text[(cursor + 1)..]) : "";
            return $"[bold]{Markup.Escape(paddedName)}:[/] {beforeCursor}[invert]{cursorChar}[/]{afterCursor}";
        }
        else
        {
            var escaped = Markup.Escape(buffer.ToString());
            var display = string.IsNullOrEmpty(escaped) ? "[dim]empty[/]" : $"[dim]{escaped}[/]";
            return $"[dim]{Markup.Escape(paddedName)}:[/] {display}";
        }
    }
}
