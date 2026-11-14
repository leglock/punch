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
    public void SelectsFinalBlockWhenNoLaterSlotIsFree()
    {
        var existingBlock = new TimeBlock(92, 4, "last-block", "TICKET");
        var schedule = new DaySchedule(new[]
        {
            new TimeBlock(90, 2, "almost-last", ""),
            existingBlock,
        });

        var (cursorSlot, selectionLength, selectedBlock) = schedule.AdvanceAfterAdd(90);

        Assert.Equal(existingBlock.StartSlot, cursorSlot);
        Assert.Equal(existingBlock.Length, selectionLength);
        Assert.Same(existingBlock, selectedBlock);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    [InlineData(95)]
    public void FullDay_SelectsFinalBlockAtItsStart(int finalBlockStart)
    {
        var finalBlock = new TimeBlock(finalBlockStart, 96 - finalBlockStart, "last-block");
        var schedule = new DaySchedule(finalBlockStart == 0
            ? new[] { finalBlock }
            : new[] { new TimeBlock(0, finalBlockStart, "earlier"), finalBlock });

        var (cursorSlot, selectionLength, selectedBlock) = schedule.AdvanceAfterAdd(0);

        Assert.Same(finalBlock, selectedBlock);
        Assert.Equal(finalBlock.StartSlot, cursorSlot);
        Assert.Equal(finalBlock.Length, selectionLength);
        Assert.Equal(96, cursorSlot + selectionLength);
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
