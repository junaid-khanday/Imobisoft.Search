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
    /// Collapses a set of matches into type-ahead suggestions: one per distinct page name, most
    /// relevant first.
    /// </summary>
    public static IReadOnlyList<SearchSuggestion> BuildAutocomplete(
        IReadOnlyList<SearchResultItem> matches,
        int take)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var suggestions = new List<SearchSuggestion>();

        foreach (SearchResultItem match in matches.OrderByDescending(m => m.Score))
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
