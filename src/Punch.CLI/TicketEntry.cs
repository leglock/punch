namespace Punch.CLI;

// One row in the ticket picker: a ticket number/key and its human-readable
// title. Rows come either from the manually-maintained tickets list
// (~/.punch/tickets.txt) or, with FromLog set, from a ticket already used on
// the open day — in which case the title is the block's own description.
internal sealed record TicketEntry(string Ticket, string Title, bool FromLog = false);
