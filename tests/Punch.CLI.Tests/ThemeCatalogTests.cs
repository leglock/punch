using System.Collections.Generic;
using System.Linq;
using Punch.CLI;
using Xunit;

namespace Punch.CLI.Tests;

public class ThemeCatalogTests
{
    [Fact]
    public void Build_NullYieldsBuiltInsInOrder()
    {
        var names = ThemeCatalog.Build(null).Select(t => t.Name);

        Assert.Equal(new[] { "ember", "meadow", "glacier", "blossom" }, names);
    }

    [Fact]
    public void Build_AppendsUserThemesAfterBuiltIns()
    {
        var themes = ThemeCatalog.Build(new Dictionary<string, ThemeColors?>
        {
            ["dusk"] = new() { BlockPrimary = "#5f00af" },
            ["noir"] = new() { BlockPrimary = "#ffffff" },
        });

        Assert.Equal(new[] { "ember", "meadow", "glacier", "blossom", "dusk", "noir" }, themes.Select(t => t.Name));
        Assert.Equal("#5F00AF", themes[4].BlockPrimary);
        // Keys a user theme leaves out come from Ember.
        Assert.Equal(ThemeCatalog.Ember.StatusBarBackground, themes[4].StatusBarBackground);
    }

    [Fact]
    public void Build_UserThemeNamedLikeBuiltInReplacesItInPlaceAndInheritsFromIt()
    {
        var builtInGlacier = ThemeCatalog.BuiltIn.Single(t => t.Name == "glacier");

        var themes = ThemeCatalog.Build(new Dictionary<string, ThemeColors?>
        {
            ["Glacier"] = new() { Ticket = "#ffffff" },
        });

        Assert.Equal(4, themes.Count);
        var glacier = themes[2];
        Assert.Equal("glacier", glacier.Name);
        Assert.Equal("#FFFFFF", glacier.Ticket);
        Assert.Equal(builtInGlacier.BlockPrimary, glacier.BlockPrimary);
    }

    [Fact]
    public void Build_SkipsBlankNamesAndNullEntries()
    {
        var themes = ThemeCatalog.Build(new Dictionary<string, ThemeColors?>
        {
            ["  "] = new() { BlockPrimary = "#000000" },
            ["empty"] = null,
        });

        Assert.Equal(4, themes.Count);
    }

    [Theory]
    [InlineData("glacier", 2)]
    [InlineData("BLOSSOM", 3)]
    [InlineData(" meadow ", 1)]
    [InlineData("unknown", 0)]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    public void IndexOf_MatchesCaseInsensitivelyAndDefaultsToFirst(string? name, int expected)
    {
        Assert.Equal(expected, ThemeCatalog.IndexOf(ThemeCatalog.BuiltIn, name));
    }
}
