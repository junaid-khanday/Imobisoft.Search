using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Imobisoft.Search.Models;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Cache;
using Umbraco.Extensions;

namespace Imobisoft.Search.Services.Ai;

/// <summary>
/// What the model made of a query, after everything it proposed has been checked against the
/// profile.
/// </summary>
public sealed class AiQueryPlan
{
    /// <summary>
    /// The term to run, corrected and stripped of the words that were describing a filter rather
    /// than naming a thing. Empty leaves the visitor's own term alone.
    /// </summary>
    public string Term { get; init; } = string.Empty;

    /// <summary>
    /// Words the site uses for the same thing, searched alongside the term rather than instead of
    /// it. Applied as synonyms, so they widen what matches without outranking the real term.
    /// </summary>
    public IReadOnlyList<string> Expansions { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Filter selections lifted out of the query, keyed by facet alias. Only ever facets the
    /// profile defines and has enabled.
    /// </summary>
    public IReadOnlyDictionary<string, IList<string>> Filters { get; init; } =
        new Dictionary<string, IList<string>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Alias of a sort option the query asked for, or null. Only ever an enabled one.</summary>
    public string? Sort { get; init; }

    /// <summary>True when nothing survived validation, so there is nothing to apply.</summary>
    public bool IsEmpty
        => Term.Length == 0 && Expansions.Count == 0 && Filters.Count == 0 && Sort is null;

    /// <summary>
    /// The filters this plan selected, written the way the profile labels them rather than by alias
    /// - "Location: Leeds" rather than "location=Leeds". This goes on the page for a visitor to
    /// read, so it uses the facet's own label, and a range bucket's own label where there is one.
    /// </summary>
    public List<string> DescribeFilters(SearchRuleSet rules)
    {
        var described = new List<string>();

        foreach (KeyValuePair<string, IList<string>> filter in Filters)
        {
            FacetDefinition? facet = rules.Results.Facets.FirstOrDefault(
                f => f.Alias.Equals(filter.Key, StringComparison.OrdinalIgnoreCase));

            var label = string.IsNullOrWhiteSpace(facet?.Label) ? filter.Key : facet!.Label;

            // A range facet's values are bucket aliases, which say nothing to a visitor -
            // "price-2" is not a price. The bucket's own label is what was written to be read.
            IEnumerable<string> values = filter.Value.Select(value =>
                facet?.Ranges.FirstOrDefault(
                        r => r.Alias.Equals(value, StringComparison.OrdinalIgnoreCase))
                    ?.Label is { Length: > 0 } bucket
                    ? bucket
                    : value);

            described.Add(label + ": " + string.Join(", ", values));
        }

        return described;
    }

    /// <summary>Short description of what was changed, for the dashboard's diagnostics.</summary>
    public string Describe()
    {
        var parts = new List<string>();

        if (Term.Length > 0)
        {
            parts.Add($"term rewritten to \"{Term}\"");
        }

        if (Expansions.Count > 0)
        {
            parts.Add("expanded with " + string.Join(", ", Expansions));
        }

        foreach (KeyValuePair<string, IList<string>> filter in Filters)
        {
            parts.Add($"filter {filter.Key}={string.Join('|', filter.Value)}");
        }

        if (Sort is not null)
        {
            parts.Add("sorted by " + Sort);
        }

        return parts.Count == 0 ? "nothing to apply" : string.Join("; ", parts);
    }
}

/// <summary>
/// Reads a query before it runs.
/// <para>
/// A keyword engine does what it is told: "3 bed houses in Leeds added this month, cheapest first"
/// is nine words to match, and the pages that match most of them are rarely the three-bedroom
/// houses in Leeds. This turns that sentence into the parts the engine already understands - a
/// term, the site's own words for it, two filter selections and a sort order - so the profile's
/// existing rules do the work.
/// </para>
/// <para>
/// Nothing the model proposes is trusted. A filter is applied only if the profile defines it and
/// has it enabled, a range bucket only if it is one of the buckets configured on that facet, and a
/// sort only if it is an enabled option. Anything else is dropped. The widest thing it can do is
/// add words to search for.
/// </para>
/// </summary>
public interface IAiQueryPlanner
{
    /// <summary>
    /// The plan for this query, or null when there is nothing to do - the stage is off, no key has
    /// been added, the budget is spent, the provider was slow, or the model found nothing to change.
    /// </summary>
    Task<AiQueryPlan?> PlanAsync(
        string term,
        SearchRuleSet rules,
        CancellationToken cancellationToken);

