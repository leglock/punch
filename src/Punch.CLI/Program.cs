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
                UpdateLayout(layout, messages, inputBuffer, cursorSlot: cursorSlot);
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
                        UpdateLayout(layout, messages, inputBuffer, cursorSlot: cursorSlot);
                        ctx.Refresh();
                        continue;
                    }

                    if (key.Key == ConsoleKey.Q && key.Modifiers.HasFlag(ConsoleModifiers.Control))
                    {
                        confirming = true;
                        UpdateLayout(layout, messages, inputBuffer, confirming: true);
                        ctx.Refresh();
                        continue;
                    }

                    if (key.Key == ConsoleKey.LeftArrow)
                    {
                        if (cursorSlot > 0)
                            cursorSlot--;
                    }
                    else if (key.Key == ConsoleKey.RightArrow)
                    {
                        if (cursorSlot < 95)
                            cursorSlot++;
                    }
                    else if (key.Key == ConsoleKey.Enter)
                    {
                        if (inputBuffer.Length > 0)
                        {
                            var timestamp = DateTime.Now.ToString("HH:mm:ss");
                            var escaped = Markup.Escape(inputBuffer.ToString());
                            messages.Add($"[dim]{timestamp}[/] {escaped}");
                            inputBuffer.Clear();
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

                    UpdateLayout(layout, messages, inputBuffer, cursorSlot: cursorSlot);
                    ctx.Refresh();
                }
            });
        });

        return 0;
    }

    private static void UpdateLayout(Layout layout, List<string> messages, StringBuilder inputBuffer, bool confirming = false, int cursorSlot = 0)
    {
        // Timeline pane
        var consoleWidth = System.Console.WindowWidth;
        var barWidth = Math.Max(1, consoleWidth - 4); // account for panel border + padding
        var cursorHours = cursorSlot / 4;
        var cursorMinutes = (cursorSlot % 4) * 15;
        var timeLabel = $"{cursorHours:D2}:{cursorMinutes:D2}";

        // Build the bar line with cursor marker
        var barChars = new char[barWidth];
        Array.Fill(barChars, '─');
        var cursorPos = (int)((double)cursorSlot / 95 * (barWidth - 1));
        barChars[cursorPos] = '▼';

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

        // Position the time label above the cursor
        var timeLabelStart = Math.Max(0, Math.Min(cursorPos - timeLabel.Length / 2, barWidth - timeLabel.Length));
        var topLine = new string(' ', timeLabelStart) + timeLabel;

        var barMarkup = new string(barChars).Replace("▼", "[bold yellow]▼[/]");
        var timelineContent = new Rows(
            new Markup($"[bold]{Markup.Escape(topLine)}[/]"),
            new Markup(barMarkup),
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
            new Markup("[dim]Press [bold]← →[/] to move cursor | [bold]Enter[/] to send | [bold]Ctrl+Q[/] to quit[/]"));
    }
}
