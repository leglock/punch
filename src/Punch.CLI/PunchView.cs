using System.Reflection;
using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Punch.CLI;

// Renders the TUI panes from a PunchSession into the Spectre layout. Pure
// presentation: it reads session state and never mutates it.
internal sealed class PunchView
{
    private readonly Layout _layout;

    public PunchView(Layout layout)
    {
        _layout = layout;
    }

    public void Render(PunchSession session, bool confirming = false, bool confirmingDelete = false)
    {
        RenderTimeline(session);
        RenderMessages(session);
        RenderInput(session, confirming, confirmingDelete);
        RenderStatusBar(session);
    }

    private void RenderTimeline(PunchSession session)
    {
        var cursorSlot = session.CursorSlot;
        var selectionLength = session.SelectionLength;
        var selectedBlock = session.SelectedBlock;

        var consoleWidth = System.Console.WindowWidth;
        var barWidth = Math.Max(1, consoleWidth - 4); // account for panel border + padding
        var endSlot = cursorSlot + selectionLength;
        var timeLabel = SlotTime.FormatRange(cursorSlot, endSlot);

        var sorted = session.Blocks.OrderBy(b => b.StartSlot).ToList();

        // Use a fixed pixels-per-slot so every 15-min segment is the same width.
        // On terminals narrower than 96 columns barWidth/96 rounds to 0, so fall
        // back to proportional mapping to prevent bar overflow.
        var pixelsPerSlot = barWidth / 96;
        int SlotToPixel(int slot) => pixelsPerSlot > 0
            ? slot * pixelsPerSlot
            : (int)((long)slot * barWidth / 96);
        var totalBarWidth = pixelsPerSlot > 0 ? pixelsPerSlot * 96 : barWidth;
        var pixelState = new int[totalBarWidth]; // 0=free, 1=booked, 2=selected, 3=selected-existing
        var pixelBlockIndex = new int[totalBarWidth];
        Array.Fill(pixelBlockIndex, -1);
        for (var blockIdx = 0; blockIdx < sorted.Count; blockIdx++)
        {
            var block = sorted[blockIdx];
            var bStart = SlotToPixel(block.StartSlot);
            var bEndExcl = SlotToPixel(block.StartSlot + block.Length);
            bStart = Math.Clamp(bStart, 0, totalBarWidth);
            bEndExcl = Math.Clamp(bEndExcl, bStart, totalBarWidth);
            for (var px = bStart; px < bEndExcl; px++)
            {
                pixelState[px] = 1;
                pixelBlockIndex[px] = blockIdx;
            }
        }

        var selStartPos = SlotToPixel(cursorSlot);
        var selEndExcl = SlotToPixel(endSlot);
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
            var pos = SlotToPixel(h * 4);
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

        _layout["Timeline"].Update(
            new Panel(timelineContent)
                .Header("Timeline")
                .Expand()
                .Border(BoxBorder.Rounded));
    }

    private void RenderMessages(PunchSession session)
    {
        if (session.ShowHelp)
            // Split like the picker/summary so the time log stays visible on the
            // left while help occupies the right half.
            _layout["Messages"].Update(new Layout("MessagesSplit")
                .SplitColumns(
                    new Layout("Log").Update(BuildLogPanel(session)),
                    new Layout("Help").Update(BuildHelpPanel())));
        else if (session.ShowTicketPicker)
            // Split the content area so the time log stays visible on the left
            // while the picker occupies the right half.
            _layout["Messages"].Update(new Layout("MessagesSplit")
                .SplitColumns(
                    new Layout("Log").Update(BuildLogPanel(session)),
                    new Layout("Picker").Update(BuildTicketPickerPanel(session))));
        else if (session.ShowTicketSummary)
            // Split like the picker so the time log stays visible on the left
            // while the summary occupies the right half.
            _layout["Messages"].Update(new Layout("MessagesSplit")
                .SplitColumns(
                    new Layout("Log").Update(BuildLogPanel(session)),
                    new Layout("Summary").Update(BuildTicketSummaryPanel(session))));
        else
            _layout["Messages"].Update(BuildLogPanel(session));
    }

