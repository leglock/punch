using Punch.CLI;
using Xunit;

namespace Punch.CLI.Tests;

public class AdvanceAfterAddTests
{
    [Fact]
    public void AdvancesToNextFreeSlot()
    {
        var schedule = new DaySchedule(new[] { new TimeBlock(4, 1, "existing", "") });

        var (cursorSlot, selectionLength, selectedBlock) = schedule.AdvanceAfterAdd(4);

        Assert.Equal(5, cursorSlot);
        Assert.Equal(1, selectionLength);
        Assert.Null(selectedBlock);
    }

    [Fact]
    public void SkipsOverMultipleOccupiedSlots()
    {
        var schedule = new DaySchedule(new[]
        {
            new TimeBlock(4, 3, "block-a", ""),
            new TimeBlock(7, 3, "block-b", ""),
        });

        var (cursorSlot, selectionLength, selectedBlock) = schedule.AdvanceAfterAdd(4);

        Assert.Equal(10, cursorSlot);
        Assert.Equal(1, selectionLength);
        Assert.Null(selectedBlock);
    }

    [Fact]
    public void FallsBackToSlot95WhenDayIsFull_SelectsExistingBlock()
    {
        var existingBlock = new TimeBlock(92, 4, "last-block", "TICKET");
        var schedule = new DaySchedule(new[]
        {
            new TimeBlock(90, 2, "almost-last", ""),
            existingBlock,
        });

        var (cursorSlot, selectionLength, selectedBlock) = schedule.AdvanceAfterAdd(90);

        Assert.Equal(95, cursorSlot);
        Assert.Equal(existingBlock.Length, selectionLength);
        Assert.Same(existingBlock, selectedBlock);
    }

    [Fact]
    public void StaysAtCurrentSlotIfFree()
    {
        var schedule = new DaySchedule(new List<TimeBlock>());

        var (cursorSlot, selectionLength, selectedBlock) = schedule.AdvanceAfterAdd(20);

        Assert.Equal(20, cursorSlot);
        Assert.Equal(1, selectionLength);
        Assert.Null(selectedBlock);
    }
}
