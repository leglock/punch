namespace Punch.CLI;

// The built-in color themes plus any user themes from settings.json, in the
// order F5/Ctrl+R cycles through them.
internal static class ThemeCatalog
{
    // The original orange look, and the base every other theme falls back to.
    public static Theme Ember { get; } = Theme.Resolve("ember", new ThemeColors
    {
        BlockPrimary = "orangered1",
        BlockAlternate = "orange3",
        BlockNonBillable = "grey50",
        Selection = "yellow",
        SelectionBlock = "white",
        NowMarker = "orangered1",
        Ticket = "cyan",
        Highlight = "yellow",
        StatusBarBackground = "orangered1",
        StatusBarText = "white",
        StatusBarAccent = "yellow",
        GaugeFilled = "yellow",
        Logo = new() { "red", "orangered1", "darkorange", "orange3", "orange1" },
    }, fallback: null);

    // The light themes use a pale status bar, so its text, key hints and gauge
    // switch to dark shades to stay readable.
    private static readonly Theme Meadow = Theme.Resolve("meadow", new ThemeColors
    {
        BlockPrimary = "#87d787",
        BlockAlternate = "#5faf5f",
        NowMarker = "#87d787",
        StatusBarBackground = "#87d787",
        StatusBarText = "#121212",
        StatusBarAccent = "#005f00",
        GaugeFilled = "#005f00",
        Logo = new() { "#00af5f", "#5faf5f", "#5fd787", "#87d787", "#afffaf" },
    }, Ember);

    private static readonly Theme Glacier = Theme.Resolve("glacier", new ThemeColors
    {
        BlockPrimary = "#87d7ff",
        BlockAlternate = "#5fafd7",
        NowMarker = "#87d7ff",
        Ticket = "#d7afff",
        StatusBarBackground = "#87d7ff",
        StatusBarText = "#121212",
        StatusBarAccent = "#005f87",
        GaugeFilled = "#005f87",
        Logo = new() { "#005f87", "#0087af", "#5fafd7", "#87d7ff", "#afffff" },
    }, Ember);

    private static readonly Theme Blossom = Theme.Resolve("blossom", new ThemeColors
    {
        BlockPrimary = "#ff8787",
        BlockAlternate = "#d75f5f",
        NowMarker = "#ff8787",
        StatusBarBackground = "#ff8787",
        StatusBarText = "#121212",
        StatusBarAccent = "#870000",
        GaugeFilled = "#870000",
        Logo = new() { "#af0000", "#d75f5f", "#ff5f5f", "#ff8787", "#ffafaf" },
    }, Ember);

    public static IReadOnlyList<Theme> BuiltIn { get; } = new[] { Ember, Meadow, Glacier, Blossom };

    // Merges user themes over the built-ins. A user theme named like a built-in
    // (case-insensitive) replaces it in place and falls back to that built-in's
    // colors; a new name is appended in file order and falls back to Ember.
    // Blank names and null entries are skipped.
    public static IReadOnlyList<Theme> Build(IReadOnlyDictionary<string, ThemeColors?>? userThemes)
    {
        var themes = BuiltIn.ToList();
        if (userThemes is null)
            return themes;

        foreach (var (rawName, colors) in userThemes)
        {
            var name = rawName.Trim();
            if (name.Length == 0 || colors is null)
                continue;

            var existing = themes.FindIndex(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
                themes[existing] = Theme.Resolve(themes[existing].Name, colors, themes[existing]);
            else
                themes.Add(Theme.Resolve(name, colors, Ember));
        }
        return themes;
    }

    // Finds a theme by name (case-insensitive); unknown or missing names
    // resolve to the first theme.
    public static int IndexOf(IReadOnlyList<Theme> themes, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return 0;
        for (var i = 0; i < themes.Count; i++)
        {
            if (string.Equals(themes[i].Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return 0;
    }
}
