namespace Punch.CLI;

// One user-configured non-billable rule from the "nonBillable" list in
// ~/.punch/settings.json. Word is the text to look for in a block's label;
// Match selects the mode: "word" (whole-word match anywhere in the label,
// the default) or "exact" (the whole label must equal the word).
internal sealed class NonBillableRule
{
    public string Word { get; set; } = "";
    public string Match { get; set; } = "word";
}
