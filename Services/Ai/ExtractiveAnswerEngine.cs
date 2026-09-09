using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Imobisoft.Search.Models;

namespace Imobisoft.Search.Services.Ai;

/// <summary>
/// The keyless answer engine: builds the answer box out of the results the search already found,
/// with no API key, no account, no network call and no cost.
/// <para>
/// It reads each result, splits it into sentences, scores every sentence against what the visitor
/// asked, and stitches the best few together with a citation each. That makes it an <em>extractor</em>
/// rather than a writer: it can surface an answer a page states, and it will say so plainly when the
/// results do not contain one, but it cannot reason across pages or phrase something the content
/// never says. Adding a model key upgrades the same box to a written answer.
/// </para>
/// <para>
/// Because nothing leaves the server, this also suits sites that cannot send content to a third
/// party at all.
/// </para>
/// </summary>
internal sealed partial class ExtractiveAnswerEngine
{
    /// <summary>
    /// Below this share of the visitor's words, a sentence is a coincidence rather than an answer.
    /// The box says it found nothing direct instead of quoting something that merely shares a word.
    /// </summary>
    private const double ConfidentCoverage = 0.5;

    /// <summary>
    /// The share at which a sentence stands on its own, without its page's title having to vouch
    /// for it. See <see cref="IsConfident"/>.
    /// </summary>
    private const double SelfEvidentCoverage = 0.75;

    /// <summary>A sentence shorter than this is a heading or a fragment, not an answer.</summary>
    private const int MinimumSentenceWords = 5;

    private const int MaximumSentenceWords = 60;

