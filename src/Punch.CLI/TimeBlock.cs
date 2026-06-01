using System.Text.RegularExpressions;

namespace Punch.CLI;

internal sealed record TimeBlock(int StartSlot, int Length, string Label, string Ticket = "")
{
    private static readonly Regex UnpaidRegex =
        new(@"\b(lunch|break)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public bool IsUnpaid => UnpaidRegex.IsMatch(Label);
}
