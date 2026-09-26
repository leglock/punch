namespace Punch.CLI;

// One theme's colors as written in the "themes" section of ~/.punch/settings.json
// (and how the built-in themes are declared). Every key is optional: a missing
// or unparseable value falls back to the base theme's color. Values are any
// color Spectre understands — "#ff5f00", "rgb(255,95,0)", or a name like
// "orangered1".
internal sealed class ThemeColors
{
    public string? BlockPrimary { get; set; }
    public string? BlockAlternate { get; set; }
    public string? BlockNonBillable { get; set; }
    public string? Selection { get; set; }
    public string? SelectionBlock { get; set; }
    public string? NowMarker { get; set; }
    public string? Ticket { get; set; }
    public string? Highlight { get; set; }
    public string? StatusBarBackground { get; set; }
    public string? StatusBarText { get; set; }
    public string? StatusBarAccent { get; set; }
    public string? GaugeFilled { get; set; }

    // Colors for the letters of the "punch" logo in the help panel, applied
    // in order and repeated if there are fewer than five.
    public List<string>? Logo { get; set; }
}
