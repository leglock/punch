using System.Reflection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Punch.CLI;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] is "--version" or "-v")
        {
            var version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
            Console.WriteLine(version);
            return 0;
        }

        var app = new CommandApp<PunchCommand>();
        app.Configure(config =>
        {
            config.SetApplicationName("punch");
        });

        return app.Run(args);
    }
}

internal sealed class PunchCommand : Command
{
    public override int Execute(CommandContext context)
    {
        AnsiConsole.AlternateScreen(() =>
        {
            AnsiConsole.Cursor.Hide();

            var layout = new Layout("Root")
                .SplitRows(
                    new Layout("Top"),
                    new Layout("Title").Size(1),
                    new Layout("Bottom"),
                    new Layout("Footer").Size(1));

            layout["Top"].Update(new Panel("").Expand().Border(BoxBorder.None));
            layout["Title"].Update(
                new Markup("[bold][red]p[/][orangered1]u[/][darkorange]n[/][orange3]c[/][orange1]h[/][/]")
                    .Centered());
            layout["Bottom"].Update(new Panel("").Expand().Border(BoxBorder.None));

            layout["Footer"].Update(
                new Markup("[dim]Press [bold]q[/] to quit[/]"));

            AnsiConsole.Write(layout);

            while (true)
            {
                var key = System.Console.ReadKey(true);
                if (key.KeyChar is 'q' or 'Q')
                    break;
            }
        });

        return 0;
    }
}
