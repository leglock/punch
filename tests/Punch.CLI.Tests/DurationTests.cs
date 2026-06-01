using Punch.CLI;
using Xunit;

namespace Punch.CLI.Tests;

public class DurationTests
{
    [Theory]
    [InlineData(0, "0m")]
    [InlineData(45, "45m")]
    [InlineData(60, "1h")]
    [InlineData(90, "1h 30m")]
    [InlineData(120, "2h")]
    public void Humanize_UsesCompactForm(int minutes, string expected)
    {
        Assert.Equal(expected, Duration.Humanize(minutes));
    }

    [Theory]
    [InlineData(0, "0h 0m")]
    [InlineData(45, "0h 45m")]
    [InlineData(90, "1h 30m")]
    [InlineData(120, "2h 0m")]
    public void HumanizeTotal_AlwaysLeadsWithHours(int minutes, string expected)
    {
        Assert.Equal(expected, Duration.HumanizeTotal(minutes));
    }
}
