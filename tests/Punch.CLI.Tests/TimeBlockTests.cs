using Punch.CLI;
using Xunit;

namespace Punch.CLI.Tests;

public class TimeBlockTests
{
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
