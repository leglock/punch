using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Punch.CLI;
using Spectre.Console;
using Xunit;

namespace Punch.CLI.Tests;

[Collection(StorageCollection.Name)]
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
    public void Note_RoundTripsWithoutBlocksAndCanBeReplacedOrCleared()
    {
        var date = new DateOnly(2026, 5, 4);
        PunchStorage.Save(date, new List<TimeBlock>(), "Review [draft] — café");
        Assert.Empty(PunchStorage.Load(date, out var note));
        Assert.Equal("Review [draft] — café", note);

        PunchStorage.Save(date, new List<TimeBlock>(), "Replacement");
        PunchStorage.Load(date, out note);
        Assert.Equal("Replacement", note);

        PunchStorage.Save(date, new List<TimeBlock>(), "");
        PunchStorage.Load(date, out note);
        Assert.Equal("", note);
    }

    [Fact]
    public void BlockOnlySave_PreservesNote()
    {
        var date = new DateOnly(2026, 5, 4);
        PunchStorage.Save(date, new List<TimeBlock>(), "Keep me");
        PunchStorage.Save(date, new List<TimeBlock> { new(32, 4, "Work") });
        Assert.Single(PunchStorage.Load(date, out var note));
        Assert.Equal("Keep me", note);
    }

    [Theory]
    [InlineData("{ \"Blocks\": [] }")]
    [InlineData("{ \"Blocks\": [], \"Note\": null }")]
    [InlineData("null")]
    public void Load_MissingOrNullNoteDefaultsToEmpty(string json)
    {
        var date = new DateOnly(2026, 5, 4);
        File.WriteAllText(PunchStorage.GetFilePath(date), json);
        PunchStorage.Load(date, out var note);
        Assert.Equal("", note);
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

    [Theory]
    [InlineData(0, int.MaxValue)]
    [InlineData(1, int.MaxValue)]
    [InlineData(50, int.MaxValue)]
    [InlineData(95, int.MaxValue)]
    [InlineData(50, 2147483000)]
    public void Load_SkipsBlocksWithExcessiveLength(int startSlot, int length)
    {
        var date = new DateOnly(2026, 5, 4);
        var valid = new TimeBlock(startSlot, 1, "Valid");
        var json = JsonSerializer.Serialize(new
        {
            Blocks = new[] { new TimeBlock(startSlot, length, "Invalid"), valid }
        });
        File.WriteAllText(PunchStorage.GetFilePath(date), json);

        Assert.Equal(valid, Assert.Single(PunchStorage.Load(date)));
    }

    [Theory]
    [InlineData(0, 96)]
    [InlineData(50, 46)]
    [InlineData(95, 1)]
    public void Load_AcceptsBlocksEndingAtDayBoundary(int startSlot, int length)
    {
        var date = new DateOnly(2026, 5, 4);
        var block = new TimeBlock(startSlot, length, "Valid");
        var json = JsonSerializer.Serialize(new { Blocks = new[] { block } });
        File.WriteAllText(PunchStorage.GetFilePath(date), json);

        Assert.Equal(block, Assert.Single(PunchStorage.Load(date)));
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
    public void Load_ThrowsExceptionAndCreatesBackupOnMalformedJson()
    {
        var date = new DateOnly(2026, 5, 4);
        var path = PunchStorage.GetFilePath(date);
        var corruptContent = "{ this is not valid json";
        File.WriteAllText(path, corruptContent);

        var ex = Assert.Throws<InvalidOperationException>(() => PunchStorage.Load(date));
        Assert.Contains("corrupted", ex.Message);

        var backupPath = path + ".bak";
        Assert.True(File.Exists(backupPath));
        Assert.Equal(corruptContent, File.ReadAllText(backupPath));
    }

    [Fact]
    public void SaveThenLoad_PreservesResizedLength()
    {
        var date = new DateOnly(2026, 5, 4);
        var original = new TimeBlock(32, 4, "Standup", "ABC-1");
        PunchStorage.Save(date, new List<TimeBlock> { original });

        var resized = original with { Length = 6 };
        PunchStorage.Save(date, new List<TimeBlock> { resized });

        var loaded = PunchStorage.Load(date);
        var only = Assert.Single(loaded);
        Assert.Equal(6, only.Length);
        Assert.Equal(32, only.StartSlot);
        Assert.Equal("Standup", only.Label);
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

    [Fact]
    public void GetDisplayPath_SubstitutesTildeWhenUnderHome()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        PunchStorage.DataDirectoryOverride = Path.Combine(home, ".punch-test", "data");

        var display = PunchStorage.GetDisplayPath(new DateOnly(2026, 5, 4));

        Assert.StartsWith("~", display);
        Assert.EndsWith("2026-05-04.json", display);
    }

    [Fact]
    public void GetDisplayPath_ReturnsAbsolutePathWhenOutsideHome()
    {
        // Don't use _tempDir: on Windows the temp dir lives under the user's home.
        // GetDisplayPath is pure string work, so the directory needn't exist.
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var outside = Path.Combine(Path.GetPathRoot(home)!, "punch-outside-home", "data");
        PunchStorage.DataDirectoryOverride = outside;

        var display = PunchStorage.GetDisplayPath(new DateOnly(2026, 5, 4));

        Assert.StartsWith(outside, display);
        Assert.DoesNotContain("~", display);
    }

    [Fact]
    public void GetDisplayPath_DoesNotSubstituteTildeForSiblingOfHome()
    {
        var home = Path.TrimEndingDirectorySeparator(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var sibling = Path.Combine(home + "-sibling", "data");
        PunchStorage.DataDirectoryOverride = sibling;

        var display = PunchStorage.GetDisplayPath(new DateOnly(2026, 5, 4));

        Assert.StartsWith(sibling, display);
        Assert.DoesNotContain("~", display);
    }

    [Fact]
    public void Load_ReturnsEmptyWhenJsonIsNullLiteral()
    {
        var date = new DateOnly(2026, 5, 4);
        File.WriteAllText(PunchStorage.GetFilePath(date), "null");

        Assert.Empty(PunchStorage.Load(date));
    }

    [Fact]
    public void Load_ReturnsEmptyWhenBlocksFieldIsNull()
    {
        var date = new DateOnly(2026, 5, 4);
        File.WriteAllText(PunchStorage.GetFilePath(date), """{ "Blocks": null }""");

        Assert.Empty(PunchStorage.Load(date));
    }

    [Fact]
    public void Load_ReturnsEmptyWhenBlocksArrayIsEmpty()
    {
        var date = new DateOnly(2026, 5, 4);
        File.WriteAllText(PunchStorage.GetFilePath(date), """{ "Blocks": [] }""");

        Assert.Empty(PunchStorage.Load(date));
    }

    [Fact]
    public void Load_DefaultsTicketToEmptyStringWhenFieldMissing()
    {
        var date = new DateOnly(2026, 5, 4);
        File.WriteAllText(PunchStorage.GetFilePath(date), """
        {
          "Blocks": [
            { "StartSlot": 32, "Length": 4, "Label": "NoTicketField" }
          ]
        }
        """);

        var only = Assert.Single(PunchStorage.Load(date));
        Assert.Equal("", only.Ticket);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(null, "ABC-1")]
    [InlineData("Work", null)]
    public void Load_NormalizesExplicitNullLabelAndTicket(string? label, string? ticket)
    {
        var date = new DateOnly(2026, 5, 4);
        var json = JsonSerializer.Serialize(new
        {
            Blocks = new[] { new { StartSlot = 32, Length = 4, Label = label, Ticket = ticket } }
        });
        File.WriteAllText(PunchStorage.GetFilePath(date), json);

        var block = Assert.Single(PunchStorage.Load(date));

        Assert.Equal(new TimeBlock(32, 4, label ?? "", ticket ?? ""), block);
        Assert.Equal(label ?? "", Markup.Escape(block.Label));
        Assert.Equal(ticket ?? "", Markup.Escape(block.Ticket));
        Assert.False(NonBillableMatcher.Default.IsNonBillable(block.Label));
    }

    [Fact]
    public void Save_EmptyListRoundTripsAsEmpty()
    {
        var date = new DateOnly(2026, 5, 4);

        PunchStorage.Save(date, new List<TimeBlock>());

        Assert.True(File.Exists(PunchStorage.GetFilePath(date)));
        Assert.Empty(PunchStorage.Load(date));
    }

    [Fact]
    public void Save_OverwritesExistingFileAndDropsRemovedBlocks()
    {
        var date = new DateOnly(2026, 5, 4);
        PunchStorage.Save(date, new List<TimeBlock>
        {
            new(32, 4, "First", "ABC-1"),
            new(36, 4, "Second", "ABC-2"),
        });

        PunchStorage.Save(date, new List<TimeBlock>
        {
            new(32, 4, "First", "ABC-1"),
        });

        var loaded = PunchStorage.Load(date);
        var only = Assert.Single(loaded);
        Assert.Equal("First", only.Label);
    }

    [Fact]
    public void Save_LeavesNoTempFileBehind()
    {
        var date = new DateOnly(2026, 5, 4);
        PunchStorage.Save(date, new List<TimeBlock> { new(32, 4, "Test", "ABC-1") });

        var tmpPath = PunchStorage.GetFilePath(date) + ".tmp";
        Assert.False(File.Exists(tmpPath));
    }
}
