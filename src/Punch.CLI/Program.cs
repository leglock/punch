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

        var messages = new List<string>();
        var inputBuffer = new StringBuilder();
        var cursorSlot = 0; // 0–95: 96 quarter-hour slots (00:00–23:45)
        var selectionLength = 1; // number of 15-min slots selected (min 1)
        var bookedBlocks = new List<TimeBlock>();
        var occupied = new bool[96];

        AnsiConsole.AlternateScreen(() =>
        {
            AnsiConsole.Cursor.Hide();

            var layout = new Layout("Root")
                .SplitRows(
                    new Layout("Timeline").Size(5),
                    new Layout("Messages").Ratio(4),
                    new Layout("Input").Ratio(1),
                    new Layout("Footer").Size(1));

            AnsiConsole.Live(layout).Start(ctx =>
            {
                UpdateLayout(layout, messages, inputBuffer, cursorSlot: cursorSlot, selectionLength: selectionLength, occupied: occupied);
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
                        UpdateLayout(layout, messages, inputBuffer, cursorSlot: cursorSlot, selectionLength: selectionLength, occupied: occupied);
                        ctx.Refresh();
                        continue;
                    }

                    if (key.Key == ConsoleKey.Q && key.Modifiers.HasFlag(ConsoleModifiers.Control))
                    {
                        confirming = true;
                        UpdateLayout(layout, messages, inputBuffer, confirming: true, cursorSlot: cursorSlot, selectionLength: selectionLength, occupied: occupied);
                        ctx.Refresh();
                        continue;
                    }

                    if (key.Key == ConsoleKey.LeftArrow)
                    {
                        var next = cursorSlot - 1;
                        while (next >= 0 && IsOverlapping(next, selectionLength, occupied))
                            next--;
                        if (next >= 0)
                            cursorSlot = next;
                    }
                    else if (key.Key == ConsoleKey.RightArrow)
                    {
                        var next = cursorSlot + 1;
                        while (next + selectionLength <= 96 && IsOverlapping(next, selectionLength, occupied))
                            next++;
                        if (next + selectionLength <= 96)
                            cursorSlot = next;
                    }
                    else if (key.KeyChar == '+')
                    {
                        var newLen = selectionLength + 1;
                        if (cursorSlot + newLen <= 96 && !IsOverlapping(cursorSlot, newLen, occupied))
                            selectionLength = newLen;
                    }
                    else if (key.KeyChar == '-')
                    {
                        if (selectionLength > 1)
                            selectionLength--;
                    }
                    else if (key.Key == ConsoleKey.Enter)
                    {
                        if (inputBuffer.Length > 0)
                        {
                            var label = inputBuffer.ToString();
                            var block = new TimeBlock(cursorSlot, selectionLength, label);
                            bookedBlocks.Add(block);
                            for (var s = block.StartSlot; s < block.StartSlot + block.Length; s++)
                                occupied[s] = true;

                            var sh = cursorSlot / 4;
                            var sm = (cursorSlot % 4) * 15;
                            var es = cursorSlot + selectionLength;
                            var eh = es / 4;
                            var em = (es % 4) * 15;
                            var escaped = Markup.Escape(label);
                            messages.Add($"[green]■[/] [bold]{sh:D2}:{sm:D2}\u2013{eh:D2}:{em:D2}[/] {escaped}");
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
                        if (inputBuffer.Length > 0)
                            inputBuffer.Remove(inputBuffer.Length - 1, 1);
                    }
                    else if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
                    {
                        inputBuffer.Append(key.KeyChar);
                    }

                    UpdateLayout(layout, messages, inputBuffer, cursorSlot: cursorSlot, selectionLength: selectionLength, occupied: occupied);
                    ctx.Refresh();
                }
            });
        });

        return 0;
    }

    private static bool IsOverlapping(int start, int length, bool[] occupied)
    {
        for (var i = start; i < start + length && i < 96; i++)
            if (occupied[i])
                return true;
        return false;
    }

    private static void UpdateLayout(Layout layout, List<string> messages, StringBuilder inputBuffer, bool confirming = false, int cursorSlot = 0, int selectionLength = 1, bool[]? occupied = null)
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
        var pixelState = new int[barWidth]; // 0=free, 1=booked, 2=selected
        var occ = occupied ?? new bool[96];
        for (var px = 0; px < barWidth; px++)
        {
            var slotStart = (int)((double)px / barWidth * 96);
            var slotEnd = (int)((double)(px + 1) / barWidth * 96);
            if (slotEnd <= slotStart) slotEnd = slotStart + 1;
            for (var s = slotStart; s < slotEnd && s < 96; s++)
            {
                if (occ[s]) { pixelState[px] = 1; break; }
            }
        }

        var selStartPos = (int)((double)cursorSlot / 96 * barWidth);
        var selEndPos = (int)((double)endSlot / 96 * barWidth) - 1;
        selStartPos = Math.Clamp(selStartPos, 0, barWidth - 1);
        selEndPos = Math.Clamp(selEndPos, selStartPos, barWidth - 1);
        for (var i = selStartPos; i <= selEndPos; i++)
            pixelState[i] = 2;

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

        // Messages pane: show recent messages that fit
        var consoleHeight = System.Console.WindowHeight;
        var messagesHeight = Math.Max(1, (int)(consoleHeight * 0.8) - 2); // account for panel border
        var visibleMessages = messages.Count > messagesHeight
            ? messages.Skip(messages.Count - messagesHeight).ToList()
            : messages;

        IRenderable messagesContent;
        if (visibleMessages.Count == 0)
        {
            messagesContent = new Markup("[dim]No messages yet. Type below and press Enter.[/]");
        }
        else
        {
            var renderables = visibleMessages.Select(m => (IRenderable)new Markup(m)).ToArray();
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
        else
        {
            var inputText = Markup.Escape(inputBuffer.ToString());
            layout["Input"].Update(
                new Panel(new Markup($"> {inputText}[blink]_[/]"))
                    .Expand()
                    .Border(BoxBorder.Rounded));
        }

        // Footer
        layout["Footer"].Update(
            new Markup("[dim]Press [bold]← →[/] move | [bold]+/−[/] resize | [bold]Enter[/] send | [bold]Ctrl+Q[/] quit[/]"));
    }
}
