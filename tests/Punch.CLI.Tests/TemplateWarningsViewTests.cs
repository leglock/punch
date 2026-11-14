using Punch.CLI;
using Spectre.Console;
using Spectre.Console.Testing;
using Xunit;

namespace Punch.CLI.Tests;

public class TemplateWarningsViewTests
{
    [Fact]
    public void FirstRender_ShowsEscapedWarningsSeparatelyFromEntriesAndNote()
    {
        var session = new PunchSession(new DaySchedule([new TimeBlock(36, 1, "Standup")]),
            new DateOnly(2026, 5, 4), "unused", 34)
        {
            Note = "Private note",
            StartupWarnings = ["dayTemplates '[red]': unknown weekday.", "monday entry 2: label must be a nonblank string."]
        };
        using var console = new TestConsole();
        console.Profile.Width = 120;
        console.Write(PunchView.BuildLogPanel(session, 120, 30));

        var output = console.Output;
        Assert.Contains("Warning: dayTemplates '[red]': unknown weekday.", output);
        Assert.Contains("monday entry 2: label", output);
        Assert.Contains("Standup", output);
        Assert.DoesNotContain("Private note", output);
        Assert.Empty(session.InputBuffer.ToString());
        Assert.Empty(session.TicketBuffer.ToString());
    }

    [Fact]
    public void Scrolling_ReachesLaterWarningsEvenWithoutAnyBlocks()
    {
        var warnings = Enumerable.Range(1, 20).Select(i => $"monday entry {i}: invalid start time.").ToArray();
        var session = new PunchSession(new DaySchedule([]), new DateOnly(2026, 5, 4), "unused", 34)
        {
            StartupWarnings = warnings,
            LogScrollOffset = 16
        };
        using var console = new TestConsole();
        console.Profile.Width = 100;
        console.Write(PunchView.BuildLogPanel(session, 100, 20));

        Assert.Contains("monday entry 20: invalid start time.", console.Output);
        Assert.Contains("No entries yet", console.Output);
        Assert.DoesNotContain("monday entry 1:", console.Output);
    }
}
