using Imobisoft.Search.Models;

namespace Imobisoft.Search.Services.Querying;

/// <summary>
/// Turns a disappointing search into a useful one: "did you mean" corrections, and type-ahead
/// suggestions.
/// <para>
/// Both are built from what is actually in the index rather than from a dictionary, so they only
/// ever suggest something the site can answer. Suggesting a correction that also returns nothing
/// is worse than suggesting nothing at all.
/// </para>
/// </summary>
internal static class SuggestionEngine
{
    /// <summary>
    /// Picks the best correction for a term from the names of documents a fuzzy search found.
    /// Returns null when nothing is close enough to be worth offering.
    /// </summary>
    public static string? ChooseCorrection(
        string term,
        IReadOnlyList<SearchResultItem> fuzzyMatches,
        SuggestionSettings settings)
    {
        var words = term.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length == 0 || fuzzyMatches.Count == 0)
        {
            return null;
        }

        // Correct each word independently, so "prodct serch" can become "product search".
        var corrected = new List<string>(words.Length);
        var changed = false;

        // Candidate words come from the names of what the fuzzy pass found, which keeps the
        // vocabulary to things the site actually contains.
        var vocabulary = fuzzyMatches
            .SelectMany(m => Tokenize(m.Name))
            .Concat(fuzzyMatches.SelectMany(m => m.Fields.TryGetValue(ImobisoftSearchConstants.IndexFields.NodeName, out var n) ? Tokenize(n) : Array.Empty<string>()))
            .Where(w => w.Length > 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var word in words)
        {
            var best = BestMatch(word, vocabulary, settings.MaximumEditDistance);

            if (best is null || best.Equals(word, StringComparison.OrdinalIgnoreCase))
            {
                corrected.Add(word);
                continue;
            }

            corrected.Add(best);
            changed = true;
        }

        return changed ? string.Join(' ', corrected) : null;
    }

    /// <summary>
    /// Collapses a set of matches into type-ahead suggestions, most relevant first.
    /// <para>
    /// Words the typing completes come first, then the whole page names they came from. Someone
    /// typing "ap" is part way through a word, so "apartments" is what they are reaching for;
    /// offering only "Apartments To Rent In Didsbury" makes them read a sentence to find out whether
    /// their word is in it. The page names still follow, because sometimes the page is the answer.
    /// </para>
    /// <para>
    /// Both come from the names of documents this search already matched, so every suggestion is
    /// something the site can answer.
    /// </para>
    /// </summary>
    public static IReadOnlyList<SearchSuggestion> BuildAutocomplete(
        IReadOnlyList<SearchResultItem> matches,
        int take,
        string? typed = null)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var suggestions = new List<SearchSuggestion>();

        List<SearchResultItem> ranked = matches.OrderByDescending(m => m.Score).ToList();

        foreach (SearchSuggestion word in CompletedWords(ranked, typed))
        {
            if (!seen.Add(word.Text))
            {
                continue;
            }

            suggestions.Add(word);

            if (suggestions.Count >= take)
            {
                return suggestions;
            }
        }

        foreach (SearchResultItem match in ranked)
        {
            if (string.IsNullOrWhiteSpace(match.Name) || !seen.Add(match.Name))
            {
                continue;
            }

            suggestions.Add(new SearchSuggestion
            {
                Text = match.Name,
                NodeKey = match.Key,
                Url = match.Url,
                ContentTypeAlias = match.ContentTypeAlias,
                Score = match.Score,
            });

            if (suggestions.Count >= take)
            {
                break;
            }
        }

        return suggestions;
    }

    /// <summary>
    /// The words in the matched names that finish the word being typed, each offered as the whole
    /// query it would make - type "rent ap" and the suggestion is "rent apartments", not "apartments",
    /// so accepting one does not throw away what was already typed.
    /// </summary>
    private static IEnumerable<SearchSuggestion> CompletedWords(
        IReadOnlyList<SearchResultItem> ranked,
        string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed))
        {
            yield break;
        }

        var trimmed = typed.TrimStart();

        // A trailing space means the word is finished, so there is nothing to complete.
        if (char.IsWhiteSpace(trimmed[^1]))
        {
            yield break;
        }

        var lastSpace = trimmed.LastIndexOf(' ');
        var prefix = trimmed[(lastSpace + 1)..];
        var alreadyTyped = trimmed[..(lastSpace + 1)];

        foreach (SearchResultItem match in ranked)
        {
            foreach (var token in Tokenize(match.Name))
            {
                // Only forwards: a word the typing is inside the middle of is a different word, and
                // offering it would move the cursor backwards.
                if (token.Length <= prefix.Length
                    || !token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                yield return new SearchSuggestion
                {
                    Text = alreadyTyped + token,
                    NodeKey = match.Key,
                    Url = match.Url,
                    ContentTypeAlias = match.ContentTypeAlias,
                    Score = match.Score,
                };
            }
        }
    }

    private static IEnumerable<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        foreach (var token in text.Split(
                     new[] { ' ', ',', '.', ':', ';', '-', '/', '\\', '(', ')', '"', '\'', '\t', '\n', '\r' },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            yield return token;
        }
    }

    /// <summary>
    /// The closest word by edit distance, provided it is close enough to be a plausible typo rather
    /// than a different word that happened to appear in a matching document.
    /// </summary>
    private static string? BestMatch(string word, IReadOnlyList<string> vocabulary, int maximumDistance)
    {
        // A short word tolerates fewer mistakes: at three letters, two edits is a different word.
        var allowed = Math.Min(maximumDistance, Math.Max(1, word.Length / 3));

        string? best = null;
        var bestDistance = int.MaxValue;

        foreach (var candidate in vocabulary)
        {
            // Length alone rules most candidates out before the expensive comparison.
            if (Math.Abs(candidate.Length - word.Length) > allowed)
            {
                continue;
            }

            var distance = EditDistance(word, candidate, allowed);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;

                if (distance == 0)
                {
                    break;
                }
            }
        }

        return bestDistance <= allowed ? best : null;
    }

    /// <summary>
    /// Levenshtein distance, abandoned early once every cell in a row exceeds the ceiling - at which
    /// point the final distance cannot come back under it.
    /// </summary>
    private static int EditDistance(string a, string b, int ceiling)
    {
        if (a.Length == 0)
        {
            return b.Length;
        }

        if (b.Length == 0)
        {
            return a.Length;
        }

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            var rowMinimum = current[0];

            for (var j = 1; j <= b.Length; j++)
            {
                var substitution = char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;

                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + substitution);

                rowMinimum = Math.Min(rowMinimum, current[j]);
            }

            if (rowMinimum > ceiling)
            {
                return ceiling + 1;
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
