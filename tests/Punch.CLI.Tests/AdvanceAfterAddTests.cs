using Punch.CLI;
using Xunit;

namespace Punch.CLI.Tests;

public class AdvanceAfterAddTests
{
    [Fact]
    public void AdvancesToNextFreeSlot()
    {
        var occupied = new bool[96];
        occupied[4] = true;
        var blocks = new List<TimeBlock> { new(4, 1, "existing", "") };

        var (cursorSlot, selectionLength, selectedBlock) =
            PunchCommand.AdvanceAfterAdd(4, blocks, occupied);

        Assert.Equal(5, cursorSlot);
        Assert.Equal(1, selectionLength);
        Assert.Null(selectedBlock);
    }

    [Fact]
    public void SkipsOverMultipleOccupiedSlots()
    {
        var occupied = new bool[96];
        for (var s = 4; s < 10; s++) occupied[s] = true;
        var blocks = new List<TimeBlock>
        {
            new(4, 3, "block-a", ""),
            new(7, 3, "block-b", ""),
        };

        var (cursorSlot, selectionLength, selectedBlock) =
            PunchCommand.AdvanceAfterAdd(4, blocks, occupied);

        Assert.Equal(10, cursorSlot);
        Assert.Equal(1, selectionLength);
        Assert.Null(selectedBlock);
    }

    [Fact]
    public void FallsBackToSlot95WhenDayIsFull_SelectsExistingBlock()
    {
        var occupied = new bool[96];
        for (var s = 90; s < 96; s++) occupied[s] = true;
        var existingBlock = new TimeBlock(92, 4, "last-block", "TICKET");
        var blocks = new List<TimeBlock>
        {
            new(90, 2, "almost-last", ""),
            existingBlock,
        };

        var (cursorSlot, selectionLength, selectedBlock) =
            PunchCommand.AdvanceAfterAdd(90, blocks, occupied);

        Assert.Equal(95, cursorSlot);
        Assert.Equal(existingBlock.Length, selectionLength);
        Assert.Same(existingBlock, selectedBlock);
    }

    [Fact]
    public void StaysAtCurrentSlotIfFree()
    {
        var occupied = new bool[96];
        var blocks = new List<TimeBlock>();

        var (cursorSlot, selectionLength, selectedBlock) =
            PunchCommand.AdvanceAfterAdd(20, blocks, occupied);

        Assert.Equal(20, cursorSlot);
        Assert.Equal(1, selectionLength);
        Assert.Null(selectedBlock);
    }
}