using System.Reflection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Punch.CLI;

internal sealed class PunchCommand : Command<PunchCommandSettings>
{
    private readonly IAnsiConsole _console;

    public PunchCommand(IAnsiConsole console)
    {
        _console = console;
    }

    public override int Execute(CommandContext context, PunchCommandSettings settings)
    {
        if (settings.Version)
        {
            var version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
            Console.WriteLine(version);
            return 0;
        }

        if (settings.Date != null && settings.Yesterday)
        {
            Console.Error.WriteLine("Cannot combine --date and --yesterday.");
            return 1;
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
        else if (settings.Yesterday)
        {
            workingDate = DateOnly.FromDateTime(DateTime.Now).AddDays(-1);
        }
        else
        {
            workingDate = DateOnly.FromDateTime(DateTime.Now);
        }

        var filePath = PunchStorage.GetDisplayPath(workingDate);
        TimesheetInitialization initialized;
        PunchSettings appSettings;
        try
        {
            appSettings = PunchStorage.LoadSettings();
            initialized = TimesheetInitializer.Initialize(workingDate, appSettings);
        }
        catch (Exception ex)
        {
            _console.MarkupLine($"[bold red]Error:[/] Failed to load time log or initialize templates at [bold cyan]{Markup.Escape(filePath)}[/].");
            _console.MarkupLine($"[bold yellow]Reason:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }

        var (cursorSlot, selectionLength, selectedBlock) = initialized.GetInitialSelection();
        var themes = appSettings.CreateThemes();
        var session = new PunchSession(initialized.Schedule, workingDate, filePath, cursorSlot,
            appSettings.GetTargetHours(workingDate.DayOfWeek), appSettings.CreateNonBillableMatcher(),
            themes, ThemeCatalog.IndexOf(themes, PunchStorage.LoadState().Theme))
        {
            Note = initialized.Note,
            StartupWarnings = initialized.Warnings,
            SelectionLength = selectionLength,
            SelectedBlock = selectedBlock
        };

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