    /// <summary>
    /// The plan already worked out for this query, or null when there is not one to hand.
    /// <para>
    /// Reads the cache and nothing else - it never calls a provider and so never spends anything.
    /// That is the whole point of it: the page needs to say what was done to a visitor's query, and
    /// the search that rendered the page has already put the answer here. An endpoint that could
    /// trigger the work instead would be a way to run up a bill by asking politely.
    /// </para>
    /// </summary>
    AiQueryPlan? Cached(string term, SearchRuleSet rules);
}

/// <inheritdoc />
public sealed class AiQueryPlanner : IAiQueryPlanner
{
    private const string Stage = "query";

    private const string CacheKeyPrefix = ImobisoftSearchConstants.Ai.CacheKeyPrefix + ".Query";

    /// <summary>Stands in for "asked, nothing to change", which cannot be cached as a null.</summary>
    private const string NothingToApply = "-";

    /// <summary>
    /// Longest query this is worth running on. A long query is usually pasted text rather than a
    /// question, and reading it costs more than the search it would improve.
    /// </summary>
    private const int MaxTermLength = 300;

    /// <summary>
    /// Most expansion words accepted. Each one widens the query, and past a handful they stop
    /// adding recall and start adding noise.
    /// </summary>
    private const int MaxExpansions = 6;

    /// <summary>Most filters accepted from one query.</summary>
    private const int MaxFilters = 3;

    /// <summary>Most values accepted within one filter.</summary>
    private const int MaxFilterValues = 3;

    private readonly IAiGateway _gateway;
    private readonly AppCaches _caches;
    private readonly ILogger<AiQueryPlanner> _logger;

