using System.Collections.Generic;
using System.Linq;
using Punch.CLI;
using Xunit;

namespace Punch.CLI.Tests;

// TicketCatalog.Build is pure — no disk, no statics — so this suite stays out
// of the storage collection.
public class TicketCatalogTests
{
    private static List<TicketEntry> Saved(params (string Ticket, string Title)[] rows) =>
        rows.Select(r => new TicketEntry(r.Ticket, r.Title)).ToList();

    [Fact]
    public void LogTickets_ComeFirstWithTheBlockLabelAsTitle()
    {
        var saved = Saved(("ABC-1", "First ticket"));
        var blocks = new[] { new TimeBlock(10, 2, "Fix login bug", "PROJ-99") };

        var result = TicketCatalog.Build(saved, blocks);

        Assert.Equal(2, result.Count);
        Assert.Equal(new TicketEntry("PROJ-99", "Fix login bug", FromLog: true), result[0]);
        Assert.Equal(new TicketEntry("ABC-1", "First ticket"), result[1]);
    }

    [Fact]
    public void TicketAlreadyInSavedList_IsNotDuplicated()
    {
        var saved = Saved(("ABC-1", "First ticket"));
        var blocks = new[] { new TimeBlock(10, 2, "Some work", "ABC-1") };

        var result = TicketCatalog.Build(saved, blocks);

        Assert.Equal(new TicketEntry("ABC-1", "First ticket"), Assert.Single(result));
    }

    [Fact]
    public void SavedMatchIsCaseInsensitive()
    {
        var saved = Saved(("abc-1", "First ticket"));
        var blocks = new[] { new TimeBlock(10, 2, "Some work", "ABC-1") };

        var result = TicketCatalog.Build(saved, blocks);

        Assert.Equal(new TicketEntry("abc-1", "First ticket"), Assert.Single(result));
    }

    [Fact]
    public void BlocksWithoutATicket_AreIgnored()
    {
        var blocks = new[]
        {
            new TimeBlock(10, 2, "no ticket", ""),
            new TimeBlock(12, 2, "blank ticket", "   ")
        };

        Assert.Empty(TicketCatalog.Build(Saved(), blocks));
    }

    [Fact]
    public void RepeatedTicket_TakesTheLongestBlocksLabel()
    {
        var blocks = new[]
        {
            new TimeBlock(10, 1, "short stint", "PROJ-99"),
            new TimeBlock(20, 4, "the real work", "PROJ-99")
        };

        var entry = Assert.Single(TicketCatalog.Build(Saved(), blocks));
        Assert.Equal(new TicketEntry("PROJ-99", "the real work", FromLog: true), entry);
    }

    [Fact]
    public void RepeatedTicketOfEqualLength_TakesTheEarliestBlocksLabel()
    {
        var blocks = new[]
        {
            new TimeBlock(20, 2, "afternoon", "PROJ-99"),
            new TimeBlock(10, 2, "morning", "PROJ-99")
        };

        var entry = Assert.Single(TicketCatalog.Build(Saved(), blocks));
        Assert.Equal("morning", entry.Title);
    }

    [Fact]
    public void RepeatedTicket_IsGroupedCaseInsensitively()
    {
        var blocks = new[]
        {
            new TimeBlock(10, 1, "morning", "proj-99"),
            new TimeBlock(20, 4, "the real work", "PROJ-99")
        };

        var entry = Assert.Single(TicketCatalog.Build(Saved(), blocks));
        Assert.Equal(new TicketEntry("PROJ-99", "the real work", FromLog: true), entry);
    }

    [Fact]
    public void LogTickets_AreOrderedByEarliestStartSlot()
    {
        // DaySchedule.Blocks is insertion-ordered, so the sort must be explicit.
        var blocks = new[]
        {
            new TimeBlock(40, 2, "standup", "OPS-4"),
            new TimeBlock(10, 2, "fix bug", "PROJ-99")
        };

        var result = TicketCatalog.Build(Saved(), blocks);

        Assert.Equal(new[] { "PROJ-99", "OPS-4" }, result.Select(t => t.Ticket));
    }

    [Fact]
    public void BlockWithATicketButNoLabel_YieldsAnEmptyTitle()
    {
        var blocks = new[] { new TimeBlock(10, 2, "", "PROJ-99") };

        Assert.Equal("", Assert.Single(TicketCatalog.Build(Saved(), blocks)).Title);
    }

    [Fact]
    public void NoBlocks_ReturnsTheSavedListUnchanged()
    {
        var saved = Saved(("ABC-1", "First ticket"), ("DEF-2", "Second ticket"));

        var result = TicketCatalog.Build(saved, new List<TimeBlock>());

        Assert.Equal(saved, result);
    }

    [Fact]
    public void NoSavedTickets_ReturnsOnlyTheLogTickets()
    {
        var blocks = new[] { new TimeBlock(10, 2, "Fix login bug", "PROJ-99") };

        var entry = Assert.Single(TicketCatalog.Build(Saved(), blocks));
        Assert.True(entry.FromLog);
    }

    [Fact]
    public void BothSourcesEmpty_ReturnsEmpty()
    {
        Assert.Empty(TicketCatalog.Build(Saved(), new List<TimeBlock>()));
    }
}
