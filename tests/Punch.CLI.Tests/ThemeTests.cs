using System.Collections.Generic;
using Punch.CLI;
using Xunit;

namespace Punch.CLI.Tests;

public class ThemeTests
{
    [Theory]
    [InlineData("#ff5f00", "#FF5F00")]
    [InlineData("  #87d787  ", "#87D787")]
    [InlineData("rgb(255,95,0)", "#FF5F00")]
    [InlineData("orangered1", "orangered1")]
    [InlineData("cyan", "aqua")] // Spectre's canonical name for color 14
    public void ParseColor_AcceptsHexRgbAndNames(string input, string expected)
    {
        Assert.Equal(expected, Theme.ParseColor(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("notacolor")]
    [InlineData("#zzzzzz")]
    [InlineData("bold red")]
    [InlineData("red on blue")]
    [InlineData("default")]
    public void ParseColor_RejectsNonColors(string? input)
    {
        Assert.Null(Theme.ParseColor(input));
    }

    [Fact]
    public void Resolve_MissingAndInvalidKeysFallBack()
    {
        var theme = Theme.Resolve("custom", new ThemeColors
        {
            BlockPrimary = "#112233",
            Ticket = "not-a-color",
        }, ThemeCatalog.Ember);

        Assert.Equal("custom", theme.Name);
        Assert.Equal("#112233", theme.BlockPrimary);
        Assert.Equal(ThemeCatalog.Ember.Ticket, theme.Ticket);
        Assert.Equal(ThemeCatalog.Ember.StatusBarBackground, theme.StatusBarBackground);
        Assert.Equal(ThemeCatalog.Ember.Logo, theme.Logo);
    }

    [Fact]
    public void Resolve_SingleLogoColorAppliesToEveryLetter()
    {
        var theme = Theme.Resolve("custom", new ThemeColors { Logo = new List<string> { "#ffffff" } }, ThemeCatalog.Ember);

        Assert.All(theme.Logo, c => Assert.Equal("#FFFFFF", c));
        Assert.Equal(5, theme.Logo.Count);
    }

    [Fact]
    public void Resolve_InvalidLogoEntryFallsBackPerLetter()
    {
        var theme = Theme.Resolve("custom", new ThemeColors
        {
            Logo = new List<string> { "#111111", "bogus", "#333333", "#444444", "#555555" },
        }, ThemeCatalog.Ember);

        Assert.Equal("#111111", theme.Logo[0]);
        Assert.Equal(ThemeCatalog.Ember.Logo[1], theme.Logo[1]);
        Assert.Equal("#555555", theme.Logo[4]);
    }

    [Fact]
    public void Ember_KeepsTheOriginalColors()
    {
        var ember = ThemeCatalog.Ember;

        Assert.Equal("orangered1", ember.BlockPrimary);
        Assert.Equal("orange3", ember.BlockAlternate);
        Assert.Equal("grey50", ember.BlockNonBillable);
        Assert.Equal("orangered1", ember.StatusBarBackground);
        Assert.Equal(new[] { "red", "orangered1", "darkorange", "orange3", "orange1" }, ember.Logo);
    }
}
