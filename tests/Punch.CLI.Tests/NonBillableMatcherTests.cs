using System.Collections.Generic;
using Punch.CLI;
using Xunit;

namespace Punch.CLI.Tests;

public class NonBillableMatcherTests
{
    [Theory]
    [InlineData("Lunch")]
    [InlineData("LUNCH break")]
    [InlineData("team lunch")]
    [InlineData("Out for Lunch with the crew")]
    [InlineData("Break")]
    [InlineData("coffee BREAK")]
    [InlineData("short break with the team")]
    [InlineData("lunch!")]
    [InlineData("break-time")]
    [InlineData("lunch/break")]
    [InlineData("my lunch.")]
    [InlineData("LuNcH")]
    [InlineData("bReAk")]
    public void Default_DetectsLunchAndBreakLabelsCaseInsensitive(string label)
    {
        Assert.True(NonBillableMatcher.Default.IsNonBillable(label));
    }

    [Theory]
    [InlineData("")]
    [InlineData("meeting")]
    [InlineData("standup")]
    [InlineData("luncheon")]
    [InlineData("breakfast")]
    [InlineData("breaking changes")]
    [InlineData("system breakdown")]
    [InlineData("lunchbox")]
    [InlineData("lunch1")]
    [InlineData("1break")]
    [InlineData("   ")]
    public void Default_ReturnsFalseForNonUnpaidLabelsAndSubstrings(string label)
    {
        Assert.False(NonBillableMatcher.Default.IsNonBillable(label));
    }

    [Fact]
    public void Create_NullRules_BehavesLikeDefault()
    {
        var matcher = NonBillableMatcher.Create(null);

        Assert.True(matcher.IsNonBillable("team lunch"));
        Assert.True(matcher.IsNonBillable("coffee break"));
        Assert.False(matcher.IsNonBillable("breakfast"));
    }

    [Fact]
    public void Create_EmptyRules_MatchesNothing()
    {
        var matcher = NonBillableMatcher.Create(new List<NonBillableRule>());

        Assert.False(matcher.IsNonBillable("lunch"));
        Assert.False(matcher.IsNonBillable("break"));
    }

    [Fact]
    public void WordMode_MatchesWholeWordsOnly()
    {
        var matcher = NonBillableMatcher.Create(new[] { new NonBillableRule { Word = "standup" } });

        Assert.True(matcher.IsNonBillable("daily STANDUP"));
        Assert.True(matcher.IsNonBillable("standup"));
        Assert.False(matcher.IsNonBillable("standups"));
        Assert.False(matcher.IsNonBillable("lunch"));
    }

    [Fact]
    public void ExactMode_RequiresFullLabelEqualityIgnoringCaseAndWhitespace()
    {
        var matcher = NonBillableMatcher.Create(new[] { new NonBillableRule { Word = "afk", Match = "exact" } });

        Assert.True(matcher.IsNonBillable("AFK"));
        Assert.True(matcher.IsNonBillable("  afk  "));
        Assert.False(matcher.IsNonBillable("afk meeting"));
    }

    [Fact]
    public void ExactMode_HandlesRegexMetacharactersLiterally()
    {
        var matcher = NonBillableMatcher.Create(new[] { new NonBillableRule { Word = "1:1", Match = "exact" } });

        Assert.True(matcher.IsNonBillable("1:1"));
        Assert.False(matcher.IsNonBillable("101"));
    }

    [Fact]
    public void WordMode_EscapesRegexMetacharacters()
    {
        var matcher = NonBillableMatcher.Create(new[] { new NonBillableRule { Word = "on-call" } });

        Assert.True(matcher.IsNonBillable("on-call rotation"));
        Assert.False(matcher.IsNonBillable("onXcall rotation"));
    }

    [Fact]
    public void Create_SkipsRulesWithUnknownMatchMode()
    {
        var matcher = NonBillableMatcher.Create(new[]
        {
            new NonBillableRule { Word = "lunch", Match = "substring" },
        });

        Assert.False(matcher.IsNonBillable("lunch"));
    }

    [Fact]
    public void Create_SkipsRulesWithBlankWords()
    {
        var matcher = NonBillableMatcher.Create(new[]
        {
            new NonBillableRule { Word = "   " },
            new NonBillableRule { Word = "", Match = "exact" },
            new NonBillableRule { Word = "break" },
        });

        Assert.True(matcher.IsNonBillable("coffee break"));
        Assert.False(matcher.IsNonBillable("   "));
        Assert.False(matcher.IsNonBillable(""));
    }

    [Fact]
    public void Create_AcceptsMatchModeCaseInsensitively()
    {
        var matcher = NonBillableMatcher.Create(new[]
        {
            new NonBillableRule { Word = "lunch", Match = "WORD" },
            new NonBillableRule { Word = "afk", Match = "Exact" },
        });

        Assert.True(matcher.IsNonBillable("team lunch"));
        Assert.True(matcher.IsNonBillable("AFK"));
    }
}