    private static IRenderable BuildLogPanel(PunchSession session)
    {
        var selectedBlock = session.SelectedBlock;
        var sorted = session.Blocks.OrderBy(b => b.StartSlot).ToList();

        // Messages pane: show booked blocks sorted chronologically with scrolling
        var consoleHeight = System.Console.WindowHeight;
        var messagesHeight = Math.Max(1, consoleHeight - 10 - 2); // 10 = fixed panes (5+4+1), 2 = panel border

        // Clamp scroll offset to valid range and reserve lines for scroll indicators
        var availableLines = messagesHeight;
        var clampedOffset = Math.Clamp(session.LogScrollOffset, 0, Math.Max(0, sorted.Count - 1));

        var hasMoreAbove = clampedOffset > 0;
        if (hasMoreAbove) availableLines--;

        var hasMoreBelow = clampedOffset + availableLines < sorted.Count;
        if (hasMoreBelow) availableLines--;

        availableLines = Math.Max(1, availableLines);
        // Re-clamp offset so we don't scroll past the end
        var maxScrollOffset = Math.Max(0, sorted.Count - availableLines);
        clampedOffset = Math.Min(clampedOffset, maxScrollOffset);
        // Recalculate indicators after clamping
        hasMoreAbove = clampedOffset > 0;
        hasMoreBelow = clampedOffset + availableLines < sorted.Count;

        var visibleBlocks = sorted.Skip(clampedOffset).Take(availableLines).ToList();

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
                var belowCount = sorted.Count - clampedOffset - availableLines;
                renderables.Add(new Markup($"[dim]  ▼ {belowCount} more below (PgDn)[/]"));
            }
            messagesContent = new Rows(renderables);
        }

        return new Panel(messagesContent)
            .Header("Time Logged")
            .Expand()
            .Border(BoxBorder.Rounded);
    }

    private static IRenderable BuildHelpPanel()
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
            "[bold]F3, Ctrl+T[/]   Ticket summary\n" +
            "[bold]F4, Ctrl+P[/]   Pick ticket for entry");
        var helpContent = new Rows(
            titleLine,
            new Text(" "),
            helpText,
            new Text(" "),
            new Markup("[dim]Esc/? cancel[/]"));
        // A single Expand-ed panel that fills the region height exactly, matching
        // the log panel beside it.
        return new Panel(helpContent)
            .Header("Help")
            .Border(BoxBorder.Rounded)
            .Expand();
    }

    private static IRenderable BuildTicketSummaryPanel(PunchSession session)
    {
        var blocks = session.Blocks;
        var ticketGroups = blocks
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

        var billableMinutes = blocks.Where(b => !b.IsUnpaid).Sum(b => b.Length * 15);
        var unbillableMinutes = blocks.Where(b => b.IsUnpaid).Sum(b => b.Length * 15);
        var totalDur = Duration.HumanizeTotal(blocks.Sum(b => b.Length * 15));
        summaryLines.Add(new Markup($"  [dim]{new string('─', 28)}[/]"));
        summaryLines.Add(new Markup($"  {"Billable".PadRight(20)} {Duration.Humanize(billableMinutes)}"));
        summaryLines.Add(new Markup($"  [grey50]{"Unbillable".PadRight(20)} {Duration.Humanize(unbillableMinutes)}[/]"));
        summaryLines.Add(new Markup($"  [bold]{"Total".PadRight(20)} {totalDur}[/]"));

        summaryLines.Add(new Text(" "));
        summaryLines.Add(new Markup("[dim]Esc/F3 close[/]"));

        // A single Expand-ed panel that fills the region height exactly, matching
        // the log panel beside it.
        return new Panel(new Rows(summaryLines))
            .Header("Ticket Summary")
            .Border(BoxBorder.Rounded)
            .Expand();
    }

    // Clamps text to a maximum display width, appending an ellipsis when cut.
    private static string Truncate(string text, int maxWidth)
    {
        if (maxWidth <= 0 || text.Length <= maxWidth)
            return text;
        return maxWidth == 1 ? "…" : text[..(maxWidth - 1)] + "…";
    }

    private static IRenderable BuildTicketPickerPanel(PunchSession session)
    {
        var lines = new List<IRenderable>();
        if (session.Tickets.Count == 0)
        {
            lines.Add(new Markup("[dim]No tickets found.[/]"));
            lines.Add(new Text(" "));
            lines.Add(new Markup("[dim]Create [/][cyan]~/.punch/tickets.txt[/][dim] with one ticket per line,[/]"));
            lines.Add(new Markup("[dim]tab- or comma-delimited as [/][cyan]TICKET<tab|,>Title[/][dim].[/]"));
        }
        else
        {
            // Window the list around the cursor so long lists scroll instead of
            // overflowing the pane. Budget: panel interior (messages region minus
            // its border) minus the footer block (blank + hint = 2). Rows are
            // truncated to one physical line below so the budget holds exactly.
            var consoleHeight = System.Console.WindowHeight;
            var interior = Math.Max(3, consoleHeight - 10 - 2);
            var maxRows = Math.Max(1, interior - 2);

            // The picker occupies the right half of the content width; keep each
            // row to a single line so wrapping never eats into the row budget.
            var textWidth = Math.Max(8, System.Console.WindowWidth / 2 - 4);

            var count = session.Tickets.Count;
            var cursor = session.TicketPickerCursor;

            // The ▲/▼ indicators each consume a row, but whether they appear
            // depends on the window position — which depends on how many rows fit.
            // Iterate to a fixed point so the indicator lines never overshoot the
            // budget (an overshoot of one would clip the footer).
            var visibleRows = Math.Min(count, maxRows);
            var offset = 0;
            var hasMoreAbove = false;
            var hasMoreBelow = false;
            for (var iter = 0; iter < 4; iter++)
            {
                offset = count <= visibleRows
                    ? 0
                    : Math.Clamp(cursor - visibleRows / 2, 0, count - visibleRows);
                hasMoreAbove = offset > 0;
                hasMoreBelow = offset + visibleRows < count;
                var fit = Math.Max(1, Math.Min(count,
                    maxRows - (hasMoreAbove ? 1 : 0) - (hasMoreBelow ? 1 : 0)));
                if (fit == visibleRows)
                    break;
                visibleRows = fit;
            }

            if (hasMoreAbove)
                lines.Add(new Markup($"  [dim]▲ {offset} more[/]"));
            for (var i = offset; i < offset + visibleRows && i < count; i++)
            {
                var t = session.Tickets[i];
                // Prefix is 4 chars ("  > " / "    "); reserve the rest for the
                // ticket + two-space gap + title, truncating the title to fit.
                var title = Truncate(t.Title, Math.Max(1, textWidth - 4 - t.Ticket.Length - 2));
                var ticket = Markup.Escape(t.Ticket);
                var titleEsc = Markup.Escape(title);
                if (i == cursor)
                    lines.Add(new Markup($"  [bold yellow]> {ticket}[/]  {titleEsc}"));
                else
                    lines.Add(new Markup($"    [cyan]{ticket}[/]  [dim]{titleEsc}[/]"));
            }
            if (hasMoreBelow)
                lines.Add(new Markup($"  [dim]▼ {count - offset - visibleRows} more[/]"));
        }
        lines.Add(new Text(" "));
        lines.Add(new Markup("[dim]↑/↓ select · Enter assign · Esc/F4 cancel[/]"));

        // A single Expand-ed panel that fills the region height exactly, matching
        // the log panel beside it so the footer never gets clipped.
        return new Panel(new Rows(lines))
            .Header("Pick Ticket")
            .Border(BoxBorder.Rounded)
            .Expand();
    }

    private void RenderInput(PunchSession session, bool confirming, bool confirmingDelete)
    {
        var selectedBlock = session.SelectedBlock;
        var editing = session.Editing;

        if (confirming)
        {
            _layout["Input"].Update(
                new Panel(new Markup("[bold yellow]Press Q again to quit[/]"))
                    .Expand()
                    .Border(BoxBorder.Rounded));
        }
        else if (confirmingDelete)
        {
            _layout["Input"].Update(
                new Panel(new Markup("[bold yellow]Press D again to delete[/]"))
                    .Expand()
                    .Border(BoxBorder.Rounded));
        }
        else if (selectedBlock != null && editing)
        {
            var descLine = RenderFieldLine("Description", session.InputBuffer, session.InputCursor, session.ActiveField == 0);
            var tickLine = RenderFieldLine("Ticket", session.TicketBuffer, session.TicketCursor, session.ActiveField == 1);
            _layout["Input"].Update(
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
            _layout["Input"].Update(
                new Panel(new Rows(new Markup(descLine), new Markup(tickLine)))
                    .Header("Input")
                    .Expand()
                    .Border(BoxBorder.Rounded));
        }
        else
        {
            var descLine = RenderFieldLine("Description", session.InputBuffer, session.InputCursor, session.ActiveField == 0);
            var tickLine = RenderFieldLine("Ticket", session.TicketBuffer, session.TicketCursor, session.ActiveField == 1);
            _layout["Input"].Update(
                new Panel(new Rows(new Markup(descLine), new Markup(tickLine)))
                    .Header("Input")
                    .Expand()
                    .Border(BoxBorder.Rounded));
        }
    }

    private void RenderStatusBar(PunchSession session)
    {
        var consoleWidth = System.Console.WindowWidth;
        var filePath = session.FilePath;
        var totalMinutesAll = session.Blocks.Where(b => !b.IsUnpaid).Sum(b => b.Length * 15);
        var totalFormatted = Duration.HumanizeTotal(totalMinutesAll);
        var statusLeftPlain = $"  {filePath}  ?=help F3=summary F4=tickets";
        string statusRight;
        if (session.TargetHours > 0)
        {
            var targetMinutes = session.TargetHours * 60;
            var percent = totalMinutesAll * 100 / targetMinutes;
            statusRight = $"{totalFormatted}    {percent}% of {session.TargetHours}h  ";
        }
        else
        {
            statusRight = $"{totalFormatted}  ";
        }
        var padding = Math.Max(0, consoleWidth - statusLeftPlain.Length - statusRight.Length);
        var statusBar = $"[white on orangered1]  {Markup.Escape(filePath)}  [bold yellow]?=help F3=summary F4=tickets[/]{new string(' ', padding)}[bold white]{Markup.Escape(statusRight)}[/][/]";
        _layout["StatusBar"].Update(new Markup(statusBar));
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
