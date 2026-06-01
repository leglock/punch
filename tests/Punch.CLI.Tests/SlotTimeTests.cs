using Punch.CLI;
using Xunit;

namespace Punch.CLI.Tests;

public class SlotTimeTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(34, 8, 30)]
    [InlineData(95, 23, 45)]
    [InlineData(96, 24, 0)]
    public void ToTime_ConvertsSlotToHoursAndMinutes(int slot, int expectedHours, int expectedMinutes)
    {
        var (h, m) = SlotTime.ToTime(slot);
        Assert.Equal(expectedHours, h);
        Assert.Equal(expectedMinutes, m);
    }

    [Theory]
    [InlineData(34, 38, "08:30–09:30")]
    [InlineData(0, 4, "00:00–01:00")]
    [InlineData(92, 96, "23:00–24:00")]
    public void FormatRange_FormatsStartAndEndSlots(int startSlot, int endSlot, string expected)
    {
        Assert.Equal(expected, SlotTime.FormatRange(startSlot, endSlot));
    }
}
