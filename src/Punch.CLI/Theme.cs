using Spectre.Console;

namespace Punch.CLI;

// A fully resolved color theme. Each color is stored as a Spectre markup color
// string (e.g. "orangered1" or "#87D787") ready to drop into a markup tag.
// Muted text (hints, durations, the empty track) is a fixed grey in PunchView
// and deliberately not themeable.
internal sealed class Theme
{
    private const int LogoLetters = 5; // "punch"

    private Theme(string name)
    {
        Name = name;
    }

    public string Name { get; }
    public string BlockPrimary { get; private init; } = "";
    public string BlockAlternate { get; private init; } = "";
    public string BlockNonBillable { get; private init; } = "";
    public string Selection { get; private init; } = "";
    public string SelectionBlock { get; private init; } = "";
    public string NowMarker { get; private init; } = "";
    public string Ticket { get; private init; } = "";
    public string Highlight { get; private init; } = "";
    public string StatusBarBackground { get; private init; } = "";
    public string StatusBarText { get; private init; } = "";
    public string StatusBarAccent { get; private init; } = "";
    public string GaugeFilled { get; private init; } = "";
    public IReadOnlyList<string> Logo { get; private init; } = Array.Empty<string>();

    // Resolves a theme from its configured colors. Missing or invalid values
    // take the fallback theme's color; with no fallback every value must be
    // valid (used for the built-in base theme).
    public static Theme Resolve(string name, ThemeColors colors, Theme? fallback)
    {
        string Pick(string? value, Func<Theme, string> fromFallback) =>
            ParseColor(value)
            ?? (fallback is not null ? fromFallback(fallback) : throw new ArgumentException($"Theme '{name}' has an invalid color '{value}'."));

        return new Theme(name)
        {
            BlockPrimary = Pick(colors.BlockPrimary, t => t.BlockPrimary),
            BlockAlternate = Pick(colors.BlockAlternate, t => t.BlockAlternate),
            BlockNonBillable = Pick(colors.BlockNonBillable, t => t.BlockNonBillable),
            Selection = Pick(colors.Selection, t => t.Selection),
            SelectionBlock = Pick(colors.SelectionBlock, t => t.SelectionBlock),
            NowMarker = Pick(colors.NowMarker, t => t.NowMarker),
            Ticket = Pick(colors.Ticket, t => t.Ticket),
            Highlight = Pick(colors.Highlight, t => t.Highlight),
            StatusBarBackground = Pick(colors.StatusBarBackground, t => t.StatusBarBackground),
            StatusBarText = Pick(colors.StatusBarText, t => t.StatusBarText),
            StatusBarAccent = Pick(colors.StatusBarAccent, t => t.StatusBarAccent),
            GaugeFilled = Pick(colors.GaugeFilled, t => t.GaugeFilled),
            Logo = ResolveLogo(name, colors.Logo, fallback),
        };
    }

    // Logo colors repeat when fewer than five are given; an invalid entry takes
    // the fallback's color for that letter.
    private static string[] ResolveLogo(string name, List<string>? logo, Theme? fallback)
    {
        var result = new string[LogoLetters];
        for (var i = 0; i < LogoLetters; i++)
        {
            var value = logo is { Count: > 0 } ? logo[i % logo.Count] : null;
            result[i] = ParseColor(value)
                ?? fallback?.Logo[i]
                ?? throw new ArgumentException($"Theme '{name}' has an invalid logo color '{value}'.");
        }
        return result;
    }

    // Parses a single color ("#ff5f00", "#f50", "rgb(255,95,0)", or a Spectre
    // color name) into its markup form. Returns null for blanks, unknown names,
    // and anything that isn't a plain foreground color ("bold red", "red on
    // blue", "default").
    internal static string? ParseColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!Style.TryParse(value.Trim(), out var style) || style is null)
            return null;

        if (style.Decoration != Decoration.None
            || style.Background != Color.Default
            || style.Foreground == Color.Default)
            return null;

        return style.Foreground.ToMarkup();
    }
}
