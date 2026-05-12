using Punch.CLI;
using Xunit;

namespace Punch.CLI.Tests;

public class ResizeTests
{
    [Fact]
    public void CanGrowBlock_AllowsGrowthIntoFreeSlot()
    {
        var occupied = new bool[96];
        for (var s = 32; s < 36; s++) occupied[s] = true;
        Assert.True(PunchCommand.CanGrowBlock(32, 4, occupied));
    }

    [Fact]
    public void CanGrowBlock_BlocksGrowthIntoOccupiedSlot()
    {
        var occupied = new bool[96];
        for (var s = 32; s < 36; s++) occupied[s] = true;
        occupied[36] = true;
        Assert.False(PunchCommand.CanGrowBlock(32, 4, occupied));
    }

    [Fact]
    public void CanGrowBlock_BlocksGrowthPastSlot96()
    {
        var occupied = new bool[96];
        for (var s = 92; s < 96; s++) occupied[s] = true;
        Assert.False(PunchCommand.CanGrowBlock(92, 4, occupied));
    }
}
