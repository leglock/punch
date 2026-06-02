namespace Punch.CLI;

// One row from the manually-maintained tickets list (~/.punch/tickets.txt):
// a ticket number/key and its human-readable title. Used by the ticket picker
// overlay to assign a ticket to a selected block.
internal sealed record TicketEntry(string Ticket, string Title);