    /// <summary>
    /// Words that carry the shape of a question rather than its subject, so matching one says
    /// nothing about relevance.
    /// <para>
    /// This is not just a stop list. Asked "how long does a refund take", the subject is "refund" -
    /// no page contains the words "long" or "take", and counting them as things to match makes a
    /// page that answers the question perfectly look like a one-word-in-three coincidence. Dropping
    /// the question frame is what lets coverage mean "how much of what they asked about".
    /// </para>
    /// </summary>
    private static readonly HashSet<string> Noise = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "about", "after", "again", "all", "also", "an", "and", "any", "are", "as", "at", "back",
        "be", "been", "before", "being", "both", "but", "by", "can", "did", "do", "does", "down",
        "during", "each", "few", "for", "from", "get", "give", "go", "had", "has", "have", "her",
        "here", "him", "his", "how", "i", "if", "in", "into", "is", "it", "its", "just", "long",
        "make", "many", "me", "more", "most", "much", "must", "my", "need", "no", "not", "now", "of",
        "off", "on", "once", "only", "or", "other", "our", "out", "over", "own", "put", "same",
        "should", "so", "some", "such", "take", "takes", "than", "that", "the", "their", "them",
        "then", "there", "these", "they", "this", "through", "to", "too", "under", "until", "up",
        "us", "use", "very", "want", "was", "we", "were", "what", "when", "where", "which", "while",
        "who", "why", "will", "with", "would", "you", "your",
    };

    /// <summary>
    /// Builds an answer, or null when there is nothing worth showing - no question, no results, or
    /// no readable text on any of them.
    /// </summary>
    public AiAnswer? Build(string term, IReadOnlyList<SearchResultItem> results, AiAnswerSettings settings)
    {
        if (string.IsNullOrWhiteSpace(term) || results.Count == 0)
        {
            return null;
        }

        List<string> queryTerms = Tokenise(term)
            .Where(t => !Noise.Contains(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Every word was noise ("how do I?"), so there is nothing to match sentences against and
        // any "answer" would be the first sentence of the first result dressed up as one.
        if (queryTerms.Count == 0)
        {
            return null;
        }

        var maxSources = Math.Clamp(settings.MaxSources, 1, 12);
        var maxWords = Math.Clamp(settings.MaxWords, 20, 400);
        List<SearchResultItem> sources = results.Take(maxSources).ToList();

        var candidates = new List<Candidate>();

        for (var i = 0; i < sources.Count; i++)
        {
            var text = ReadableText(sources[i]);

            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            // What the page's own title already establishes. A sentence on a page called
            // "Cancellations and refunds" does not have to repeat the word "cancellation" to be
            // about it, so the title's matches count towards the sentence's coverage.
            List<string> titleWords = Tokenise(sources[i].Name);
            var titleMatches = queryTerms.Where(t => titleWords.Any(w => Matches(w, t))).ToList();

            var position = 0;

            foreach (var raw in SplitSentences(text))
            {
                var sentence = Clean(raw);
                var words = Tokenise(sentence);

                // Counted on words that actually contain a letter, so a run of layout digits cannot
                // pad a fragment up to the minimum and get quoted as prose.
                var prose = words.Count(w => w.Any(char.IsLetter));

                if (prose < MinimumSentenceWords || words.Count > MaximumSentenceWords)
                {
                    position++;
                    continue;
                }

                var present = queryTerms.Where(t => words.Any(w => Matches(w, t))).ToList();

                if (present.Count > 0)
                {
                    var covered = new HashSet<string>(present.Concat(titleMatches), StringComparer.OrdinalIgnoreCase);
                    var total = Score(sentence, words, queryTerms, present.Count, titleMatches.Count, position);

                    candidates.Add(new Candidate(
                        i + 1,
                        sentence.Trim(),
                        total,
                        (double)covered.Count / queryTerms.Count,
                        titleMatches.Count > 0));
                }

                position++;
            }
        }

        if (candidates.Count == 0)
        {
            return Inconclusive(sources, settings);
        }

        candidates = candidates.OrderByDescending(c => c.Total).ToList();
        Candidate best = candidates[0];

        if (!IsConfident(best))
        {
            return Inconclusive(sources, settings);
        }

        var builder = new StringBuilder();
        var used = new List<int>();
        var perSource = new Dictionary<int, int>();
        var wordCount = 0;
        var taken = 0;

        foreach (Candidate candidate in candidates)
        {
            var length = Tokenise(candidate.Sentence).Count;

            if (taken > 0)
            {
                // Everything past the first sentence has to earn its place. Without these gates the
                // box pads a good answer with the best-scoring sentence from an unrelated page -
                // asked how to cancel a booking, it answered correctly and then volunteered how to
                // change your dates, which reads as though both are the answer.
                var sameSource = candidate.SourceNumber == best.SourceNumber;

                if (!IsConfident(candidate)) continue;
                if (candidate.Total < best.Total * 0.75) continue;

                // A page whose title has nothing to do with the question is not somewhere to quote
                // a second sentence from, however well that one sentence happens to score.
                if (!sameSource && !candidate.SourceTitleMatches) continue;

                // Two sentences from one page is context; three is an excerpt.
                if (perSource.GetValueOrDefault(candidate.SourceNumber) >= 2) continue;

                if (wordCount + length > maxWords) break;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(Punctuate(candidate.Sentence));

            if (settings.ShowSources)
            {
                builder.Append(" [").Append(candidate.SourceNumber).Append(']');
            }

            used.Add(candidate.SourceNumber);
            perSource[candidate.SourceNumber] = perSource.GetValueOrDefault(candidate.SourceNumber) + 1;
            wordCount += length;
            taken++;

            if (taken >= 3)
            {
                break;
            }
        }

        var answer = new AiAnswer
        {
            // Encoded here, matching the model-backed path: the text is page content, and every
            // consumer of AiAnswer.Text is entitled to assume it is already safe to render.
            Text = WebUtility.HtmlEncode(builder.ToString()),
            Model = "built-in",
        };

        if (settings.ShowSources)
        {
            AddSources(answer, sources, used);
        }

        return answer;
    }

    /// <summary>
    /// The honest outcome when nothing in the results answers the question: say so, and point at
    /// what the search did find instead. Quoting a weakly-matching sentence here would be the one
    /// way this engine could actively mislead.
    /// </summary>
    private static AiAnswer Inconclusive(IReadOnlyList<SearchResultItem> sources, AiAnswerSettings settings)
    {
        List<string> titles = sources
            .Select(s => s.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Take(3)
            .ToList();

        var text = titles.Count == 0
            ? "These results do not directly answer that question."
            : "These results do not directly answer that question. They cover " + Join(titles) + ".";

        var answer = new AiAnswer
        {
            Text = WebUtility.HtmlEncode(text),
            IsInconclusive = true,
            Model = "built-in",
        };

        if (settings.ShowSources)
        {
            AddSources(answer, sources, Enumerable.Range(1, Math.Min(3, sources.Count)).ToList());
        }

        return answer;
    }

    private static void AddSources(AiAnswer answer, IReadOnlyList<SearchResultItem> sources, IEnumerable<int> numbers)
    {
        foreach (var number in numbers.Distinct().OrderBy(n => n))
        {
            if (number < 1 || number > sources.Count)
            {
                continue;
            }

            answer.Sources.Add(new AiAnswerSource
            {
                Number = number,
                Name = sources[number - 1].Name,
                Url = sources[number - 1].Url,
            });
        }
    }

    /// <summary>
    /// How well one sentence answers the question.
    /// <para>
    /// What the sentence itself matches dominates, because it is the only signal that distinguishes
    /// an answer from a line that merely shares a word. The rest are tie-breakers: an exact phrase
    /// beats scattered words, a sentence near the top of a page beats one buried in it, and a
    /// sentence from a page whose <em>title</em> matches beats one from a page that mentions the
    /// subject in passing. The title is weighted heavily on purpose - it is the strongest evidence
    /// a page is about the question rather than merely containing the word.
    /// </para>
    /// </summary>
    private static double Score(
        string sentence,
        IReadOnlyList<string> sentenceWords,
        IReadOnlyList<string> queryTerms,
        int sentenceMatches,
        int titleMatches,
        int position)
    {
        double total = ((double)sentenceMatches / queryTerms.Count) * 10;

        if (sentence.Contains(string.Join(' ', queryTerms), StringComparison.OrdinalIgnoreCase))
        {
            total += 4;
        }

        total += 2.0 / (1 + position);
        total += titleMatches * 3.0;

        // Mild preference for a sentence long enough to carry a full thought without being a
        // paragraph masquerading as one.
        total -= Math.Abs(sentenceWords.Count - 22) * 0.04;

        return total;
    }

    /// <summary>
    /// Word match, tolerating the endings English adds. "cancel" matches "cancelled" and
    /// "cancellation" without needing a stemmer or a dictionary in the package.
    /// </summary>
    private static bool Matches(string word, string term)
    {
        if (word.Equals(term, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Only prefix-match once there is enough word to be sure: "car" matching "care", "carry"
        // and "cart" produces confident nonsense.
        if (term.Length < 4)
        {
            return false;
        }

        return word.StartsWith(term, StringComparison.OrdinalIgnoreCase)
               || term.StartsWith(word, StringComparison.OrdinalIgnoreCase) && word.Length >= 4;
    }

    /// <summary>
    /// The longest readable text a result carries. Prefers real field values over the engine's
    /// highlight snippet: the snippet is already cut to a sentence or two, which is too little to
    /// choose between.
    /// </summary>
    private static string ReadableText(SearchResultItem item)
    {
        var best = item.Fields
            .Where(f => IsReadableField(f.Key))
            .Select(f => f.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .OrderByDescending(v => v.Length)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(best) && !string.IsNullOrWhiteSpace(item.Highlight))
        {
            best = WebUtility.HtmlDecode(TagPattern().Replace(item.Highlight!, " "));
        }

        if (string.IsNullOrWhiteSpace(best))
        {
            return string.Empty;
        }

        // Block lists reach the index as raw JSON. It has to be walked rather than stripped with a
        // regex: taking the syntax out of a block leaves its plumbing behind as prose, and the
        // answer box ends up quoting content type keys and umb:// udis at the visitor.
        best = Flatten(best);

        // A block-level tag is a sentence boundary even without punctuation. Without this, a
        // heading runs straight into the paragraph under it and the answer box quotes
        // "Refund policy A refund is issued..." as though the title were part of the sentence.
        best = BlockEndPattern().Replace(best, ". ");
        best = TagPattern().Replace(best, " ");

        // Entities have to come out before the whitespace collapse, not after. Indexed rich text is
        // full of &nbsp;, and leaving it encoded meant the answer was HTML-encoded a second time on
        // the way out - so the visitor read a literal "&nbsp;" in the middle of the sentence.
        // Decoded, it becomes a non-breaking space and the collapse below folds it away.
        best = WebUtility.HtmlDecode(best);

        best = WhitespacePattern().Replace(best, " ");

        // Most paragraphs already end in a full stop, so closing them adds a second one. Collapsing
        // the run afterwards is simpler than trying to work out in advance whether one is needed.
        return DuplicateStopPattern().Replace(best, "$1").Trim();
    }

    /// <summary>
    /// Whether a field holds something a visitor would read.
    /// <para>
    /// Umbraco's double-underscore fields are bookkeeping - paths, keys, icons - with exactly one
    /// exception: <c>__Raw_</c> holds the untouched value of a rich text property, and on many sites
    /// it is the <em>only</em> place the page's prose is retrievable. Excluding it with the rest of
    /// the plumbing left the answer engine with nothing to read on precisely the content-heavy pages
    /// it exists to summarise.
    /// </para>
    /// </summary>
    private static bool IsReadableField(string name)
        => !name.StartsWith("__", StringComparison.Ordinal)
           || name.StartsWith(ImobisoftSearchConstants.IndexFields.Raw, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the readable strings inside a JSON value, skipping the keys that hold identifiers
    /// rather than words. Anything that is not JSON passes straight through.
    /// </summary>
    private static string Flatten(string value)
    {
        var trimmed = value.TrimStart();

        if (trimmed.Length == 0 || (trimmed[0] != '{' && trimmed[0] != '['))
        {
            return value;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(value);
            var builder = new StringBuilder();
            Walk(document.RootElement, builder);

            var flattened = builder.ToString().Trim();

            return flattened.Length > 0 ? flattened : value;
        }
        catch (JsonException)
        {
            // Not JSON after all - a property that merely starts with a brace. Better to quote it
            // verbatim than to lose the page's text to a parse failure.
            return value;
        }
    }

    private static readonly HashSet<string> JsonNoiseKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "contentTypeKey", "key", "udi", "settingsKey", "settingsUdi", "icon", "culture", "alias",
        "id", "elementType", "$type",
    };

    private static void Walk(JsonElement element, StringBuilder builder)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var text = element.GetString();

                if (!string.IsNullOrWhiteSpace(text) && !IdentifierPattern().IsMatch(text))
                {
                    // Each block's text is its own sentence. Punctuate closes it only when it is
                    // not already closed, so a value ending in "." does not come out as "day..".
                    builder.Append(Punctuate(text.Trim())).Append(' ');
                }

                break;

            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (!JsonNoiseKeys.Contains(property.Name))
                    {
                        Walk(property.Value, builder);
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                {
                    Walk(item, builder);
                }

                break;
        }
    }

    /// <summary>
    /// Sentence boundaries on <c>.</c>, <c>!</c> and <c>?</c>, ignoring a full stop inside a decimal
    /// or an abbreviation - splitting on those produces fragments rather than sentences.
    /// </summary>
    private static IEnumerable<string> SplitSentences(string text)
    {
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('.' or '!' or '?'))
            {
                continue;
            }

            var next = i + 1 < text.Length ? text[i + 1] : ' ';

            if (!char.IsWhiteSpace(next))
            {
                continue;
            }

            var previous = i > 0 ? text[i - 1] : ' ';

            if (char.IsDigit(previous) && i + 2 < text.Length && char.IsDigit(text[i + 2]))
            {
                continue;
            }

            var sentence = text[start..(i + 1)].Trim();

            if (sentence.Length > 0)
            {
                yield return sentence;
            }

            start = i + 1;
        }

        if (start < text.Length)
        {
            var tail = text[start..].Trim();

            if (tail.Length > 0)
            {
                yield return tail;
            }
        }
    }

    private static List<string> Tokenise(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? new List<string>()
            : WordPattern().Matches(text).Select(m => m.Value).ToList();

    /// <summary>
    /// Whether a candidate is a real answer rather than a coincidence.
    /// <para>
    /// Half the question is only enough when the page is demonstrably about the subject - which its
    /// title is the evidence for. Asked "parking cost", a sentence about parking prices on a page
    /// titled "Parking at the hotel" covers half the words and is plainly the answer. Asked "what
    /// time can I check in", a sentence about rate changes on a page titled "Changing your dates"
    /// covers exactly the same half - the generic word - and is plainly not. Without the title
    /// condition the box answers the second as confidently as the first.
    /// </para>
    /// </summary>
    private static bool IsConfident(Candidate candidate)
        => candidate.Coverage >= ConfidentCoverage
           && (candidate.SourceTitleMatches || candidate.Coverage >= SelfEvidentCoverage);

    /// <summary>
    /// Strips the layout debris that leads a sentence extracted from grid or block content.
    /// <para>
    /// Block and grid properties carry their column and row spans next to the text, so a sentence
    /// arrives as "1 1 0 0 &#160; Using the platform allows us to…" and the answer box quotes the
    /// spans as though they were part of it.
    /// </para>
    /// <para>
    /// Deliberately narrow: it takes a leading run of <em>three or more</em> short standalone
    /// numbers, which is a layout tuple. One or two leading numbers are left alone, because
    /// "48 hours before your arrival date" is a sentence that starts with a number and cutting it
    /// would change what the page said.
    /// </para>
    /// </summary>
    private static string Clean(string sentence)
        => LayoutTuplePattern().Replace(sentence.Trim(), string.Empty).Trim();

    /// <summary>Makes sure a stitched sentence still ends like one.</summary>
    private static string Punctuate(string sentence)
        => sentence.Length > 0 && sentence[^1] is '.' or '!' or '?' ? sentence : sentence + ".";

    private static string Join(IReadOnlyList<string> values) => values.Count switch
    {
        1 => values[0],
        2 => values[0] + " and " + values[1],
        _ => string.Join(", ", values.Take(values.Count - 1)) + " and " + values[^1],
    };

    /// <param name="Coverage">
    /// Share of the visitor's words answered by this sentence <em>and</em> its page title together.
    /// A page called "Cancellations and refunds" has already established what it is about, so a
    /// sentence on it saying only "Refunds are returned within five working days" is a direct answer
    /// to "cancellation refund time" rather than a one-word-in-three coincidence.
    /// </param>
    private sealed record Candidate(
        int SourceNumber,
        string Sentence,
        double Total,
        double Coverage,
        bool SourceTitleMatches);

    [GeneratedRegex(@"[\p{L}\p{N}']+")]
    private static partial Regex WordPattern();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagPattern();

    /// <summary>Tags that end a block of text, and therefore a sentence.</summary>
    [GeneratedRegex(@"</(?:h[1-6]|p|li|div|section|article|td|th|tr|blockquote)\s*>|<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockEndPattern();

    /// <summary>
    /// A value that is a machine identifier rather than something a visitor would read - a udi, a
    /// GUID, or a bare hex token. Skipped even when it sits under a key this does not recognise.
    /// </summary>
    [GeneratedRegex(@"^\s*(?:umb://[^\s]+|[0-9a-fA-F]{8}-?[0-9a-fA-F-]{4,}|[0-9a-fA-F]{16,})\s*$")]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    /// <summary>A terminator followed by further stops, e.g. the "day.." a closed paragraph leaves.</summary>
    [GeneratedRegex(@"([.!?])(?:\s*\.)+")]
    private static partial Regex DuplicateStopPattern();

    /// <summary>
    /// A leading run of three or more short standalone numbers - the column/row span tuple that
    /// leads block and grid content, e.g. "1 1 0 0 ". Three is the threshold so that a sentence
    /// opening with one or two real figures is left intact.
    /// </summary>
    [GeneratedRegex(@"^(?:\d{1,3}[\s\p{P}]+){3,}")]
    private static partial Regex LayoutTuplePattern();
}
