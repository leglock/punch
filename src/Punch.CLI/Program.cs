using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Text.Json;
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

    [Description("Working date override (format: yyyy-MM-dd)")]
    [CommandOption("-d|--date")]
    public string? Date { get; set; }
}

internal sealed record TimeBlock(int StartSlot, int Length, string Label, string Ticket = "");

internal sealed class PunchData
{
    public List<TimeBlockDto> Blocks { get; set; } = new();
}

internal sealed class TimeBlockDto
{
    public int StartSlot { get; set; }
    public int Length { get; set; }
    public string Label { get; set; } = "";
    public string Ticket { get; set; } = "";
}

internal static class PunchStorage
{
    public static string GetDataDirectory()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".punch", "data");
    }

    public static string GetFilePath(DateOnly date)
    {
        return Path.Combine(GetDataDirectory(), $"{date:yyyy-MM-dd}.json");
    }

    public static string GetDisplayPath(DateOnly date)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var full = GetFilePath(date);
        return full.StartsWith(home) ? "~" + full[home.Length..] : full;
    }

    public static List<TimeBlock> Load(DateOnly date)
    {
        var path = GetFilePath(date);
        if (!File.Exists(path))
            return new List<TimeBlock>();

        try
        {
            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<PunchData>(json);
            if (data?.Blocks == null)
                return new List<TimeBlock>();

            var result = new List<TimeBlock>();
            var occupied = new bool[96];
            foreach (var dto in data.Blocks)
            {
                if (dto.StartSlot < 0 || dto.StartSlot >= 96 || dto.Length < 1 || dto.StartSlot + dto.Length > 96)
                    continue;

                var overlaps = false;
                for (var s = dto.StartSlot; s < dto.StartSlot + dto.Length; s++)
                {
                    if (occupied[s]) { overlaps = true; break; }
                }
                if (overlaps) continue;

                for (var s = dto.StartSlot; s < dto.StartSlot + dto.Length; s++)
                    occupied[s] = true;

                result.Add(new TimeBlock(dto.StartSlot, dto.Length, dto.Label, dto.Ticket));
            }
            return result;
        }
        catch
        {
            return new List<TimeBlock>();
        }
    }

    public static void Save(DateOnly date, List<TimeBlock> blocks)
    {
        var dir = GetDataDirectory();
        Directory.CreateDirectory(dir);

        var data = new PunchData
        {
            Blocks = blocks.Select(b => new TimeBlockDto
            {
                StartSlot = b.StartSlot,
                Length = b.Length,
                Label = b.Label,
                Ticket = b.Ticket
            }).ToList()
        };

        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(GetFilePath(date), json);
    }
}

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
        var inputBuffer = new StringBuilder();
        var inputCursor = 0;
        var ticketBuffer = new StringBuilder();
        var ticketCursor = 0;
        var activeField = 0; // 0 = Description, 1 = Ticket
        var selectionLength = 1; // number of 15-min slots selected (min 1)
        var bookedBlocks = PunchStorage.Load(workingDate);
        var occupied = new bool[96];
        foreach (var block in bookedBlocks)
            for (var s = block.StartSlot; s < block.StartSlot + block.Length; s++)
                occupied[s] = true;

        var cursorSlot = 34; // default to 8:30am
        if (bookedBlocks.Count > 0)
        {
            var lastEnd = bookedBlocks.Max(b => b.StartSlot + b.Length);
            if (lastEnd < 96)
                cursorSlot = lastEnd;
        }
        TimeBlock? selectedBlock = null;
        var editing = false;
        var showHelp = false;
        var logScrollOffset = 0;

        AnsiConsole.AlternateScreen(() =>
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
                UpdateLayout(layout, bookedBlocks, inputBuffer, filePath, cursorSlot: cursorSlot, selectionLength: selectionLength, occupied: occupied, selectedBlock: selectedBlock, editing: editing, showHelp: showHelp, inputCursor: inputCursor, ticketBuffer: ticketBuffer, ticketCursor: ticketCursor, activeField: activeField, logScrollOffset: logScrollOffset);
                ctx.Refresh();

                var confirming = false;

                while (true)
                {
                    var key = System.Console.ReadKey(true);

                    if (confirming)
                    {
                        if (key.Key == ConsoleKey.Q)
                            break;

                        confirming = false;
                        UpdateLayout(layout, bookedBlocks, inputBuffer, filePath, cursorSlot: cursorSlot, selectionLength: selectionLength, occupied: occupied, selectedBlock: selectedBlock, editing: editing, showHelp: showHelp, inputCursor: inputCursor, ticketBuffer: ticketBuffer, ticketCursor: ticketCursor, activeField: activeField, logScrollOffset: logScrollOffset);
                        ctx.Refresh();
                        continue;
                    }

                    if (key.Key == ConsoleKey.Q && key.Modifiers.HasFlag(ConsoleModifiers.Control))
                    {
                        confirming = true;
                        UpdateLayout(layout, bookedBlocks, inputBuffer, filePath, confirming: true, cursorSlot: cursorSlot, selectionLength: selectionLength, occupied: occupied, selectedBlock: selectedBlock, editing: editing, showHelp: showHelp, inputCursor: inputCursor, ticketBuffer: ticketBuffer, ticketCursor: ticketCursor, activeField: activeField, logScrollOffset: logScrollOffset);
                        ctx.Refresh();
                        continue;
                    }

                    if (showHelp)
                    {
                        showHelp = false;
                        UpdateLayout(layout, bookedBlocks, inputBuffer, filePath, cursorSlot: cursorSlot, selectionLength: selectionLength, occupied: occupied, selectedBlock: selectedBlock, editing: editing, showHelp: showHelp, inputCursor: inputCursor, ticketBuffer: ticketBuffer, ticketCursor: ticketCursor, activeField: activeField, logScrollOffset: logScrollOffset);
                        ctx.Refresh();
                        continue;
                    }

                    if (key.KeyChar == '?' && selectedBlock == null && !editing && inputBuffer.Length == 0 && ticketBuffer.Length == 0)
                    {
                        showHelp = !showHelp;
                        UpdateLayout(layout, bookedBlocks, inputBuffer, filePath, cursorSlot: cursorSlot, selectionLength: selectionLength, occupied: occupied, selectedBlock: selectedBlock, editing: editing, showHelp: showHelp, inputCursor: inputCursor, ticketBuffer: ticketBuffer, ticketCursor: ticketCursor, activeField: activeField, logScrollOffset: logScrollOffset);
                        ctx.Refresh();
                        continue;
                    }

                    var currentBuffer = activeField == 0 ? inputBuffer : ticketBuffer;
                    var currentCursor = activeField == 0 ? inputCursor : ticketCursor;

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
                        activeField = 0;
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
                        activeField = 0;
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
                    else if (key.Key == ConsoleKey.PageUp)
                    {
                        logScrollOffset = Math.Max(0, logScrollOffset - 5);
                    }
                    else if (key.Key == ConsoleKey.PageDown)
                    {
                        var maxOffset = Math.Max(0, bookedBlocks.Count - 1);
                        logScrollOffset = Math.Min(maxOffset, logScrollOffset + 5);
                    }
                    else if (key.Key == ConsoleKey.D && key.Modifiers.HasFlag(ConsoleModifiers.Control))
                    {
                        // Ctrl+D: delete selected block
                        if (selectedBlock != null)
                        {
                            for (var s = selectedBlock.StartSlot; s < selectedBlock.StartSlot + selectedBlock.Length; s++)
                                occupied[s] = false;
                            bookedBlocks.Remove(selectedBlock);
                            PunchStorage.Save(workingDate, bookedBlocks);
                            selectedBlock = null;
                            selectionLength = 1;
                            editing = false;
                            inputBuffer.Clear();
                            inputCursor = 0;
                            ticketBuffer.Clear();
                            ticketCursor = 0;
                            activeField = 0;
                            currentCursor = 0;
                        }
                    }
                    else if (key.Key == ConsoleKey.E && key.Modifiers.HasFlag(ConsoleModifiers.Control))
                    {
                        // Ctrl+E: edit selected block's label and ticket
                        if (selectedBlock != null && !editing)
                        {
                            editing = true;
                            inputBuffer.Clear();
                            inputBuffer.Append(selectedBlock.Label);
                            inputCursor = inputBuffer.Length;
                            ticketBuffer.Clear();
                            ticketBuffer.Append(selectedBlock.Ticket);
                            ticketCursor = ticketBuffer.Length;
                            activeField = 0;
                            currentCursor = inputBuffer.Length;
                        }
                    }
                    else if (key.Key == ConsoleKey.Enter)
                    {
                        if (editing && selectedBlock != null && inputBuffer.Length > 0)
                        {
                            // Save edited label and ticket
                            var newLabel = inputBuffer.ToString();
                            var newTicket = ticketBuffer.ToString();
                            var idx = bookedBlocks.IndexOf(selectedBlock);
                            if (idx >= 0)
                            {
                                selectedBlock = selectedBlock with { Label = newLabel, Ticket = newTicket };
                                bookedBlocks[idx] = selectedBlock;
                                PunchStorage.Save(workingDate, bookedBlocks);
                            }
                            editing = false;
                            inputBuffer.Clear();
                            inputCursor = 0;
                            ticketBuffer.Clear();
                            ticketCursor = 0;
                            activeField = 0;
                            currentCursor = 0;
                        }
                        else if (selectedBlock == null && inputBuffer.Length > 0)
                        {
                            var label = inputBuffer.ToString();
                            var ticket = ticketBuffer.ToString();
                            var block = new TimeBlock(cursorSlot, selectionLength, label, ticket);
                            bookedBlocks.Add(block);
                            for (var s = block.StartSlot; s < block.StartSlot + block.Length; s++)
                                occupied[s] = true;
                            PunchStorage.Save(workingDate, bookedBlocks);

                            inputBuffer.Clear();
                            inputCursor = 0;
                            ticketBuffer.Clear();
                            ticketCursor = 0;
                            activeField = 0;
                            currentCursor = 0;

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
                        if ((selectedBlock == null || editing) && currentCursor > 0)
                        {
                            currentBuffer.Remove(currentCursor - 1, 1);
                            currentCursor--;
                        }
                    }
                    else if (key.Key == ConsoleKey.Delete)
                    {
                        if ((selectedBlock == null || editing) && currentCursor < currentBuffer.Length)
                            currentBuffer.Remove(currentCursor, 1);
                    }
                    else if (key.Key == ConsoleKey.End)
                    {
                        if ((selectedBlock == null || editing) && currentBuffer.Length > 0)
                            currentCursor = currentBuffer.Length;
                    }
                    else if (key.Key == ConsoleKey.Home)
                    {
                        if ((selectedBlock == null || editing) && currentBuffer.Length > 0)
                            currentCursor = 0;
                    }
                    else if (key.Key == ConsoleKey.Tab)
                    {
                        if (selectedBlock == null || editing)
                        {
                            // Write back current cursor before switching
                            if (activeField == 0) inputCursor = currentCursor;
                            else ticketCursor = currentCursor;

                            activeField = activeField == 0 ? 1 : 0;
                            // Place cursor at end of newly focused field
                            if (activeField == 0) currentCursor = inputBuffer.Length;
                            else currentCursor = ticketBuffer.Length;
                        }
                    }
                    else if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
                    {
                        if (selectedBlock == null || editing)
                        {
                            var ch = activeField == 1 ? char.ToUpperInvariant(key.KeyChar) : key.KeyChar;
                            currentBuffer.Insert(currentCursor, ch);
                            currentCursor++;
                        }
                    }

                    // Write back cursor position to the active field
                    if (activeField == 0) inputCursor = currentCursor;
                    else ticketCursor = currentCursor;

                    // Auto-scroll to keep selected block visible
                    if (selectedBlock != null)
                    {
                        var sorted = bookedBlocks.OrderBy(b => b.StartSlot).ToList();
                        var selIdx = sorted.FindIndex(b => b.StartSlot == selectedBlock.StartSlot && b.Length == selectedBlock.Length);
                        if (selIdx >= 0)
                        {
                            // When scrolled, "above" indicator takes 1 line
                            var visHeight = Math.Max(1, System.Console.WindowHeight - 10 - 2 - 1);
                            if (selIdx < logScrollOffset)
                                logScrollOffset = selIdx;
                            else if (selIdx >= logScrollOffset + visHeight)
                                logScrollOffset = selIdx - visHeight + 1;
                        }
                    }

                    // Clamp scroll offset after block additions/deletions
                    // When scrolled down, the "▲ more above" indicator takes 1 line,
                    // so only viewHeight-1 block rows are visible at the bottom.
                    var totalBlocks = bookedBlocks.Count;
                    var viewHeight = Math.Max(1, System.Console.WindowHeight - 10 - 2);
                    var maxOff = Math.Max(0, totalBlocks - (viewHeight - 1));
                    logScrollOffset = Math.Clamp(logScrollOffset, 0, maxOff);

                    UpdateLayout(layout, bookedBlocks, inputBuffer, filePath, cursorSlot: cursorSlot, selectionLength: selectionLength, occupied: occupied, selectedBlock: selectedBlock, editing: editing, showHelp: showHelp, inputCursor: inputCursor, ticketBuffer: ticketBuffer, ticketCursor: ticketCursor, activeField: activeField, logScrollOffset: logScrollOffset);
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

    private static void UpdateLayout(Layout layout, List<TimeBlock> bookedBlocks, StringBuilder inputBuffer, string filePath, bool confirming = false, int cursorSlot = 0, int selectionLength = 1, bool[]? occupied = null, TimeBlock? selectedBlock = null, bool editing = false, bool showHelp = false, int inputCursor = 0, StringBuilder? ticketBuffer = null, int ticketCursor = 0, int activeField = 0, int logScrollOffset = 0)
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

        // Sort blocks chronologically so alternating colors are consistent
        bookedBlocks.Sort((a, b) => a.StartSlot.CompareTo(b.StartSlot));

        // Build the bar line: mark booked slots, then overlay selection
        // Fixed pixels-per-slot for uniform block widths
        var pixelsPerSlot = Math.Max(1, barWidth / 96);
        var totalBarWidth = pixelsPerSlot * 96;
        var pixelState = new int[totalBarWidth]; // 0=free, 1=booked, 2=selected, 3=selected-existing
        var pixelBlockIndex = new int[totalBarWidth];
        Array.Fill(pixelBlockIndex, -1);
        for (var blockIdx = 0; blockIdx < bookedBlocks.Count; blockIdx++)
        {
            var block = bookedBlocks[blockIdx];
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
                    1 => currentBlockIndex % 2 == 0 ? "[orangered1]" : "[orange3]",
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
        var sortedBlocks = bookedBlocks.OrderBy(b => b.StartSlot).ToList();

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
                renderables.Add(new Markup($"[dim]  \u25b2 {clampedOffset} more above (PgUp)[/]"));
            foreach (var b in visibleBlocks)
            {
                var sh = b.StartSlot / 4;
                var sm = (b.StartSlot % 4) * 15;
                var es = b.StartSlot + b.Length;
                var eh = es / 4;
                var em = (es % 4) * 15;
                var escaped = Markup.Escape(b.Label);
                var isSelected = selectedBlock != null && b.StartSlot == selectedBlock.StartSlot && b.Length == selectedBlock.Length;
                var blockIdx = bookedBlocks.IndexOf(b);
                var squareColor = isSelected ? "white" : blockIdx % 2 == 0 ? "orangered1" : "orange3";
                var totalMinutes = b.Length * 15;
                var durationText = totalMinutes >= 60
                    ? totalMinutes % 60 == 0
                        ? $"{totalMinutes / 60}h"
                        : $"{totalMinutes / 60}h {totalMinutes % 60}m"
                    : $"{totalMinutes}m";
                var ticketDisplay = string.IsNullOrEmpty(b.Ticket) ? "" : $"[cyan]{Markup.Escape(b.Ticket)}[/] ";
                renderables.Add(new Markup($"[{squareColor}]\u25a0[/] [bold]{sh:D2}:{sm:D2}\u2013{eh:D2}:{em:D2}[/] {ticketDisplay}{escaped} [dim grey]{durationText}[/]"));
            }
            if (hasMoreBelow)
            {
                var belowCount = sortedBlocks.Count - clampedOffset - availableLines;
                renderables.Add(new Markup($"[dim]  \u25bc {belowCount} more below (PgDn)[/]"));
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
                "[bold]PgUp/PgDn[/]  Scroll time log\n" +
                "[bold]Enter[/]       Log time entry\n" +
                "[bold]Tab[/]         Switch input field\n" +
                "[bold]Ctrl+E[/]      Edit selected entry\n" +
                "[bold]Ctrl+D[/]      Delete selected entry\n" +
                "[bold]Ctrl+Q, Q[/]   Quit\n" +
                "[bold]?[/]           Toggle this help");
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
        else
        {
            layout["Messages"].Update(
                new Panel(messagesContent)
                    .Header("Time Logged")
                    .Expand()
                    .Border(BoxBorder.Rounded));
        }

        // Input pane
        var tb = ticketBuffer ?? new StringBuilder();
        if (confirming)
        {
            layout["Input"].Update(
                new Panel(new Markup("[bold yellow]Press Q again to quit[/]"))
                    .Expand()
                    .Border(BoxBorder.Rounded));
        }
        else if (selectedBlock != null && editing)
        {
            var descLine = RenderFieldLine("Description", inputBuffer, inputCursor, activeField == 0);
            var tickLine = RenderFieldLine("Ticket", tb, ticketCursor, activeField == 1);
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
            var tickLine = RenderFieldLine("Ticket", tb, ticketCursor, activeField == 1);
            layout["Input"].Update(
                new Panel(new Rows(new Markup(descLine), new Markup(tickLine)))
                    .Header("Input")
                    .Expand()
                    .Border(BoxBorder.Rounded));
        }

        // Status bar
        var totalMinutesAll = bookedBlocks.Sum(b => b.Length * 15);
        var totalHours = totalMinutesAll / 60;
        var totalMins = totalMinutesAll % 60;
        var totalFormatted = totalMins > 0 ? $"{totalHours}h {totalMins}m" : $"{totalHours}h 0m";
        var percent = totalMinutesAll * 100 / 480;
        var statusLeftPlain = $"  {filePath}  ?=help";
        var statusRight = $"{totalFormatted}    {percent}% of 8h  ";
        var padding = Math.Max(0, consoleWidth - statusLeftPlain.Length - statusRight.Length);
        var statusBar = $"[white on orangered1]  {Markup.Escape(filePath)}  [dim]?=help[/]{new string(' ', padding)}{Markup.Escape(statusRight)}[/]";
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
