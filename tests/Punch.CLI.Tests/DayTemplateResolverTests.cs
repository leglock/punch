using System.Globalization;
using System.Text.Json;
using Punch.CLI;
using Xunit;

namespace Punch.CLI.Tests;

public class DayTemplateResolverTests
{
    private static (DaySchedule Schedule, IReadOnlyList<string> Warnings) Resolve(string json) =>
        DayTemplateResolver.Resolve(JsonSerializer.Deserialize<JsonElement>(json), DayOfWeek.Monday);

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"monday":null}""")]
    [InlineData("""{"monday":[]}""")]
    [InlineData("""{"tuesday":[{"label":"Other day","start":"09:00","end":"10:00"}]}""")]
    public void MissingOrEmptyTemplate_ProducesNothing(string json)
    {
        var result = Resolve(json);
        Assert.Empty(result.Schedule.Blocks);
        Assert.Empty(result.Warnings);
        Assert.Empty(DayTemplateResolver.Resolve(null, DayOfWeek.Monday).Schedule.Blocks);
    }

    [Theory]
    [InlineData(DayOfWeek.Monday)]
    [InlineData(DayOfWeek.Tuesday)]
    [InlineData(DayOfWeek.Wednesday)]
    [InlineData(DayOfWeek.Thursday)]
    [InlineData(DayOfWeek.Friday)]
    [InlineData(DayOfWeek.Saturday)]
    [InlineData(DayOfWeek.Sunday)]
    public void AllWeekdaysAndEntryProperties_AreCaseInsensitive(DayOfWeek day)
    {
        var json = $$"""
            {"{{day.ToString().ToUpperInvariant()}}":[
              {"LaBeL":"  Standup  ","TICKET":"  team-i23  ","START":"09:00","End":"09:15"},
              {"label":"Planning","start":"10:00","end":"11:00"},
              {"label":"Review","ticket":null,"start":"15:00","end":"15:30"}
            ]}
            """;
        // Invariant normalization must not become Turkish dotted I.
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var result = DayTemplateResolver.Resolve(JsonSerializer.Deserialize<JsonElement>(json), day);
            Assert.Empty(result.Warnings);
            Assert.Equal(new[]
            {
                new TimeBlock(36, 1, "Standup", "TEAM-I23"),
                new TimeBlock(40, 4, "Planning"),
                new TimeBlock(60, 2, "Review")
            }, result.Schedule.Blocks);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("00:00", "00:15", 0, 1)]
    [InlineData("23:45", "24:00", 95, 1)]
    [InlineData("00:00", "24:00", 0, 96)]
    [InlineData("08:15", "10:45", 33, 10)]
    public void ValidRanges_ConvertExactly(string start, string end, int slot, int length)
    {
        var result = Resolve($$"""{"monday":[{"label":"Work","start":"{{start}}","end":"{{end}}"}]}""");
        Assert.Empty(result.Warnings);
        Assert.Equal(new TimeBlock(slot, length, "Work"), Assert.Single(result.Schedule.Blocks));
    }

    [Theory]
    [InlineData("09:00", "09:00")]
    [InlineData("10:00", "09:00")]
    [InlineData("23:00", "01:00")]
    [InlineData("24:00", "24:00")]
    [InlineData("00:00", "00:00")]
    [InlineData("9:00", "10:00")]
    [InlineData("09:0", "10:00")]
    [InlineData(" 09:00", "10:00")]
    [InlineData("09:00", "10:00 ")]
    [InlineData("09:00:00", "10:00")]
    [InlineData("09:01", "10:00")]
    [InlineData("09:00", "10:14")]
    [InlineData("09:60", "10:00")]
    [InlineData("09:00", "24:15")]
    [InlineData("25:00", "26:00")]
    [InlineData("09.00", "10:00")]
    [InlineData("０９:00", "10:00")]
    [InlineData("-1:00", "10:00")]
    public void InvalidRanges_AreSkippedWithContext(string start, string end)
    {
        var result = Resolve($$"""{"monday":[{"label":"Work","start":"{{start}}","end":"{{end}}"}]}""");
        Assert.Empty(result.Schedule.Blocks);
        Assert.Contains("monday entry 1:", Assert.Single(result.Warnings));
    }

    [Theory]
    [InlineData("null", "entry object")]
    [InlineData("42", "entry object")]
    [InlineData("[]", "entry object")]
    [InlineData("{}", "label")]
    [InlineData("""{"label":null}""", "label")]
    [InlineData("""{"label":"   "}""", "label")]
    [InlineData("""{"label":42}""", "label")]
    [InlineData("""{"label":{} }""", "label")]
    [InlineData("""{"label":"Bad","ticket":123,"start":"09:00","end":"10:00"}""", "ticket")]
    [InlineData("""{"label":"Bad","ticket":[],"start":"09:00","end":"10:00"}""", "ticket")]
    [InlineData("""{"label":"Bad","end":"10:00"}""", "start")]
    [InlineData("""{"label":"Bad","start":900,"end":"10:00"}""", "start")]
    [InlineData("""{"label":"Bad","start":null,"end":"10:00"}""", "start")]
    [InlineData("""{"label":"Bad","start":"09:00"}""", "end")]
    [InlineData("""{"label":"Bad","start":"09:00","end":true}""", "end")]
    public void BadEntry_DoesNotDiscardValidSiblings(string entry, string reason)
    {
        var result = Resolve($$"""
            {"monday":[
              {"label":"Before","start":"08:00","end":"08:15"},
              {{entry}},
              {"label":"After","start":"10:00","end":"10:15"}
            ]}
            """);
        Assert.Equal(2, result.Schedule.Count);
        var warning = Assert.Single(result.Warnings);
        Assert.Contains("monday entry 2:", warning);
        Assert.Contains(reason, warning);
    }

    [Theory]
    [InlineData("[]", "object")]
    [InlineData("42", "object")]
    [InlineData("true", "object")]
    [InlineData("\"bad\"", "object")]
    [InlineData("""{"mon":[]}""", "unknown weekday")]
    [InlineData("""{"1":[]}""", "unknown weekday")]
    [InlineData("""{" monday":[]}""", "unknown weekday")]
    [InlineData("""{"monday":{}}""", "entry list")]
    [InlineData("""{"tuesday":123}""", "entry list")]
    public void MalformedSubsections_WarnRatherThanThrow(string json, string reason)
    {
        var result = Resolve(json);
        Assert.Empty(result.Schedule.Blocks);
        Assert.Contains(reason, Assert.Single(result.Warnings));
    }

    [Fact]
    public void Conflicts_FirstAcceptedWinsInConfigurationOrder_AdjacencyIsAllowed()
    {
        var result = Resolve("""
            {"monday":[
              {"label":"First","start":"10:00","end":"11:00"},
              {"label":"Earlier overlapping","start":"09:30","end":"10:30"},
              {"label":"Contained","start":"10:15","end":"10:30"},
              {"label":"Containing","start":"09:00","end":"12:00"},
              {"label":"Duplicate","start":"10:00","end":"11:00"},
              {"label":"Before","start":"09:00","end":"10:00"},
              {"label":"After","start":"11:00","end":"12:00"}
            ]}
            """);
        Assert.Equal(new[] { "First", "Before", "After" }, result.Schedule.Blocks.Select(b => b.Label));
        Assert.Equal(4, result.Warnings.Count);
        Assert.All(result.Warnings, w => Assert.Contains("overlaps", w));
        Assert.False(result.Schedule.IsFree(36));
        Assert.True(result.Schedule.IsFree(48));
    }
}
