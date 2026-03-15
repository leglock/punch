using System.ComponentModel;
using System.Reflection;
using System.Text;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Rendering;

namespace Punch.CLI;

class Program
{
    static int Main(string[] args)
    {
        var app = new CommandApp<PunchCommand>();
        app.Configure(config =>
        {
            config.SetApplicationName("punch");
        });

        return app.Run(args);
    }
}

internal sealed class PunchCommandSettings : CommandSettings
{
    [Description("Show version information")]
    [CommandOption("-v|--version")]
    public bool Version { get; set; }
}

internal sealed record TimeBlock(int StartSlot, int Length, string Label);

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

        var inputBuffer = new StringBuilder();
        var cursorSlot = 0; // 0–95: 96 quarter-hour slots (00:00–23:45)
        var selectionLength = 1; // number of 15-min slots selected (min 1)
        var bookedBlocks = new List<TimeBlock>();
        var occupied = new bool[96];
        TimeBlock? selectedBlock = null;
        var editing = false;

        AnsiConsole.AlternateScreen(() =>
        {
            AnsiConsole.Cursor.Hide();

            var layout = new Layout("Root")
                .SplitRows(
                    new Layout("Timeline").Size(5),
                    new Layout("Messages").Ratio(4),
                    new Layout("Input").Ratio(1),
                    new Layout("Footer").Size(1),
                    new Layout("StatusBar").Size(1));

            AnsiConsole.Live(layout).Start(ctx =>
            {
                UpdateLayout(layout, bookedBlocks, inputBuffer, cursorSlot: cursorSlot, selectionLength: selectionLength, occupied: occupied, selectedBlock: selectedBlock, editing: editing);
                ctx.Refresh();

                var confirming = false;

                while (true)
                {
                    var key = System.Console.ReadKey(true);

                    if (confirming)
                    {
                        if (key.KeyChar is 'y' or 'Y')
                            break;

                        confirming = false;
                        UpdateLayout(layout, bookedBlocks, inputBuffer, cursorSlot: cursorSlot, selectionLength: selectionLength, occupied: occupied, selectedBlock: selectedBlock, editing: editing);
                        ctx.Refresh();
                        continue;
                    }

                    if (key.Key == ConsoleKey.Q && key.Modifiers.HasFlag(ConsoleModifiers.Control))
                    {
                        confirming = true;
                        UpdateLayout(layout, bookedBlocks, inputBuffer, confirming: true, cursorSlot: cursorSlot, selectionLength: selectionLength, occupied: occupied, selectedBlock: selectedBlock, editing: editing);
                        ctx.Refresh();
                        continue;
                    }

                    if (key.Key == ConsoleKey.LeftArrow)
                    {
                        if (selectedBlock != null)
                        {
                            // On an existing block: jump to slot just before it
                            var target = selectedBlock.StartSlot - 1;
                            if (target >= 0)
                            {
                                var adj = FindBlockAt(target, bookedBlocks);
                                if (adj != null)
                                {
                                    cursorSlot = adj.StartSlot;
                                    selectionLength = adj.Length;
                                    selectedBlock = adj;
                                }
                                else
                                {
                                    cursorSlot = target;
                                    selectionLength = 1;
                                    selectedBlock = null;
                                }
                            }
                        }
                        else if (cursorSlot > 0)
                        {
                            // Free selection: slide left by 1, keeping length
                            var newStart = cursorSlot - 1;
                            var adj = FindBlockAt(newStart, bookedBlocks);
                            if (adj != null)
                            {
                                cursorSlot = adj.StartSlot;
                                selectionLength = adj.Length;
                                selectedBlock = adj;
                            }
                            else if (!occupied[newStart])
                            {
                                cursorSlot = newStart;
                            }
                        }
                        editing = false;
                        inputBuffer.Clear();
                    }
                    else if (key.Key == ConsoleKey.RightArrow)
                    {
                        if (selectedBlock != null)
                        {
                            // On an existing block: jump to slot just after it
                            var target = selectedBlock.StartSlot + selectedBlock.Length;
                            if (target < 96)
                            {
                                var adj = FindBlockAt(target, bookedBlocks);
                                if (adj != null)
                                {
                                    cursorSlot = adj.StartSlot;
                                    selectionLength = adj.Length;
                                    selectedBlock = adj;
                                }
                                else
                                {
                                    cursorSlot = target;
                                    selectionLength = 1;
                                    selectedBlock = null;
                                }
                            }
                        }
                        else if (cursorSlot + selectionLength < 96)
                        {
                            // Free selection: slide right by 1, keeping length
                            var newEnd = cursorSlot + selectionLength; // the slot that would become the new last slot
                            var adj = FindBlockAt(newEnd, bookedBlocks);
                            if (adj != null)
                            {
                                cursorSlot = adj.StartSlot;
                                selectionLength = adj.Length;
                                selectedBlock = adj;
                            }
                            else if (!occupied[newEnd])
                            {
                                cursorSlot++;
                            }
                        }
                        editing = false;
                        inputBuffer.Clear();
                    }
                    else if (key.Key == ConsoleKey.UpArrow)
                    {
                        if (selectedBlock == null)
                        {
                            var newLen = selectionLength + 1;
                            if (cursorSlot + newLen <= 96 && !IsOverlapping(cursorSlot, newLen, occupied))
                                selectionLength = newLen;
                        }
                    }
                    else if (key.Key == ConsoleKey.DownArrow)
                    {
                        if (selectedBlock == null && selectionLength > 1)
                            selectionLength--;
                    }
                    else if (key.Key == ConsoleKey.D && key.Modifiers.HasFlag(ConsoleModifiers.Control))
                    {
                        // Ctrl+D: delete selected block
                        if (selectedBlock != null)
                        {
                            for (var s = selectedBlock.StartSlot; s < selectedBlock.StartSlot + selectedBlock.Length; s++)
                                occupied[s] = false;
                            bookedBlocks.Remove(selectedBlock);
                            selectedBlock = null;
                            selectionLength = 1;
                            editing = false;
                            inputBuffer.Clear();
                        }
                    }
                    else if (key.Key == ConsoleKey.E && key.Modifiers.HasFlag(ConsoleModifiers.Control))
                    {
                        // Ctrl+E: edit selected block's label
                        if (selectedBlock != null && !editing)
                        {
                            editing = true;
                            inputBuffer.Clear();
                            inputBuffer.Append(selectedBlock.Label);
                        }
                    }
                    else if (key.Key == ConsoleKey.Enter)
                    {
                        if (editing && selectedBlock != null)
                        {
                            // Save edited label
                            var newLabel = inputBuffer.ToString();
                            var idx = bookedBlocks.IndexOf(selectedBlock);
                            if (idx >= 0)
                            {
                                selectedBlock = selectedBlock with { Label = newLabel };
                                bookedBlocks[idx] = selectedBlock;
                            }
                            editing = false;
                            inputBuffer.Clear();
                        }
                        else if (selectedBlock == null && inputBuffer.Length > 0)
                        {
                            var label = inputBuffer.ToString();
                            var block = new TimeBlock(cursorSlot, selectionLength, label);
                            bookedBlocks.Add(block);
                            for (var s = block.StartSlot; s < block.StartSlot + block.Length; s++)
                                occupied[s] = true;

                            inputBuffer.Clear();

                            // Move cursor to next free position
                            selectionLength = 1;
                            while (cursorSlot < 96 && occupied[cursorSlot])
                                cursorSlot++;
                            if (cursorSlot >= 96)
                                cursorSlot = 95;
                        }
                    }
                    else if (key.Key == ConsoleKey.Backspace)
                    {
                        if ((selectedBlock == null || editing) && inputBuffer.Length > 0)
                            inputBuffer.Remove(inputBuffer.Length - 1, 1);
                    }
                    else if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
                    {
                        if (selectedBlock == null || editing)
                            inputBuffer.Append(key.KeyChar);
                    }

                    UpdateLayout(layout, bookedBlocks, inputBuffer, cursorSlot: cursorSlot, selectionLength: selectionLength, occupied: occupied, selectedBlock: selectedBlock, editing: editing);
                    ctx.Refresh();
                }
            });
        });

        return 0;
    }

    private static TimeBlock? FindBlockAt(int slot, List<TimeBlock> blocks)
    {
        return blocks.FirstOrDefault(b => slot >= b.StartSlot && slot < b.StartSlot + b.Length);
    }

    private static bool IsOverlapping(int start, int length, bool[] occupied)
    {
        for (var i = start; i < start + length && i < 96; i++)
            if (occupied[i])
                return true;
        return false;
    }

    private static void UpdateLayout(Layout layout, List<TimeBlock> bookedBlocks, StringBuilder inputBuffer, bool confirming = false, int cursorSlot = 0, int selectionLength = 1, bool[]? occupied = null, TimeBlock? selectedBlock = null, bool editing = false)
    {
        // Timeline pane
        var consoleWidth = System.Console.WindowWidth;
        var barWidth = Math.Max(1, consoleWidth - 4); // account for panel border + padding
        var startHours = cursorSlot / 4;
        var startMinutes = (cursorSlot % 4) * 15;
        var endSlot = cursorSlot + selectionLength;
        var endHours = endSlot / 4;
        var endMinutes = (endSlot % 4) * 15;
        var timeLabel = $"{startHours:D2}:{startMinutes:D2}\u2013{endHours:D2}:{endMinutes:D2}";

        // Build the bar line: mark booked slots, then overlay selection
        // Each pixel maps to a slot range; tag pixels as: 'free', 'booked', 'selected'
        var pixelState = new int[barWidth]; // 0=free, 1=booked, 2=selected, 3=selected-existing
        foreach (var block in bookedBlocks)
        {
            var bStart = (int)((double)block.StartSlot / 96 * barWidth);
            var bEndExcl = (int)((double)(block.StartSlot + block.Length) / 96 * barWidth);
            bStart = Math.Clamp(bStart, 0, barWidth);
            bEndExcl = Math.Clamp(bEndExcl, bStart, barWidth);
            if (bEndExcl == bStart) bEndExcl = bStart + 1; // at least 1 pixel
            for (var px = bStart; px < bEndExcl && px < barWidth; px++)
                pixelState[px] = 1;
        }

        var selStartPos = (int)((double)cursorSlot / 96 * barWidth);
        var selEndExcl = (int)((double)endSlot / 96 * barWidth);
        if (selEndExcl == selStartPos) selEndExcl = selStartPos + 1;
        selStartPos = Math.Clamp(selStartPos, 0, barWidth - 1);
        var selEndPos = Math.Clamp(selEndExcl - 1, selStartPos, barWidth - 1);
        // State 3 = selected existing block (cyan), State 2 = free selection (yellow)
        var selPixelState = selectedBlock != null ? 3 : 2;
        for (var i = selStartPos; i <= selEndPos; i++)
            pixelState[i] = selPixelState;

        // Build hour labels line
        var labelChars = new char[barWidth];
        Array.Fill(labelChars, ' ');
        var hourMarkers = new[] { 0, 6, 12, 18, 24 };
        foreach (var h in hourMarkers)
        {
            var pos = (int)((double)(h * 4) / 96 * barWidth);
            if (h == 24) pos = barWidth - 1;
            var label = h.ToString();
            for (var i = 0; i < label.Length && pos + i < barWidth; i++)
                labelChars[pos + i] = label[i];
        }

        // Position the time label above the selection midpoint
        var midPos = (selStartPos + selEndPos) / 2;
        var timeLabelStart = Math.Max(0, Math.Min(midPos - timeLabel.Length / 2, barWidth - timeLabel.Length));
        var topLine = new string(' ', timeLabelStart) + timeLabel;

        // Build bar markup with colored segments
        var barMarkup = new StringBuilder();
        var currentState = -1;
        for (var i = 0; i < barWidth; i++)
        {
            if (pixelState[i] != currentState)
            {
                if (currentState >= 0) barMarkup.Append("[/]");
                currentState = pixelState[i];
                barMarkup.Append(currentState switch
                {
                    1 => "[green]",
                    2 => "[bold yellow]",
                    3 => "[bold cyan]",
                    _ => "[dim]"
                });
            }
            barMarkup.Append(currentState == 0 ? '─' : '█');
        }
        if (currentState >= 0) barMarkup.Append("[/]");

        var timelineContent = new Rows(
            new Markup($"[bold]{Markup.Escape(topLine)}[/]"),
            new Markup(barMarkup.ToString()),
            new Markup($"[dim]{Markup.Escape(new string(labelChars))}[/]"));

        layout["Timeline"].Update(
            new Panel(timelineContent)
                .Header("[bold]Timeline[/]")
                .Expand()
                .Border(BoxBorder.Rounded));

        // Messages pane: show booked blocks sorted chronologically
        var consoleHeight = System.Console.WindowHeight;
        var messagesHeight = Math.Max(1, (int)(consoleHeight * 0.8) - 2); // account for panel border
        var sortedBlocks = bookedBlocks.OrderBy(b => b.StartSlot).ToList();
        var visibleBlocks = sortedBlocks.Count > messagesHeight
            ? sortedBlocks.Skip(sortedBlocks.Count - messagesHeight).ToList()
            : sortedBlocks;

        IRenderable messagesContent;
        if (visibleBlocks.Count == 0)
        {
            messagesContent = new Markup("[dim]No entries yet. Select a time range and press Enter.[/]");
        }
        else
        {
            var renderables = visibleBlocks.Select(b =>
            {
                var sh = b.StartSlot / 4;
                var sm = (b.StartSlot % 4) * 15;
                var es = b.StartSlot + b.Length;
                var eh = es / 4;
                var em = (es % 4) * 15;
                var escaped = Markup.Escape(b.Label);
                var isSelected = selectedBlock != null && b.StartSlot == selectedBlock.StartSlot && b.Length == selectedBlock.Length;
                var squareColor = isSelected ? "cyan" : "green";
                var totalMinutes = b.Length * 15;
                var durationText = totalMinutes >= 60
                    ? totalMinutes % 60 == 0
                        ? $"{totalMinutes / 60}h"
                        : $"{totalMinutes / 60}h {totalMinutes % 60}m"
                    : $"{totalMinutes}m";
                return (IRenderable)new Markup($"[{squareColor}]\u25a0[/] [bold]{sh:D2}:{sm:D2}\u2013{eh:D2}:{em:D2}[/] {escaped} [dim grey]{durationText}[/]");
            }).ToArray();
            messagesContent = new Rows(renderables);
        }

        layout["Messages"].Update(
            new Panel(messagesContent)
                .Header("[bold][red]p[/][orangered1]u[/][darkorange]n[/][orange3]c[/][orange1]h[/][/]")
                .Expand()
                .Border(BoxBorder.Rounded));

        // Input pane
        if (confirming)
        {
            layout["Input"].Update(
                new Panel(new Markup("[bold yellow]Are you sure you want to quit? [/][dim](y/n)[/]"))
                    .Expand()
                    .Border(BoxBorder.Rounded));
        }
        else if (selectedBlock != null && editing)
        {
            var inputText = Markup.Escape(inputBuffer.ToString());
            layout["Input"].Update(
                new Panel(new Markup($"[cyan]editing:[/] > {inputText}[blink]_[/]"))
                    .Expand()
                    .Border(BoxBorder.Rounded));
        }
        else if (selectedBlock != null)
        {
            var labelText = Markup.Escape(selectedBlock.Label);
            layout["Input"].Update(
                new Panel(new Markup($"[dim]selected:[/] {labelText}"))
                    .Expand()
                    .Border(BoxBorder.Rounded));
        }
        else
        {
            var inputText = Markup.Escape(inputBuffer.ToString());
            layout["Input"].Update(
                new Panel(new Markup($"> {inputText}[blink]_[/]"))
                    .Expand()
                    .Border(BoxBorder.Rounded));
        }

        // Footer
        var footerText = selectedBlock != null
            ? "[dim]Press [bold]← →[/] move | [bold]Ctrl+D[/] delete | [bold]Ctrl+E[/] edit | [bold]Ctrl+Q[/] quit[/]"
            : "[dim]Press [bold]← →[/] move | [bold]↑ ↓[/] resize | [bold]Enter[/] send | [bold]Ctrl+Q[/] quit[/]";
        layout["Footer"].Update(new Markup(footerText));

        // Status bar
        var totalMinutesAll = bookedBlocks.Sum(b => b.Length * 15);
        var totalHours = totalMinutesAll / 60;
        var totalMins = totalMinutesAll % 60;
        var totalFormatted = totalMins > 0 ? $"{totalHours}h {totalMins}m" : $"{totalHours}h 0m";
        var percent = totalMinutesAll * 100 / 480;
        var dateStr = DateTime.Now.ToString("yyyy-MM-dd");
        var statusLeft = $"  {dateStr}";
        var statusRight = $"{totalFormatted}    {percent}% of 8h  ";
        var padding = Math.Max(0, consoleWidth - statusLeft.Length - statusRight.Length);
        var statusPadded = statusLeft + new string(' ', padding) + statusRight;
        layout["StatusBar"].Update(new Markup($"[white on blue]{Markup.Escape(statusPadded)}[/]"));
    }
}
