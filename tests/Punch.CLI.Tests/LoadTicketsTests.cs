using System;
using System.IO;
using Punch.CLI;
using Xunit;

namespace Punch.CLI.Tests;

public class LoadTicketsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _ticketsPath;

    public LoadTicketsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "punch-tickets-" + Guid.NewGuid().ToString("N"));
        // Override points at the data subdir so tickets.txt resolves to the parent (_tempDir).
        var dataDir = Path.Combine(_tempDir, "data");
        Directory.CreateDirectory(dataDir);
        PunchStorage.DataDirectoryOverride = dataDir;
        _ticketsPath = PunchStorage.GetTicketsFilePath();
    }

    public void Dispose()
    {
        PunchStorage.DataDirectoryOverride = null;
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void TicketsPath_SitsBesideDataDir()
    {
        Assert.Equal(Path.Combine(_tempDir, "tickets.txt"), _ticketsPath);
    }

    [Fact]
    public void LoadTickets_ReturnsEmptyWhenFileMissing()
    {
        Assert.Empty(PunchStorage.LoadTickets());
    }

    [Fact]
    public void LoadTickets_ParsesTabDelimitedLine()
    {
        File.WriteAllText(_ticketsPath, "ABC-123\tFix login bug\n");

        var entry = Assert.Single(PunchStorage.LoadTickets());
        Assert.Equal("ABC-123", entry.Ticket);
        Assert.Equal("Fix login bug", entry.Title);
    }

    [Fact]
    public void LoadTickets_ParsesCommaDelimitedLine()
    {
        File.WriteAllText(_ticketsPath, "ABC-123,Fix login bug\n");

        var entry = Assert.Single(PunchStorage.LoadTickets());
        Assert.Equal("ABC-123", entry.Ticket);
        Assert.Equal("Fix login bug", entry.Title);
    }

    [Fact]
    public void LoadTickets_SkipsBlankLinesAndComments()
    {
        File.WriteAllText(_ticketsPath, """
        # Common tickets

        ABC-1,First

        # another comment
        ABC-2,Second
        """);

        var entries = PunchStorage.LoadTickets();
        Assert.Equal(2, entries.Count);
        Assert.Equal("ABC-1", entries[0].Ticket);
        Assert.Equal("ABC-2", entries[1].Ticket);
    }

    [Fact]
    public void LoadTickets_TicketOnlyLineYieldsEmptyTitle()
    {
        File.WriteAllText(_ticketsPath, "ABC-123\n");

        var entry = Assert.Single(PunchStorage.LoadTickets());
        Assert.Equal("ABC-123", entry.Ticket);
        Assert.Equal("", entry.Title);
    }

    [Fact]
    public void LoadTickets_SkipsRowsWithEmptyTicket()
    {
        File.WriteAllText(_ticketsPath, ",No ticket here\nABC-9,Real\n");

        var entry = Assert.Single(PunchStorage.LoadTickets());
        Assert.Equal("ABC-9", entry.Ticket);
    }
}
