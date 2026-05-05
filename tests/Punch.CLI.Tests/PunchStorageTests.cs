using System;
using System.Collections.Generic;
using System.IO;
using Punch.CLI;
using Xunit;

namespace Punch.CLI.Tests;

public class PunchStorageTests : IDisposable
{
    private readonly string _tempDir;

    public PunchStorageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "punch-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        PunchStorage.DataDirectoryOverride = _tempDir;
    }

    public void Dispose()
    {
        PunchStorage.DataDirectoryOverride = null;
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void GetFilePath_FormatsDateAsIsoYmd()
    {
        var path = PunchStorage.GetFilePath(new DateOnly(2026, 5, 4));
        Assert.EndsWith("2026-05-04.json", path);
        Assert.StartsWith(_tempDir, path);
    }

    [Fact]
    public void Load_ReturnsEmptyWhenFileMissing()
    {
        var blocks = PunchStorage.Load(new DateOnly(1999, 1, 1));
        Assert.Empty(blocks);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsBlocks()
    {
        var date = new DateOnly(2026, 5, 4);
        var original = new List<TimeBlock>
        {
            new(32, 4, "Standup", "ABC-1"),
            new(36, 8, "Coding", "ABC-2"),
            new(48, 4, "Lunch"),
        };

        PunchStorage.Save(date, original);
        var loaded = PunchStorage.Load(date);

        Assert.Equal(original, loaded);
    }

    [Fact]
    public void Load_SkipsBlocksOutOfRange()
    {
        var date = new DateOnly(2026, 5, 4);
        var path = PunchStorage.GetFilePath(date);
        File.WriteAllText(path, """
        {
          "Blocks": [
            { "StartSlot": -1, "Length": 4, "Label": "Negative", "Ticket": "" },
            { "StartSlot": 95, "Length": 2, "Label": "OverrunsEnd", "Ticket": "" },
            { "StartSlot": 96, "Length": 1, "Label": "PastEnd", "Ticket": "" },
            { "StartSlot": 10, "Length": 0, "Label": "ZeroLength", "Ticket": "" },
            { "StartSlot": 32, "Length": 4, "Label": "Valid", "Ticket": "OK" }
          ]
        }
        """);

        var blocks = PunchStorage.Load(date);

        var valid = Assert.Single(blocks);
        Assert.Equal("Valid", valid.Label);
        Assert.Equal(32, valid.StartSlot);
    }

    [Fact]
    public void Load_SkipsOverlappingBlocks()
    {
        var date = new DateOnly(2026, 5, 4);
        var path = PunchStorage.GetFilePath(date);
        File.WriteAllText(path, """
        {
          "Blocks": [
            { "StartSlot": 32, "Length": 8, "Label": "First", "Ticket": "" },
            { "StartSlot": 36, "Length": 4, "Label": "OverlapsFirst", "Ticket": "" },
            { "StartSlot": 40, "Length": 4, "Label": "Adjacent", "Ticket": "" }
          ]
        }
        """);

        var blocks = PunchStorage.Load(date);

        Assert.Equal(2, blocks.Count);
        Assert.Equal("First", blocks[0].Label);
        Assert.Equal("Adjacent", blocks[1].Label);
    }

    [Fact]
    public void Load_ReturnsEmptyOnMalformedJson()
    {
        var date = new DateOnly(2026, 5, 4);
        File.WriteAllText(PunchStorage.GetFilePath(date), "{ this is not valid json");

        var blocks = PunchStorage.Load(date);

        Assert.Empty(blocks);
    }

    [Fact]
    public void Save_CreatesDataDirectoryWhenMissing()
    {
        var nestedDir = Path.Combine(_tempDir, "nested", "subdir");
        PunchStorage.DataDirectoryOverride = nestedDir;

        PunchStorage.Save(new DateOnly(2026, 5, 4), new List<TimeBlock>
        {
            new(0, 4, "Test"),
        });

        Assert.True(Directory.Exists(nestedDir));
    }
}
