using Punch.CLI;
using Xunit;

namespace Punch.CLI.Tests;

public class ResizeTests
{
    [Fact]
    public void CanGrow_AllowsGrowthIntoFreeSlot()
    {
        var schedule = new DaySchedule(new[] { new TimeBlock(32, 4, "block", "") });
        Assert.True(schedule.CanGrow(32, 4));
    }

    [Fact]
    public void CanGrow_BlocksGrowthIntoOccupiedSlot()
    {
        var schedule = new DaySchedule(new[]
        {
            new TimeBlock(32, 4, "block", ""),
            new TimeBlock(36, 1, "neighbour", ""),
        });
        Assert.False(schedule.CanGrow(32, 4));
    }

    [Fact]
    public void CanGrow_BlocksGrowthPastSlot96()
    {
        var schedule = new DaySchedule(new[] { new TimeBlock(92, 4, "block", "") });
        Assert.False(schedule.CanGrow(92, 4));
    }
}
