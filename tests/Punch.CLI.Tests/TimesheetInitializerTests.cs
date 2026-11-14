using System.Text.Json;
using Punch.CLI;
using Spectre.Console;
using Xunit;

namespace Punch.CLI.Tests;

[Collection(StorageCollection.Name)]
public class TimesheetInitializerTests : IDisposable
{
    private static readonly DateOnly Monday = new(2026, 5, 4);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "punch-templates-" + Guid.NewGuid().ToString("N"));

    public TimesheetInitializerTests()
    {
        PunchStorage.DataDirectoryOverride = Path.Combine(_root, "data");
        Directory.CreateDirectory(PunchStorage.GetDataDirectory());
    }

    public void Dispose()
    {
        PunchStorage.DataDirectoryOverride = null;
        Directory.Delete(_root, recursive: true);
    }

    private static PunchSettings Settings(string templates = """
        {"monday":[
          {"label":"Standup","ticket":"team-123","start":"09:00","end":"09:15"},
          {"label":"Planning","start":"15:00","end":"16:00"}
        ]}
        """) => new() { DayTemplates = JsonSerializer.Deserialize<JsonElement>(templates) };

    [Fact]
    public void MissingDay_IsSavedImmediately_ReopenDoesNotChangeFile()
    {
        var initialized = TimesheetInitializer.Initialize(Monday, Settings());
        Assert.True(initialized.TemplatesApplied);
        Assert.Equal(2, initialized.Schedule.Count);
        Assert.Equal(initialized.Schedule.Blocks, PunchStorage.Load(Monday, out var note));
        Assert.Equal("", note);
        var saved = File.ReadAllText(PunchStorage.GetFilePath(Monday));

        var reopened = TimesheetInitializer.Initialize(Monday, Settings("42"));
        Assert.False(reopened.TemplatesApplied);
        Assert.Empty(reopened.Warnings); // Existing files bypass template parsing entirely.
        Assert.Equal(saved, File.ReadAllText(PunchStorage.GetFilePath(Monday)));
        Assert.Equal(initialized.Schedule.Blocks, reopened.Schedule.Blocks);
    }

    [Fact]
    public void ChangesAndDeletions_PersistIndefinitely()
    {
        var initialized = TimesheetInitializer.Initialize(Monday, Settings());
        var schedule = initialized.Schedule;
        var changed = new TimeBlock(32, 3, "Changed description", "NEW-456");
        schedule.Replace(schedule.Blocks[0], changed);
        PunchStorage.Save(Monday, schedule.Blocks, "User note");

        var reopened = TimesheetInitializer.Initialize(Monday, Settings());
        Assert.Equal(changed, reopened.Schedule.Blocks[0]);
        Assert.Equal("User note", reopened.Note);

        reopened.Schedule.Remove(reopened.Schedule.Blocks[1]);
        PunchStorage.Save(Monday, reopened.Schedule.Blocks);
        Assert.Equal(changed, Assert.Single(TimesheetInitializer.Initialize(Monday, Settings()).Schedule.Blocks));

        reopened.Schedule.Remove(changed);
        PunchStorage.Save(Monday, reopened.Schedule.Blocks);
        var empty = TimesheetInitializer.Initialize(Monday, Settings());
        Assert.Empty(empty.Schedule.Blocks);
        Assert.False(empty.TemplatesApplied);
        Assert.Equal("User note", empty.Note);

        // Only manual deletion of the file makes the date eligible again.
        File.Delete(PunchStorage.GetFilePath(Monday));
        Assert.True(TimesheetInitializer.Initialize(Monday, Settings()).TemplatesApplied);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Note-only day")]
    public void ExistingEmptyDay_SuppressesTemplatesAndPreservesNote(string note)
    {
        PunchStorage.Save(Monday, [], note);
        var initialized = TimesheetInitializer.Initialize(Monday, Settings());
        Assert.Empty(initialized.Schedule.Blocks);
        Assert.Equal(note, initialized.Note);
        Assert.False(initialized.TemplatesApplied);
    }

    [Fact]
    public void LegacyLog_IsLoadedWithoutRewrite()
    {
        const string legacy = """{"Blocks":[{"StartSlot":8,"Length":2,"Label":"Legacy"}]}""";
        File.WriteAllText(PunchStorage.GetFilePath(Monday), legacy);
        var initialized = TimesheetInitializer.Initialize(Monday, Settings());
        Assert.Equal(new TimeBlock(8, 2, "Legacy"), Assert.Single(initialized.Schedule.Blocks));
        Assert.False(initialized.TemplatesApplied);
        Assert.Equal("", initialized.Note);
        Assert.Equal(legacy, File.ReadAllText(PunchStorage.GetFilePath(Monday)));
    }

    [Fact]
    public void CorruptLog_IsBackedUpAndNeverReplacedWithTemplates()
    {
        var path = PunchStorage.GetFilePath(Monday);
        const string corrupt = "{ corrupt";
        File.WriteAllText(path, corrupt);
        Assert.Throws<InvalidOperationException>(() => TimesheetInitializer.Initialize(Monday, Settings()));
        Assert.Equal(corrupt, File.ReadAllText(path));
        Assert.Equal(corrupt, File.ReadAllText(path + ".bak"));
    }

    [Theory]
    [InlineData("null", 0)]
    [InlineData("{}", 0)]
    [InlineData("""{"monday":null}""", 0)]
    [InlineData("""{"monday":[]}""", 0)]
    [InlineData("""{"tuesday":[{"label":"Other","start":"09:00","end":"10:00"}]}""", 0)]
    [InlineData("""{"monday":[null,{"label":"Invalid"}]}""", 2)]
    public void NoAcceptedEntries_DoesNotCreateFile(string templates, int warningCount)
    {
        var initialized = TimesheetInitializer.Initialize(Monday, Settings(templates));
        Assert.False(initialized.TemplatesApplied);
        Assert.Empty(initialized.Schedule.Blocks);
        Assert.Equal(warningCount, initialized.Warnings.Count);
        Assert.False(File.Exists(PunchStorage.GetFilePath(Monday)));
        // Fixing the settings permits retry.
        Assert.True(TimesheetInitializer.Initialize(Monday, Settings()).TemplatesApplied);
    }

    [Theory]
    [InlineData(2020, 5, 4)]
    [InlineData(2037, 5, 4)]
    public void WorkingDateDeterminesWeekday_ZeroHourGoalDoesNotSuppressTemplates(int year, int month, int day)
    {
        var date = new DateOnly(year, month, day);
        var settings = Settings($$"""
            {"{{date.DayOfWeek}}":[{"label":"Date-specific","start":"12:00","end":"12:15"}]}
            """);
        settings.TargetHoursByDay = new() { [date.DayOfWeek.ToString()] = 0 };
        Assert.Equal(0, settings.GetTargetHours(date.DayOfWeek));
        var initialized = TimesheetInitializer.Initialize(date, settings);
        Assert.True(initialized.TemplatesApplied);
        Assert.Equal("Date-specific", Assert.Single(PunchStorage.Load(date)).Label);
        Assert.Equal(date.ToString("yyyy-MM-dd") + ".json", Path.GetFileName(PunchStorage.GetFilePath(date)));
    }

    [Fact]
    public void InitialSaveFailure_PropagatesWithActionableMessage()
    {
        Directory.CreateDirectory(PunchStorage.GetFilePath(Monday) + ".tmp");
        var error = Assert.Throws<IOException>(() => TimesheetInitializer.Initialize(Monday, Settings()));
        Assert.Contains("Failed to save initial dayTemplates", error.Message);
        Assert.Contains("writable", error.Message);
        Assert.NotNull(error.InnerException);
        Assert.False(File.Exists(PunchStorage.GetFilePath(Monday)));
    }

    [Theory]
    [InlineData("15:00", "15:30", 34, 1, false)]
    [InlineData("08:30", "09:00", 34, 2, true)]
    [InlineData("08:00", "09:00", 32, 4, true)]
    [InlineData("00:00", "24:00", 0, 96, true)]
    public void NewTemplateDay_StartsAt0830OrCoveringBlock(
        string start, string end, int slot, int length, bool selected)
    {
        var initialized = TimesheetInitializer.Initialize(Monday,
            Settings($$"""{"monday":[{"label":"Meeting","start":"{{start}}","end":"{{end}}"}]}"""));
        var selection = initialized.GetInitialSelection();
        Assert.Equal(slot, selection.CursorSlot);
        Assert.Equal(length, selection.SelectionLength);
        Assert.Equal(selected, selection.SelectedBlock != null);
        if (selected)
            Assert.Same(initialized.Schedule.Blocks[0], selection.SelectedBlock);
    }

    [Fact]
    public void SavedDay_KeepsExistingCursorPolicy()
    {
        TimesheetInitializer.Initialize(Monday, Settings());
        var selection = TimesheetInitializer.Initialize(Monday, Settings()).GetInitialSelection();
        Assert.Equal(64, selection.CursorSlot);
        Assert.Equal(1, selection.SelectionLength);
        Assert.Null(selection.SelectedBlock);
    }

    [Fact]
    public void Warnings_AreScrollableAndNeverPersistedOrPutInInput()
    {
        var initialized = TimesheetInitializer.Initialize(Monday,
            Settings("""{"monday":[null,{},false,42,[],{"label":"Valid","start":"08:30","end":"09:00"}]}"""));
        var session = new PunchSession(initialized.Schedule, Monday, "unused", 34)
        {
            Note = initialized.Note,
            StartupWarnings = initialized.Warnings
        };
        var controller = new PunchController(session, new PunchView(new Layout("Root")), () => 15);
        Assert.Equal(5, session.StartupWarnings.Count);
        controller.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.PageDown, false, false, false));
        Assert.Equal(4, session.LogScrollOffset);
        controller.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.PageUp, false, false, false));
        Assert.Equal(0, session.LogScrollOffset);
        Assert.Empty(session.InputBuffer.ToString());
        Assert.Empty(session.TicketBuffer.ToString());
        Assert.Empty(session.NoteBuffer.ToString());
        Assert.Equal("", session.Note);
        var saved = File.ReadAllText(PunchStorage.GetFilePath(Monday));
        Assert.DoesNotContain("Warning", saved);
        Assert.DoesNotContain("dayTemplates", saved);
        Assert.Empty(TimesheetInitializer.Initialize(Monday, Settings()).Warnings);
    }

    [Fact]
    public void ControllerDeletesGeneratedEntry_ItDoesNotReturnOnReopen()
    {
        var initialized = TimesheetInitializer.Initialize(Monday,
            Settings("""{"monday":[{"label":"Meeting","start":"08:30","end":"09:00"}]}"""));
        var selection = initialized.GetInitialSelection();
        var session = new PunchSession(initialized.Schedule, Monday, "unused", selection.CursorSlot)
        {
            SelectedBlock = selection.SelectedBlock,
            SelectionLength = selection.SelectionLength
        };
        var controller = new PunchController(session, new PunchView(new Layout("Root")));
        controller.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.D, false, false, true));
        controller.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.D, false, false, true));
        Assert.Empty(TimesheetInitializer.Initialize(Monday, Settings()).Schedule.Blocks);
    }
}