    public AiQueryPlanner(IAiGateway gateway, AppCaches caches, ILogger<AiQueryPlanner> logger)
    {
        _gateway = gateway;
        _caches = caches;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AiQueryPlan?> PlanAsync(
        string term,
        SearchRuleSet rules,
        CancellationToken cancellationToken)
    {
        // Never null in practice, but an ad-hoc rule set is posted as JSON and can carry an
        // explicit null for the section. Defaults mean every stage is off, which is what a rule set
        // that did not mention them is asking for.
        AiRules ai = rules.Ai ?? new AiRules();

        if (!ai.Search.UnderstandQuery || !_gateway.IsConfigured(ai))
        {
            return null;
        }

        var query = (term ?? string.Empty).Trim();

        // A single word has no structure to read: there is no filter hiding in "roofing", and
        // correcting its spelling is what the "did you mean" suggestion already does for free.
        if (query.Length < 3 || query.Length > MaxTermLength || !query.Contains(' '))
        {
            return null;
        }

        var cacheKey = CacheKey(ai, rules, query);

        if (ai.CacheMinutes > 0
            && _caches.RuntimeCache.GetCacheItem<string>(cacheKey) is { } cached)
        {
            return cached == NothingToApply ? null : Read(cached, rules, ai.Search);
        }

        // After the cache, so a search that can be served from one is never refused for budget.
        if (!AiCallBudget.TryTake(Stage, ai.Search.MaxCallsPerMinute))
        {
            return null;
        }

        var catalogue = Catalogue(rules, ai);

        AiCompletionResult completion = await _gateway.RunAsync(
            new AiGatewayRequest
            {
                Rules = ai,
                SystemPrompt = AiPrompts.QueryPlan(ai.Search),
                UserText = catalogue + "\n<query>\n" + AiPrompts.Fence(query) + "\n</query>",

                // A plan is a small object. A tight ceiling here is also the cheapest guard against
                // a model that starts explaining itself instead of answering.
                MaxOutputTokens = 400,
                Timeout = Timeout(ai),
            },
            cancellationToken);

        if (!completion.Ok)
        {
            // Not cached: a provider being slow or down says nothing about this query, and the next
            // visitor to search for it should get the benefit if the provider has recovered.
            _logger.LogDebug("Imobisoft.Search could not read the query: {Error}", completion.Error);

            return null;
        }

        AiQueryPlan? plan = Read(completion.Text, rules, ai.Search);

        if (ai.CacheMinutes > 0)
        {
            // The refusal is cached too. A query the model finds nothing to change in is the common
            // case on an ordinary search, and paying to be told so twice is the one thing worth
            // avoiding here.
            _caches.RuntimeCache.InsertCacheItem(
                cacheKey,
                () => plan is null ? NothingToApply : completion.Text,
                TimeSpan.FromMinutes(ai.CacheMinutes));
        }

        if (plan is not null)
        {
            _logger.LogDebug("Imobisoft.Search read \"{Query}\" as: {Plan}", query, plan.Describe());
        }

        return plan;
    }

    /// <inheritdoc />
    public AiQueryPlan? Cached(string term, SearchRuleSet rules)
    {
        AiRules ai = rules.Ai ?? new AiRules();

        if (!ai.Search.UnderstandQuery || ai.CacheMinutes <= 0)
        {
            return null;
        }

        var query = (term ?? string.Empty).Trim();

        if (query.Length == 0)
        {
            return null;
        }

        var cached = _caches.RuntimeCache.GetCacheItem<string>(CacheKey(ai, rules, query));

        return cached is null || cached == NothingToApply
            ? null
            : Read(cached, rules, ai.Search);
    }

    /// <summary>
    /// Turns the model's reply into a plan, keeping only what the profile actually offers. Null when
    /// nothing survives.
    /// </summary>
    private AiQueryPlan? Read(string reply, SearchRuleSet rules, AiSearchRules search)
    {
        using JsonDocument? document = AiJson.Object(reply);

        if (document is null)
        {
            return null;
        }

        JsonElement root = document.RootElement;

        var term = search.CorrectSpelling
            ? AiJson.String(root, "term", MaxTermLength) ?? string.Empty
            : string.Empty;

        List<string> expansions = search.ExpandSynonyms
            ? AiJson.Strings(root, "expansions", MaxExpansions, 60)
            : new List<string>();

        Dictionary<string, IList<string>> filters = search.ExtractFilters
            ? Filters(root, rules)
            : new Dictionary<string, IList<string>>(StringComparer.OrdinalIgnoreCase);

        var sort = search.ExtractFilters ? Sort(root, rules) : null;

        var plan = new AiQueryPlan
        {
            Term = term,
            Expansions = expansions,
            Filters = filters,
            Sort = sort,
        };

        return plan.IsEmpty ? null : plan;
    }

    /// <summary>
    /// Filter selections the profile recognises. A facet it does not define, has disabled, or - for
    /// a range facet - a bucket it does not configure, is dropped without comment: the model is
    /// proposing, and this is the only thing deciding.
    /// </summary>
    private static Dictionary<string, IList<string>> Filters(JsonElement root, SearchRuleSet rules)
    {
        var selected = new Dictionary<string, IList<string>>(StringComparer.OrdinalIgnoreCase);

        if (!root.TryGetProperty("filters", out JsonElement proposed)
            || proposed.ValueKind != JsonValueKind.Object)
        {
            return selected;
        }

        List<FacetDefinition> available = rules.Results.Facets
            .Where(f => f.Enabled && !string.IsNullOrWhiteSpace(f.Alias))
            .ToList();

        foreach (JsonProperty property in proposed.EnumerateObject())
        {
            if (selected.Count >= MaxFilters)
            {
                break;
            }

            FacetDefinition? facet = available.FirstOrDefault(
                f => f.Alias.Equals(property.Name, StringComparison.OrdinalIgnoreCase));

            if (facet is null)
            {
                continue;
            }

            List<string> values = Values(property.Value);

            if (facet.Kind != FacetKind.Field)
            {
                // A range facet's buckets are configured, named and finite, so there is no reason to
                // accept anything else - "cheapest" is either one of this facet's bands or it is not.
                values = values
                    .Select(v => facet.Ranges.FirstOrDefault(
                        r => !string.IsNullOrWhiteSpace(r.Alias)
                             && r.Alias.Equals(v, StringComparison.OrdinalIgnoreCase))?.Alias)
                    .Where(v => v is not null)
                    .Select(v => v!)
                    .ToList();
            }

            if (values.Count == 0)
            {
                continue;
            }

            selected[facet.Alias] = values;
        }

        return selected;
    }

    /// <summary>
    /// The values proposed for one facet, however the model wrote them - a single string or an
    /// array of them.
    /// </summary>
    private static List<string> Values(JsonElement value)
    {
        var values = new List<string>();

        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                var single = (value.GetString() ?? string.Empty).Trim();

                if (single.Length is > 0 and <= 80)
                {
                    values.Add(single);
                }

                break;

            case JsonValueKind.Array:
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (JsonElement item in value.EnumerateArray())
                {
                    if (values.Count >= MaxFilterValues || item.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var text = (item.GetString() ?? string.Empty).Trim();

                    if (text.Length is > 0 and <= 80 && seen.Add(text))
                    {
                        values.Add(text);
                    }
                }

                break;
        }

        return values;
    }

    /// <summary>The sort option the query asked for, when it is one the profile has enabled.</summary>
    private static string? Sort(JsonElement root, SearchRuleSet rules)
    {
        var proposed = AiJson.String(root, "sort", 80);

        if (proposed is null)
        {
            return null;
        }

        return rules.Results.SortOptions
            .FirstOrDefault(o => o.Enabled
                                 && !string.IsNullOrWhiteSpace(o.Alias)
                                 && o.Alias.Equals(proposed, StringComparison.OrdinalIgnoreCase))
            ?.Alias;
    }

    /// <summary>
    /// What the profile offers, written out for the model: the filters it may select and the sort
    /// orders it may ask for, by the aliases this package will accept back.
    /// <para>
    /// Only ever the enabled ones, so the model is never told about a dimension a visitor cannot
    /// use. A field facet's own buckets are not listed because they are whatever the index happens
    /// to hold - the model proposes a value there, and a selection that matches nothing is caught
    /// by the retry in the search service rather than guessed at here.
    /// </para>
    /// </summary>
    private static string Catalogue(SearchRuleSet rules, AiRules ai)
    {
        var builder = new StringBuilder();

        if (ai.Search.ExtractFilters)
        {
            foreach (FacetDefinition facet in rules.Results.Facets.Where(
                         f => f.Enabled && !string.IsNullOrWhiteSpace(f.Alias)))
            {
                builder.Append("<filter alias=\"").Append(AiPrompts.Fence(facet.Alias)).Append('"')
                    .Append(" label=\"").Append(AiPrompts.Fence(facet.Label)).Append('"');

                if (facet.Kind == FacetKind.Field)
                {
                    builder.Append(" values=\"any\"");
                }
                else
                {
                    builder.Append(" values=\"")
                        .Append(AiPrompts.Fence(string.Join(
                            ", ",
                            facet.Ranges
                                .Where(r => !string.IsNullOrWhiteSpace(r.Alias))
                                .Select(r => r.Alias + " (" + r.Label + ")"))))
                        .Append('"');
                }

                builder.Append(" />\n");
            }

            foreach (SortOption option in rules.Results.SortOptions.Where(
                         o => o.Enabled && !string.IsNullOrWhiteSpace(o.Alias)))
            {
                builder.Append("<sort alias=\"").Append(AiPrompts.Fence(option.Alias)).Append('"')
                    .Append(" label=\"").Append(AiPrompts.Fence(option.Label)).Append("\" />\n");
            }
        }

        return builder.Length == 0
            ? "<available>none</available>\n"
            : "<available>\n" + builder + "</available>\n";
    }

    private static TimeSpan Timeout(AiRules ai)
        => TimeSpan.FromMilliseconds(Math.Clamp(ai.Search.TimeoutMilliseconds, 250, 15000));

    /// <summary>
    /// Keyed on the query and on everything that would change the answer to it: which provider and
    /// model is answering, which parts of the stage are switched on, and the filters and sort
    /// orders the profile was offering when it was asked.
    /// </summary>
    private string CacheKey(AiRules ai, SearchRuleSet rules, string query)
    {
        AiSearchRules search = ai.Search;

        var offered = string.Join(
            ",",
            rules.Results.Facets.Where(f => f.Enabled).Select(f => f.Alias)
                .Concat(rules.Results.SortOptions.Where(o => o.Enabled).Select(o => o.Alias)));

        var raw = string.Join(
            "|",
            _gateway.Fingerprint(ai),
            query.ToLowerInvariant(),
            search.CorrectSpelling,
            search.ExpandSynonyms,
            search.ExtractFilters,
            offered);

        return CacheKeyPrefix + "|" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..24];
    }
}
