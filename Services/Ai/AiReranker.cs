using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Imobisoft.Search.Models;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Cache;
using Umbraco.Extensions;

namespace Imobisoft.Search.Services.Ai;

/// <summary>
/// Re-orders the best matches by how well they answer the query.
/// <para>
/// Keyword scoring measures how often a query's words appear, which is a good proxy for relevance
/// and not the same thing as it. The page that plainly answers "do you deliver to Scotland" may say
/// "Scotland" once, while a page listing every county says it four times and wins. This reads the
/// titles and extracts of the top matches and puts them in the order a person would.
/// </para>
/// <para>
/// It cannot invent, alter or remove a result. The model is given ids and returns ids; anything it
/// returns that was not sent is discarded, anything it omits keeps the order it already had, and
/// the whole stage is skipped if the reply cannot be read. The worst case is the order the engine
/// produced on its own.
/// </para>
/// </summary>
public interface IAiReranker
{
    /// <summary>
    /// Orders <paramref name="candidates"/> best first, or returns null when there is nothing to do
    /// - the stage is off, no key has been added, the budget is spent, the provider was slow, or
    /// the reply could not be read.
    /// </summary>
    Task<IReadOnlyList<SearchResultItem>?> RerankAsync(
        string term,
        IReadOnlyList<SearchResultItem> candidates,
        AiRules rules,
        CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class AiReranker : IAiReranker
{
    private const string Stage = "rerank";

    private const string CacheKeyPrefix = ImobisoftSearchConstants.Ai.CacheKeyPrefix + ".Rerank";

    /// <summary>Stands in for "asked, could not be ordered", which cannot be cached as a null.</summary>
    private const string Unorderable = "-";

    /// <summary>
    /// How much of each candidate the model is shown. Enough to tell what the page is about, and
    /// small enough that twenty of them is still a cheap request - this is multiplied by the depth,
    /// so it is the real cost lever of the stage.
    /// </summary>
    private const int ExtractCharacters = 320;

    /// <summary>
    /// Fewest candidates worth the call. Ordering two results is not worth a round trip, and the
    /// first page of a search that found three things is not where ranking goes wrong.
    /// </summary>
    private const int MinimumCandidates = 3;

    private readonly IAiGateway _gateway;
    private readonly AppCaches _caches;
    private readonly ILogger<AiReranker> _logger;

    public AiReranker(IAiGateway gateway, AppCaches caches, ILogger<AiReranker> logger)
    {
        _gateway = gateway;
        _caches = caches;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchResultItem>?> RerankAsync(
        string term,
        IReadOnlyList<SearchResultItem> candidates,
        AiRules rules,
        CancellationToken cancellationToken)
    {
        if (!rules.Search.Rerank || !_gateway.IsConfigured(rules))
        {
            return null;
        }

        var query = (term ?? string.Empty).Trim();

        if (query.Length == 0 || candidates.Count < MinimumCandidates)
        {
            return null;
        }

        // Pinned results are the editor's instruction about what comes first, so they are not part
        // of what gets reordered - and not part of what the model is shown, which also keeps them
        // out of the cost.
        List<SearchResultItem> pinned = candidates.Where(c => c.IsBestBet).ToList();
        List<SearchResultItem> orderable = candidates.Where(c => !c.IsBestBet).ToList();

        if (orderable.Count < MinimumCandidates)
        {
            return null;
        }

        var cacheKey = CacheKey(rules, query, orderable);

        if (rules.CacheMinutes > 0
            && _caches.RuntimeCache.GetCacheItem<string>(cacheKey) is { } cached)
        {
            return cached == Unorderable ? null : Apply(cached, pinned, orderable);
        }

        if (!AiCallBudget.TryTake(Stage, rules.Search.MaxCallsPerMinute))
        {
            return null;
        }

        AiCompletionResult completion = await _gateway.RunAsync(
            new AiGatewayRequest
            {
                Rules = rules,
                SystemPrompt = AiPrompts.Rerank,
                UserText = Candidates(orderable) + "<query>\n" + AiPrompts.Fence(query) + "\n</query>",

                // A list of numbers. Room for the longest plausible order and nothing more, which
                // also stops a model that decides to justify its ranking.
                MaxOutputTokens = 48 + (orderable.Count * 8),
                Timeout = TimeSpan.FromMilliseconds(
                    Math.Clamp(rules.Search.TimeoutMilliseconds, 250, 15000)),
            },
            cancellationToken);

        if (!completion.Ok)
        {
            // Not cached: the provider being slow says nothing about this result set.
            _logger.LogDebug("Imobisoft.Search could not re-rank the results: {Error}", completion.Error);

            return null;
        }

        IReadOnlyList<SearchResultItem>? ordered = Apply(completion.Text, pinned, orderable);

        if (rules.CacheMinutes > 0)
        {
            _caches.RuntimeCache.InsertCacheItem(
                cacheKey,
                () => ordered is null ? Unorderable : completion.Text,
                TimeSpan.FromMinutes(rules.CacheMinutes));
        }

        return ordered;
    }

    /// <summary>
    /// The candidates in the order the model gave, with the pinned results first and anything it did
    /// not mention following in the order it already had. Null when the reply carried no usable
    /// order, which leaves the engine's own ranking in place.
    /// </summary>
    private IReadOnlyList<SearchResultItem>? Apply(
        string reply,
        List<SearchResultItem> pinned,
        List<SearchResultItem> orderable)
    {
        List<int>? order = Order(reply, orderable.Count);

        if (order is null || order.Count == 0)
        {
            return null;
        }

        var ordered = new List<SearchResultItem>(pinned.Count + orderable.Count);
        ordered.AddRange(pinned);

        var taken = new HashSet<int>();

        foreach (var index in order)
        {
            if (taken.Add(index))
            {
                ordered.Add(orderable[index]);
            }
        }

        // Everything the model left out, in the order the engine had it. A result is never dropped
        // here: this stage orders a page, it does not decide what is on it, and a visitor who can
        // count the results on page one would notice.
        for (var i = 0; i < orderable.Count; i++)
        {
            if (!taken.Contains(i))
            {
                ordered.Add(orderable[i]);
            }
        }

        return ordered;
    }

    /// <summary>
    /// The ids from the reply as zero-based indexes into the candidates, keeping only the ones that
    /// were actually sent. Null when the reply is not an order at all.
    /// </summary>
    private List<int>? Order(string reply, int count)
    {
        using JsonDocument? document = AiJson.Object(reply);

        if (document is null)
        {
            return null;
        }

        if (!document.RootElement.TryGetProperty("order", out JsonElement array)
            || array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var order = new List<int>(count);

        foreach (JsonElement item in array.EnumerateArray())
        {
            var id = item.ValueKind switch
            {
                JsonValueKind.Number when item.TryGetInt32(out var number) => number,
                JsonValueKind.String when int.TryParse(item.GetString(), out var parsed) => parsed,
                _ => 0,
            };

            // Ids are handed out from 1 so that a model answering with a 0 it invented is caught
            // here rather than quietly promoting the first result.
            if (id >= 1 && id <= count)
            {
                order.Add(id - 1);
            }
        }

        if (order.Count == 0)
        {
            _logger.LogDebug("Imobisoft.Search got a re-ranking reply with no usable ids in it.");

            return null;
        }

        return order;
    }

    /// <summary>
    /// The candidates as numbered blocks. Title first, then whatever the result already carries to
    /// describe itself - the sentence that matched, or what the page says about itself. Nothing is
    /// read from the index here: this stage works from what the search already returned, which is
    /// what keeps it to one provider call and no extra index work.
    /// </summary>
    private static string Candidates(List<SearchResultItem> candidates)
    {
        var builder = new StringBuilder();

        for (var i = 0; i < candidates.Count; i++)
        {
            SearchResultItem item = candidates[i];

            var extract = Extract(item);

            builder.Append("<result id=\"").Append(i + 1).Append("\" title=\"")
                .Append(AiPrompts.Fence(item.Name)).Append("\">\n")
                .Append(AiPrompts.Fence(extract))
                .Append("\n</result>\n\n");
        }

        return builder.ToString();
    }

    private static string Extract(SearchResultItem item)
    {
        var text = item.Highlight;

        if (string.IsNullOrWhiteSpace(text))
        {
            text = item.Summary;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            // Nothing to describe it but where it lives, which is still a signal: a page under
            // /products says more about itself than an empty block would.
            text = item.Path ?? item.Url ?? string.Empty;
        }

        // The highlight carries the markup that marks the matched words. It is stripped so the
        // model is billed for words rather than tags, and so emphasis is not read as importance.
        var clean = Strip(text);

        return clean.Length <= ExtractCharacters ? clean : clean[..ExtractCharacters];
    }

    private static string Strip(string value)
    {
        var builder = new StringBuilder(value.Length);
        var insideTag = false;
        var lastWasSpace = false;

        foreach (var c in value)
        {
            if (c == '<')
            {
                insideTag = true;
                continue;
            }

            if (c == '>')
            {
                insideTag = false;
                continue;
            }

            if (insideTag)
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }

                continue;
            }

            builder.Append(c);
            lastWasSpace = false;
        }

        return System.Net.WebUtility.HtmlDecode(builder.ToString()).Trim();
    }

    /// <summary>
    /// Keyed on the query and on exactly which results are being ordered, in the order they came in.
    /// <para>
    /// The result ids are part of the key because an order is only meaningful for the set it was
    /// asked about: the same query on a site whose content has changed is a different question, and
    /// reusing the old answer would reorder results that are no longer there.
    /// </para>
    /// </summary>
    private string CacheKey(AiRules rules, string query, List<SearchResultItem> candidates)
    {
        var raw = string.Join(
            "|",
            _gateway.Fingerprint(rules),
            query.ToLowerInvariant(),
            string.Join(',', candidates.Select(c => c.Id)));

        return CacheKeyPrefix + "|" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..24];
    }
}
