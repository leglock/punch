using Punch.CLI;
using Xunit;

namespace Punch.CLI.Tests;

public class TimeBlockTests
{
    [Theory]
    [InlineData("Lunch")]
    [InlineData("LUNCH break")]
    [InlineData("team lunch")]
    [InlineData("Out for Lunch with the crew")]
    [InlineData("Break")]
    [InlineData("coffee BREAK")]
    [InlineData("short break with the team")]
    [InlineData("lunch!")]
    [InlineData("break-time")]
    [InlineData("lunch/break")]
    [InlineData("my lunch.")]
    [InlineData("LuNcH")]
    [InlineData("bReAk")]
    public void IsUnpaid_DetectsLunchAndBreakLabelsCaseInsensitive(string label)
    {
        var block = new TimeBlock(0, 4, label);
        Assert.True(block.IsUnpaid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("meeting")]
    [InlineData("standup")]
    [InlineData("luncheon")]
    [InlineData("breakfast")]
    [InlineData("breaking changes")]
    [InlineData("system breakdown")]
    [InlineData("lunchbox")]
    [InlineData("lunch1")]
    [InlineData("1break")]
    [InlineData("   ")]
    public void IsUnpaid_ReturnsFalseForNonUnpaidLabelsAndSubstrings(string label)
    {
        var block = new TimeBlock(0, 4, label);
        Assert.False(block.IsUnpaid);
    }

    [Fact]
    public void Ticket_DefaultsToEmptyString()
    {
        var block = new TimeBlock(0, 4, "Coding");
        Assert.Equal("", block.Ticket);
    }

    [Fact]
    public void Records_WithSameValues_AreEqual()
    {
        var a = new TimeBlock(8, 4, "Coding", "ABC-123");
        var b = new TimeBlock(8, 4, "Coding", "ABC-123");
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Records_WithDifferentValues_AreNotEqual()
    {
        var a = new TimeBlock(8, 4, "Coding", "ABC-123");
        var b = new TimeBlock(8, 4, "Coding", "XYZ-999");
        Assert.NotEqual(a, b);
    }
}
