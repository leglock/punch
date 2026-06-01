using Punch.CLI;
using Xunit;

namespace Punch.CLI.Tests;

public class DayScheduleTests
{
    [Fact]
    public void Constructor_DerivesOccupancyFromBlocks()
    {
        var schedule = new DaySchedule(new[] { new TimeBlock(32, 4, "block", "") });

        Assert.False(schedule.IsFree(32));
        Assert.False(schedule.IsFree(35));
        Assert.True(schedule.IsFree(31));
        Assert.True(schedule.IsFree(36));
    }

    [Fact]
    public void Add_MarksSlotsOccupied()
    {
        var schedule = new DaySchedule(new List<TimeBlock>());

        schedule.Add(new TimeBlock(10, 2, "task", ""));

        Assert.False(schedule.IsFree(10));
        Assert.False(schedule.IsFree(11));
        Assert.Equal(1, schedule.Count);
    }

    [Fact]
    public void Remove_FreesSlots()
    {
        var block = new TimeBlock(10, 2, "task", "");
        var schedule = new DaySchedule(new[] { block });

        schedule.Remove(block);

        Assert.True(schedule.IsFree(10));
        Assert.True(schedule.IsFree(11));
        Assert.Equal(0, schedule.Count);
    }

    [Fact]
    public void Replace_WithShorterBlock_FreesVacatedTail()
    {
        var original = new TimeBlock(10, 4, "task", "");
        var schedule = new DaySchedule(new[] { original });

        var shorter = original with { Length = 2 };
        schedule.Replace(original, shorter);

        Assert.False(schedule.IsFree(10));
        Assert.False(schedule.IsFree(11));
        Assert.True(schedule.IsFree(12));
        Assert.True(schedule.IsFree(13));
        Assert.Equal(shorter, Assert.Single(schedule.Blocks));
    }

    [Fact]
    public void FreeSlots_ThenFillSlots_RestoresOccupancy()
    {
        var block = new TimeBlock(10, 2, "task", "");
        var schedule = new DaySchedule(new[] { block });

        schedule.FreeSlots(block);
        Assert.True(schedule.IsFree(10));

        schedule.FillSlots(block);
        Assert.False(schedule.IsFree(10));
        // Block stays in the list throughout (it is only lifted from the mask).
        Assert.Single(schedule.Blocks);
    }

    [Fact]
    public void FindAt_ReturnsBlockCoveringSlot()
    {
        var block = new TimeBlock(10, 4, "task", "");
        var schedule = new DaySchedule(new[] { block });

        Assert.Same(block, schedule.FindAt(13));
        Assert.Null(schedule.FindAt(14));
    }

    [Fact]
    public void IsOverlapping_DetectsOccupiedSlotInRange()
    {
        var schedule = new DaySchedule(new[] { new TimeBlock(10, 2, "task", "") });

        Assert.True(schedule.IsOverlapping(8, 4));
        Assert.False(schedule.IsOverlapping(12, 4));
    }
}
