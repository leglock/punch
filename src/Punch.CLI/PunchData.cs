namespace Punch.CLI;

internal sealed class PunchData
{
    public List<TimeBlockDto> Blocks { get; set; } = new();
}

internal sealed class TimeBlockDto
{
    public int StartSlot { get; set; }
    public int Length { get; set; }
    public string Label { get; set; } = "";
    public string Ticket { get; set; } = "";
}
