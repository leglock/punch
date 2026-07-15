using System.Text.RegularExpressions;

namespace Punch.CLI;

// Decides whether a block's label marks it as non-billable, based on the
// user's "nonBillable" rules from settings.json. Built once at load; matching
// is case-insensitive. Word-mode words rely on regex \b boundaries, so words
// that start or end with non-word characters may match more loosely — use
// "exact" mode for fully literal matching.
internal sealed class NonBillableMatcher
{
    // Matches the historical hard-coded behavior: whole-word "lunch" or "break".
    public static NonBillableMatcher Default { get; } = Create(new[]
    {
        new NonBillableRule { Word = "lunch" },
        new NonBillableRule { Word = "break" },
    })!;

    private readonly Regex? _wordRegex;
    private readonly List<string> _exactWords;

    private NonBillableMatcher(Regex? wordRegex, List<string> exactWords)
    {
        _wordRegex = wordRegex;
        _exactWords = exactWords;
    }

    // Builds a matcher from user rules. A null list (key absent from
    // settings.json) yields the defaults; an empty list matches nothing.
    // Rules with a blank word or an unrecognized match mode are skipped.
    public static NonBillableMatcher Create(IReadOnlyList<NonBillableRule>? rules)
    {
        if (rules is null)
            return Default;

        var wordPatterns = new List<string>();
        var exactWords = new List<string>();
        foreach (var rule in rules)
        {
            var word = rule.Word?.Trim() ?? "";
            if (word.Length == 0)
                continue;

            if (string.Equals(rule.Match, "word", StringComparison.OrdinalIgnoreCase))
                wordPatterns.Add(Regex.Escape(word));
            else if (string.Equals(rule.Match, "exact", StringComparison.OrdinalIgnoreCase))
                exactWords.Add(word);
        }

        var wordRegex = wordPatterns.Count > 0
            ? new Regex($@"\b(?:{string.Join("|", wordPatterns)})\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled)
            : null;
        return new NonBillableMatcher(wordRegex, exactWords);
    }

    public bool IsNonBillable(string label)
    {
        if (_wordRegex is not null && _wordRegex.IsMatch(label))
            return true;

        foreach (var word in _exactWords)
        {
            if (string.Equals(label.Trim(), word, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
