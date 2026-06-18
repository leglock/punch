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

        var appSettings = PunchStorage.LoadSettings();
        var session = new PunchSession(schedule, workingDate, filePath, cursorSlot, appSettings.TargetHours);

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
            var controller = new PunchController(session, view);

            AnsiConsole.Live(layout).Start(controller.Run);
            }
            finally
            {
                AnsiConsole.Cursor.Show();
            }
        });

        return 0;
    }
}
