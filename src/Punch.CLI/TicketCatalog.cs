namespace Punch.CLI;

// Builds the ticket picker's row list by merging the manually-maintained
// tickets list with any ticket already used on the open day. Those day-log
// tickets have no title on disk, so the description of the block using them
// stands in as the title.
internal static class TicketCatalog
{
    // Day-log tickets come first (in timeline order), then the saved rows in
    // file order. A ticket already present in `saved` is not repeated. The
    // result is flat and fully selectable — the section headers are drawn by
    // the view off TicketEntry.FromLog, so picker cursor arithmetic is unaffected.
    public static List<TicketEntry> Build(IReadOnlyList<TicketEntry> saved, IReadOnlyList<TimeBlock> blocks)
    {
        // tickets.txt is hand-written while ticket input is auto-uppercased, so
        // matching has to ignore case.
        var known = new HashSet<string>(saved.Select(t => t.Ticket), StringComparer.OrdinalIgnoreCase);

        var fromLog = blocks
            .Where(b => !string.IsNullOrWhiteSpace(b.Ticket) && !known.Contains(b.Ticket.Trim()))
            .GroupBy(b => b.Ticket.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                // One ticket can span several blocks with different descriptions;
                // the longest-running block is the most representative.
                var best = g.OrderByDescending(b => b.Length).ThenBy(b => b.StartSlot).First();
                return new
                {
                    Entry = new TicketEntry(best.Ticket.Trim(), best.Label, FromLog: true),
                    FirstSlot = g.Min(b => b.StartSlot)
                };
            })
            .OrderBy(x => x.FirstSlot)
            .Select(x => x.Entry);

        return fromLog.Concat(saved).ToList();
    }
}
