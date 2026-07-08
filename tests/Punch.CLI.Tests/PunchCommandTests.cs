using System;
using System.IO;
using System.Reflection;
using Punch.CLI;
using Spectre.Console.Cli;
using Spectre.Console.Testing;
using Xunit;

namespace Punch.CLI.Tests;

[Collection(StorageCollection.Name)]
public class PunchCommandTests : IDisposable
{
    private readonly string _tempDir;

    public PunchCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "punch-cmd-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        PunchStorage.DataDirectoryOverride = _tempDir;
    }

    public void Dispose()
    {
        PunchStorage.DataDirectoryOverride = null;
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static CommandAppTester CreateApp()
    {
        var app = new CommandAppTester();
        app.SetDefaultCommand<PunchCommand>();
        app.Configure(c => c.SetApplicationName("punch"));
        return app;
    }

    private static (CommandAppResult Result, string Stdout, string Stderr) RunCapturing(CommandAppTester app, params string[] args)
    {
        var originalOut = Console.Out;
        var originalErr = Console.Error;
        using var outWriter = new StringWriter();
        using var errWriter = new StringWriter();
        Console.SetOut(outWriter);
        Console.SetError(errWriter);
        try
        {
            var result = app.Run(args);
            return (result, outWriter.ToString(), errWriter.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }
    }

    [Fact]
    public void Version_PrintsAssemblyVersionAndReturnsZero()
    {
        var expected = typeof(PunchCommand).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

        var (result, stdout, _) = RunCapturing(CreateApp(), "--version");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(expected, stdout);
    }

    [Fact]
    public void VersionShortFlag_BehavesTheSame()
    {
        var (result, stdout, _) = RunCapturing(CreateApp(), "-v");

        Assert.Equal(0, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(stdout));
    }

    [Fact]
    public void InvalidDateFormat_ReturnsOneWithErrorMessage()
    {
        var (result, _, stderr) = RunCapturing(CreateApp(), "--date", "2026/05/04");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Invalid date format", stderr);
        Assert.Contains("2026/05/04", stderr);
    }

    [Fact]
    public void DateCombinedWithYesterday_ReturnsOneWithErrorMessage()
    {
        var (result, _, stderr) = RunCapturing(CreateApp(), "--date", "2026-05-04", "--yesterday");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Cannot combine --date and --yesterday", stderr);
    }

    [Fact]
    public void Yesterday_ResolvesToPreviousDay()
    {
        // A corrupt file at yesterday's date makes Execute fail before entering
        // the TUI, proving --yesterday resolved to the previous calendar day.
        var yesterday = DateOnly.FromDateTime(DateTime.Now).AddDays(-1);
        File.WriteAllText(PunchStorage.GetFilePath(yesterday), "{ not valid json");

        var (result, stdout, _) = RunCapturing(CreateApp(), "--yesterday");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Failed to load time log", AllOutput(result, stdout));
        Assert.Contains(yesterday.ToString("yyyy-MM-dd"), AllOutput(result, stdout));
    }

    [Fact]
    public void YesterdayShortFlag_BehavesTheSame()
    {
        var yesterday = DateOnly.FromDateTime(DateTime.Now).AddDays(-1);
        File.WriteAllText(PunchStorage.GetFilePath(yesterday), "{ not valid json");

        var (result, stdout, _) = RunCapturing(CreateApp(), "-y");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(yesterday.ToString("yyyy-MM-dd"), AllOutput(result, stdout));
    }

    // AnsiConsole markup lands in the tester's TestConsole (result.Output) while plain
    // Console writes land in the redirected stdout, so check both. Line breaks are
    // stripped because the console wraps the long temp-dir path.
    private static string AllOutput(CommandAppResult result, string stdout) =>
        (stdout + result.Output).Replace("\r", "").Replace("\n", "");

    [Fact]
    public void CorruptDataFile_ReturnsOneWithoutEnteringTui()
    {
        var date = new DateOnly(2026, 5, 4);
        File.WriteAllText(PunchStorage.GetFilePath(date), "{ not valid json");

        var (result, stdout, _) = RunCapturing(CreateApp(), "--date", "2026-05-04");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Failed to load time log", AllOutput(result, stdout));
    }
}
