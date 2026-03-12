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

        AnsiConsole.AlternateScreen(() =>
        {
            AnsiConsole.Cursor.Hide();

            var layout = new Layout("Root")
                .SplitRows(
                    new Layout("Messages").Ratio(4),
                    new Layout("Input").Ratio(1),
                    new Layout("Footer").Size(1));

            AnsiConsole.Live(layout).Start(ctx =>
            {
                UpdateLayout(layout, messages, inputBuffer);
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
                        UpdateLayout(layout, messages, inputBuffer);
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

                    if (key.Key == ConsoleKey.Enter)
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

                    UpdateLayout(layout, messages, inputBuffer);
                    ctx.Refresh();
                }
            });
        });

        return 0;
    }

    private static void UpdateLayout(Layout layout, List<string> messages, StringBuilder inputBuffer, bool confirming = false)
    {
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
            new Markup("[dim]Press [bold]Enter[/] to send | [bold]Ctrl+Q[/] to quit[/]"));
    }
}
