namespace Punch.CLI;

internal sealed record TimeBlock(int StartSlot, int Length, string Label, string Ticket = "");
