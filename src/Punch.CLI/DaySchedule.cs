namespace Punch.CLI;

// Owns a day's booked blocks together with the 96-slot occupancy mask, keeping
// the two in sync so callers never manipulate the mask directly. Slots are
// quarter-hour indices 0..95.
internal sealed class DaySchedule
{
    private readonly List<TimeBlock> _blocks;
    private readonly bool[] _occupied = new bool[96];

    public DaySchedule(IEnumerable<TimeBlock> blocks)
    {
        _blocks = blocks.ToList();
        foreach (var block in _blocks)
            SetOccupied(block, true);
    }

    public IReadOnlyList<TimeBlock> Blocks => _blocks;

    public int Count => _blocks.Count;

    public bool IsFree(int slot) => slot is >= 0 and < 96 && !_occupied[slot];

    public bool IsOverlapping(int start, int length)
    {
        for (var i = start; i < start + length && i < 96; i++)
            if (_occupied[i])
                return true;
        return false;
    }

    public TimeBlock? FindAt(int slot) =>
        _blocks.FirstOrDefault(b => slot >= b.StartSlot && slot < b.StartSlot + b.Length);

    // True when the slot immediately after the block's current span is free,
    // i.e. the block can grow by one slot.
    public bool CanGrow(int startSlot, int currentLength)
    {
        var next = startSlot + currentLength;
        return next < 96 && !_occupied[next];
    }

    public void Add(TimeBlock block)
    {
        _blocks.Add(block);
        SetOccupied(block, true);
    }

    public void Remove(TimeBlock block)
    {
        if (_blocks.Remove(block))
            SetOccupied(block, false);
    }

    // Swaps oldBlock for newBlock in place, re-deriving occupancy from both spans.
    // Returns newBlock for convenient reassignment of the caller's reference.
    public TimeBlock Replace(TimeBlock oldBlock, TimeBlock newBlock)
    {
        var idx = _blocks.IndexOf(oldBlock);
        if (idx < 0)
            return oldBlock;
        SetOccupied(oldBlock, false);
        _blocks[idx] = newBlock;
        SetOccupied(newBlock, true);
        return newBlock;
    }

    // Temporarily clears a block's occupancy without removing it from the list,
    // so an in-progress edit can preview a shrink/grow in the timeline. Pair with
    // FillSlots to restore (or call Replace when committing the edit).
    public void FreeSlots(TimeBlock block) => SetOccupied(block, false);

    public void FillSlots(TimeBlock block) => SetOccupied(block, true);

    // Finds where the cursor should land after booking a block at cursorSlot:
    // the next free slot, or slot 95 (selecting the block there) when the day is full.
    public (int cursorSlot, int selectionLength, TimeBlock? selectedBlock) AdvanceAfterAdd(int cursorSlot)
    {
        var selectionLength = 1;
        TimeBlock? selectedBlock = null;

        while (cursorSlot < 96 && _occupied[cursorSlot])
            cursorSlot++;
        if (cursorSlot >= 96)
        {
            cursorSlot = 95;
            if (_occupied[95])
            {
                var adj = FindAt(95);
                if (adj != null)
                {
                    selectionLength = adj.Length;
                    selectedBlock = adj;
                }
            }
        }

        return (cursorSlot, selectionLength, selectedBlock);
    }

    private void SetOccupied(TimeBlock block, bool value)
    {
        for (var s = block.StartSlot; s < block.StartSlot + block.Length; s++)
            _occupied[s] = value;
    }
}
