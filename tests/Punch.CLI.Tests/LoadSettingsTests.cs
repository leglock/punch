using System;
using System.IO;
using Punch.CLI;
using Xunit;

namespace Punch.CLI.Tests;

[Collection(StorageCollection.Name)]
public class LoadSettingsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _settingsPath;

    public LoadSettingsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "punch-settings-" + Guid.NewGuid().ToString("N"));
        // Override points at the data subdir so settings.json resolves to the parent (_tempDir).
        var dataDir = Path.Combine(_tempDir, "data");
        Directory.CreateDirectory(dataDir);
        PunchStorage.DataDirectoryOverride = dataDir;
        _settingsPath = PunchStorage.GetSettingsFilePath();
    }

    public void Dispose()
    {
        PunchStorage.DataDirectoryOverride = null;
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void SettingsPath_SitsBesideDataDir()
    {
        Assert.Equal(Path.Combine(_tempDir, "settings.json"), _settingsPath);
    }

    [Fact]
    public void LoadSettings_DefaultsWhenFileMissing()
    {
        Assert.Equal(8, PunchStorage.LoadSettings().TargetHours);
    }

    [Fact]
    public void LoadSettings_ReadsTargetHours()
    {
        File.WriteAllText(_settingsPath, """{ "targetHours": 6 }""");

        Assert.Equal(6, PunchStorage.LoadSettings().TargetHours);
    }

    [Fact]
    public void LoadSettings_DefaultsWhenFileIsGarbage()
    {
        File.WriteAllText(_settingsPath, "not json");

        Assert.Equal(8, PunchStorage.LoadSettings().TargetHours);
    }

    [Fact]
    public void LoadSettings_ClampsTargetHoursToAtLeastOne()
    {
        File.WriteAllText(_settingsPath, """{ "targetHours": 0 }""");

        Assert.Equal(1, PunchStorage.LoadSettings().TargetHours);
    }

    [Fact]
    public void GetTargetHours_UsesPerDayOverrideAndFallsBack()
    {
        File.WriteAllText(_settingsPath,
            """{ "targetHours": 8, "targetHoursByDay": { "friday": 4, "saturday": 0 } }""");

        var settings = PunchStorage.LoadSettings();
        Assert.Equal(4, settings.GetTargetHours(DayOfWeek.Friday));
        Assert.Equal(0, settings.GetTargetHours(DayOfWeek.Saturday));
        Assert.Equal(8, settings.GetTargetHours(DayOfWeek.Monday));
    }

    [Fact]
    public void GetTargetHours_MatchesDayNamesCaseInsensitively()
    {
        File.WriteAllText(_settingsPath,
            """{ "targetHoursByDay": { "SUNDAY": 2 } }""");

        Assert.Equal(2, PunchStorage.LoadSettings().GetTargetHours(DayOfWeek.Sunday));
    }

    [Fact]
    public void GetTargetHours_ClampsNegativePerDayValuesToZero()
    {
        File.WriteAllText(_settingsPath,
            """{ "targetHoursByDay": { "sunday": -3 } }""");

        Assert.Equal(0, PunchStorage.LoadSettings().GetTargetHours(DayOfWeek.Sunday));
    }

    [Fact]
    public void GetTargetHours_FallsBackToFlatTargetWhenNoMap()
    {
        File.WriteAllText(_settingsPath, """{ "targetHours": 6 }""");

        Assert.Equal(6, PunchStorage.LoadSettings().GetTargetHours(DayOfWeek.Wednesday));
    }
}
