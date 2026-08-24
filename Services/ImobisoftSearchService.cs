using System.Collections.Concurrent;
using System.Diagnostics;
using Examine;
using Examine.Search;
using Imobisoft.Search.Configuration;
using Imobisoft.Search.Models;
using Imobisoft.Search.Services.Querying;
using Imobisoft.Search.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;

namespace Imobisoft.Search.Services;

/// <inheritdoc />
public sealed class ImobisoftSearchService : IImobisoftSearchService
{
    /// <summary>Profile alias reported when a search runs an unsaved rule set, e.g. a dashboard preview.</summary>
    internal const string AdHocProfileAlias = "ad-hoc";

    /// <summary>Fields every result carries, whatever the profile asked to return.</summary>
    private static readonly string[] AlwaysReturnedFields =
    {
        ImobisoftSearchConstants.IndexFields.NodeName,
        ImobisoftSearchConstants.IndexFields.SystemNodeName,
        ImobisoftSearchConstants.IndexFields.NodeTypeAlias,
        ImobisoftSearchConstants.IndexFields.CreateDate,
        ImobisoftSearchConstants.IndexFields.UpdateDate,
    };

    private readonly IExamineManager _examineManager;
    private readonly ISearchProfileService _profileService;
    private readonly IIndexCatalogService _catalog;
    private readonly ISearchSettingsService _settingsService;
    private readonly ISearchAnalyticsService _analytics;
    private readonly IPublicAccessService _publicAccessService;
    private readonly IUmbracoContextFactory _umbracoContextFactory;
    private readonly IPublishedUrlProvider _publishedUrlProvider;
    private readonly IIdKeyMap _idKeyMap;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ImobisoftSearchOptions _options;
    private readonly ILogger<ImobisoftSearchService> _logger;

    /// <summary>Facet subtree keys resolved to numeric ids, cached for the service lifetime.</summary>
    private readonly ConcurrentDictionary<Guid, int> _facetNodeIdCache = new();

    public ImobisoftSearchService(
        IExamineManager examineManager,
        ISearchProfileService profileService,
        IIndexCatalogService catalog,
        ISearchSettingsService settingsService,
        ISearchAnalyticsService analytics,
        IPublicAccessService publicAccessService,
        IUmbracoContextFactory umbracoContextFactory,
        IPublishedUrlProvider publishedUrlProvider,
        IIdKeyMap idKeyMap,
        IHttpContextAccessor httpContextAccessor,
        IOptions<ImobisoftSearchOptions> options,
        ILogger<ImobisoftSearchService> logger)
    {
        _examineManager = examineManager;
        _profileService = profileService;
        _catalog = catalog;
        _settingsService = settingsService;
        _analytics = analytics;
        _publicAccessService = publicAccessService;
        _umbracoContextFactory = umbracoContextFactory;
        _publishedUrlProvider = publishedUrlProvider;
        _idKeyMap = idKeyMap;
        _httpContextAccessor = httpContextAccessor;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<SearchResponse> SearchAsync(
        string term,
        string? profileAlias = null,
        int page = 1,
        int? pageSize = null,
        CancellationToken cancellationToken = default)
        => SearchAsync(
            new SearchRequest
            {
                Term = term,
                ProfileAlias = profileAlias,
                Page = page,
                PageSize = pageSize,
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        // An ad-hoc rule set is how the dashboard previews unsaved changes; otherwise resolve the
        // saved profile the caller asked for.
        SearchProfile? profile = request.Rules is null ? _profileService.ResolveForSearch(request.ProfileAlias) : null;
        SearchRuleSet rules = request.Rules ?? profile!.Rules;
        var profileAlias = request.Rules is not null ? AdHocProfileAlias : profile!.Alias;

        // An unset or zeroed rule must fall back to the rule's own default rather than clamp to a
        // nonsensical one-result-per-page - that is what a bad save would otherwise serve.
        var pageSize = request.PageSize is > 0
            ? request.PageSize.Value
            : rules.Results.PageSize > 0 ? rules.Results.PageSize : new ResultRules().PageSize;

        if (!_options.Enabled)
        {
            _logger.LogDebug("Imobisoft.Search is disabled in configuration; returning no results.");
            return Task.FromResult(SearchResponse.Empty(request.Term, profileAlias, request.Page, pageSize));
        }

        if (profile is { Enabled: false })
        {
            _logger.LogDebug("Search profile '{Alias}' is disabled; returning no results.", profileAlias);
            return Task.FromResult(SearchResponse.Empty(request.Term, profileAlias, request.Page, pageSize));
        }

        ApplyQueryStringFilters(request);

        (SearchRuleSet effectiveRules, string? appliedSort) = ApplyRequestedSort(rules, request);

        SearchResponse response = Execute(effectiveRules, request, profileAlias, pageSize, stopwatch, cancellationToken);

        response.SelectedSort = appliedSort;

        AddSuggestion(response, rules, request, profileAlias, cancellationToken);

        stopwatch.Stop();
        RecordAnalytics(response, request, (int)stopwatch.ElapsedMilliseconds);

        if (response.Diagnostics is not null)
        {
            response.Diagnostics.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
        }

        PublishToRequest(response);

        return Task.FromResult(response);
    }

    /// <summary>
    /// Resolves the visitor's sort choice against the profile's enabled sort options. A match
    /// overrides the ranking order for this request; both the rule set and its ranking are cloned
    /// because the saved profile instance is cached and shared across requests - mutating its
    /// SortBy list in place would leak one visitor's sort into everyone else's results.
    /// </summary>
    /// <returns>The effective rules plus the applied alias, or null when the profile default stands.</returns>
    private (SearchRuleSet Rules, string? Alias) ApplyRequestedSort(SearchRuleSet rules, SearchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Sort) && _options.ReadFiltersFromQueryString)
        {
            request.Sort = _httpContextAccessor.HttpContext?.Request.Query["sort"].ToString();
        }

        var alias = (request.Sort ?? string.Empty).Trim();

        if (alias.Length == 0 || rules.Results.SortOptions.Count == 0)
        {
            return (rules, null);
        }

        SortOption? option = rules.Results.SortOptions.FirstOrDefault(o =>
            o.Enabled &&
            !string.IsNullOrWhiteSpace(o.Alias) &&
            o.Alias.Equals(alias, StringComparison.OrdinalIgnoreCase));

        if (option is null)
        {
            return (rules, null);
        }

        RankingRules ranking = new()
        {
            SortBy = new List<SortRule>
            {
                new()
                {
                    Field = string.IsNullOrWhiteSpace(option.Field) ? SortRule.ScoreField : option.Field,
                    Direction = option.Direction,
                },
            },
            ContentTypeBoosts = rules.Ranking.ContentTypeBoosts,
            BestBets = rules.Ranking.BestBets,
            BlockedTerms = rules.Ranking.BlockedTerms,
            Recency = rules.Ranking.Recency,
        };

        // Best bets still pin their nodes to the top; everything below them follows the visitor's
        // chosen order, which is what a "Sort by" dropdown promises.
        SearchRuleSet effective = new()
        {
            Sources = rules.Sources,
            Matching = rules.Matching,
            Ranking = ranking,
            Results = rules.Results,
        };

        return (effective, option.Alias);
    }

    /// <summary>
    /// Resolves a content or media key for facet subtree matching. Facet buckets store the picked
    /// page as a GUID/UDI while the index path field carries numeric ids; the processor calls this
    /// through a delegate so it never needs Umbraco services directly. Results are cached because
    /// the same handful of roots is resolved on every search request.
    /// </summary>
    private int? ResolveNodeIdForFacets(Guid key)
    {
        if (_facetNodeIdCache.TryGetValue(key, out int cached))
        {
            return cached;
        }

        foreach (UmbracoObjectTypes objectType in new[] { UmbracoObjectTypes.Document, UmbracoObjectTypes.Media })
        {
            Attempt<int> attempt = _idKeyMap.GetIdForKey(key, objectType);

            if (attempt.Success)
            {
                _facetNodeIdCache[key] = attempt.Result;
                return attempt.Result;
            }
        }

        return null;
    }

    /// <summary>
    /// Picks up filter selections from the query string when the caller has not supplied any.
    /// <para>
    /// This is what lets a search page support filtering without writing code: the filter links the
    /// package generates carry <c>f_alias=value</c>, and they are read back here. A caller that sets
    /// <see cref="SearchRequest.Filters"/> itself is left alone.
    /// </para>
    /// </summary>
    private void ApplyQueryStringFilters(SearchRequest request)
    {
        if (!_options.ReadFiltersFromQueryString || request.Filters.Count > 0)
        {
            return;
        }

        HttpContext? httpContext = _httpContextAccessor.HttpContext;

        if (httpContext is null)
        {
            return;
        }

        foreach (KeyValuePair<string, IReadOnlyList<string>> filter in
                 httpContext.GetActiveFilters(_options.FilterQueryPrefix))
        {
            request.Filters[filter.Key] = filter.Value.ToList();
        }
    }

    /// <summary>
    /// Leaves the response on the request so a view can reach the parts that do not fit in a list of
    /// results - filters, the spelling suggestion, and each result's matching sentence.
    /// </summary>
    private void PublishToRequest(SearchResponse response)
    {
        HttpContext? httpContext = _httpContextAccessor.HttpContext;

        if (httpContext is null)
        {
            return;
        }

        try
        {
            httpContext.Items[SearchContextExtensions.ResponseItemKey] = response;

            if (_options.AddResponseHeader && !httpContext.Response.HasStarted)
            {
                httpContext.Response.Headers[SearchContextExtensions.EngineHeader] = response.ProfileAlias;
            }
        }
        catch (Exception ex)
        {
            // Never let bookkeeping break a search that already succeeded.
            _logger.LogDebug(ex, "Could not publish the search response to the current request.");
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SearchSuggestion>> AutocompleteAsync(
        string term,
        string? profileAlias = null,
        int take = 0,
        CancellationToken cancellationToken = default)
    {
        SuggestionSettings settings = _settingsService.Get().Suggestions;

        if (!settings.Enabled || string.IsNullOrWhiteSpace(term))
        {
            return Task.FromResult<IReadOnlyList<SearchSuggestion>>(Array.Empty<SearchSuggestion>());
        }

        var size = take > 0 ? take : Math.Max(1, settings.AutocompleteSize);
        SearchProfile profile = _profileService.ResolveForSearch(profileAlias);

        // Type-ahead runs on every keystroke, so it searches names only, skips every post-processing
        // step a full search does, and pulls a small window.
        SearchRuleSet rules = NameOnlyRules(profile.Rules, FieldMatchMode.Prefix, settings.Fuzziness, size * 3);

        var request = new SearchRequest { Term = term, Page = 1, PageSize = size * 3 };
        var stopwatch = Stopwatch.StartNew();

        SearchResponse response = Execute(rules, request, profile.Alias, size * 3, stopwatch, cancellationToken);
        ResolveUrls(response);

        return Task.FromResult(SuggestionEngine.BuildAutocomplete(response.Results.ToList(), size));
    }

    /// <inheritdoc />
    public void RecordClick(Guid queryKey, Guid nodeKey, string nodeName, int position)
    {
        try
        {
            _analytics.RecordClick(queryKey, nodeKey, nodeName, position);
        }
        catch (Exception ex)
        {
            // Click tracking is telemetry; a failure here must never surface to a visitor.
            _logger.LogDebug(ex, "Could not record a search result click.");
        }
    }

    /// <summary>
    /// Plans, runs and post-processes a search. Kept free of analytics and suggestions so the
    /// "did you mean" pass can reuse it without recursing or double-counting.
    /// </summary>
    private SearchResponse Execute(
        SearchRuleSet rules,
        SearchRequest request,
        string profileAlias,
        int pageSize,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var planner = new SearchQueryPlanner(_catalog, _idKeyMap);
        SearchPlan plan = planner.Plan(rules, request);

        if (plan.ShortCircuit)
        {
            SearchResponse blocked = SearchResponse.Empty(request.Term, profileAlias, request.Page, pageSize);

            if (request.IncludeDiagnostics)
            {
                blocked.Diagnostics = new SearchDiagnostics
                {
                    Notes = plan.Notes.ToList(),
                    ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
                };
            }

            return blocked;
        }

        var executionNotes = new List<string>();
        IReadOnlyList<SearchResultItem> matches = ExecutePlan(plan, executionNotes, cancellationToken);

        var processor = new SearchResultProcessor(IsProtectedPath, ResolveNodeIdForFacets);
        SearchResponse response = processor.Process(matches, plan, request, profileAlias);

        TrimReturnedFields(response, plan);
        ResolveUrls(response);

        if (response.Diagnostics is not null)
        {
            foreach (var note in executionNotes)
            {
                response.Diagnostics.Notes.Add(note);
            }
        }

        return response;
    }

    /// <summary>
    /// Offers a spelling correction when a search found little or nothing.
    /// <para>
    /// The correction is drawn from a fuzzy pass over page names, so it can only ever suggest
    /// something the site actually contains - a suggestion that also returns nothing is worse than
    /// no suggestion at all.
    /// </para>
    /// </summary>
    private void AddSuggestion(
        SearchResponse response,
        SearchRuleSet rules,
        SearchRequest request,
        string profileAlias,
        CancellationToken cancellationToken)
    {
        SuggestionSettings settings = _settingsService.Get().Suggestions;

        if (!settings.Enabled || response.TotalResults > settings.SuggestBelowResultCount)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(request.Term))
        {
            return;
        }

        try
        {
            SearchRuleSet fuzzyRules = NameOnlyRules(rules, FieldMatchMode.Fuzzy, settings.Fuzziness, 50);
            var fuzzyRequest = new SearchRequest { Term = request.Term, Page = 1, PageSize = 50 };

            SearchResponse fuzzy = Execute(
                fuzzyRules,
                fuzzyRequest,
                profileAlias,
                50,
                Stopwatch.StartNew(),
                cancellationToken);

            var correction = SuggestionEngine.ChooseCorrection(
                request.Term.Trim(),
                fuzzy.Results.ToList(),
                settings);

            if (!string.IsNullOrWhiteSpace(correction)
                && !correction.Equals(request.Term.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                response.Suggestion = correction;
                response.Diagnostics?.Notes.Add($"Suggested '{correction}' after finding {response.TotalResults} result(s).");
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not build a spelling suggestion for '{Term}'.", request.Term);
        }
    }

    /// <summary>
    /// A cut-down rule set that keeps the profile's scope but searches page names only. Used by
    /// type-ahead and by the spelling pass, both of which want speed over depth.
    /// </summary>
    private static SearchRuleSet NameOnlyRules(SearchRuleSet rules, FieldMatchMode mode, float fuzziness, int maxResults)
        => new()
        {
            // Shared, never mutated - the scope of a suggestion must match the scope of the search.
            Sources = rules.Sources,
            Matching = new MatchingRules
            {
                Fields = new List<SearchFieldRule>
                {
                    new() { Name = ImobisoftSearchConstants.IndexFields.NodeName, Boost = 2f, MatchMode = mode },
                    new() { Name = ImobisoftSearchConstants.IndexFields.SystemNodeName, Boost = 1f, MatchMode = mode },
                },
                DefaultOperator = SearchOperator.Or,
                Fuzziness = fuzziness,
                MinimumQueryLength = 1,
                StopWords = rules.Matching.StopWords,
                Synonyms = rules.Matching.Synonyms,
            },
            Ranking = new RankingRules { BlockedTerms = rules.Ranking.BlockedTerms },
            Results = new ResultRules { PageSize = maxResults, MaxResults = maxResults },
        };

    private void RecordAnalytics(SearchResponse response, SearchRequest request, int durationMilliseconds)
    {
        // A dashboard preview is not a visitor searching, and recording it would pollute the reports.
        if (response.ProfileAlias == AdHocProfileAlias)
        {
            return;
        }

        // Neither is an empty-term listing: that is a search page loading its filters, not someone
        // looking for something.
        if (string.IsNullOrWhiteSpace(request.Term))
        {
            return;
        }

        try
        {
            _analytics.Record(response, request.Cultures.FirstOrDefault(), durationMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not record search analytics.");
        }
    }

    /// <summary>
    /// Runs the query against each planned index and merges the hits. Indexes are independent: one
    /// failing to parse or read must not take the whole search down.
    /// </summary>
    private IReadOnlyList<SearchResultItem> ExecutePlan(
        SearchPlan plan,
        List<string> notes,
        CancellationToken cancellationToken)
    {
        var matches = new List<SearchResultItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (IndexQueryPlan indexPlan in plan.Indexes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_examineManager.TryGetIndex(indexPlan.IndexName, out IIndex? index))
            {
                notes.Add($"Index '{indexPlan.IndexName}' is no longer registered.");
                continue;
            }

            try
            {
                ISearchResults results = index.Searcher
                    .CreateQuery()
                    .NativeQuery(indexPlan.Query)
                    .Execute(QueryOptions.SkipTake(0, plan.FetchSize));

                var added = 0;

                foreach (ISearchResult result in results)
                {
                    // The same node can sit in several indexes; the first hit wins, and indexes are
                    // planned in the order the profile listed them.
                    if (!seen.Add($"{result.Id}|{ReadValue(result, ImobisoftSearchConstants.IndexFields.IndexType)}"))
                    {
                        continue;
                    }

                    matches.Add(MapResult(result, indexPlan.IndexName));
                    added++;
                }

                notes.Add($"Index '{indexPlan.IndexName}' returned {added} match(es) of {results.TotalItemCount} total.");
            }
            catch (Exception ex)
            {
                notes.Add($"Index '{indexPlan.IndexName}' could not run the query: {ex.Message}");
                _logger.LogWarning(
                    ex,
                    "Imobisoft.Search could not query index {IndexName} with {Query}",
                    indexPlan.IndexName,
                    indexPlan.Query);
            }
        }

        return matches;
    }

    private static SearchResultItem MapResult(ISearchResult result, string indexName)
    {
        var item = new SearchResultItem
        {
            Id = result.Id,
            IndexName = indexName,
            RawScore = result.Score,
            Score = result.Score,
            Name = ReadValue(result, ImobisoftSearchConstants.IndexFields.NodeName)
                   ?? ReadValue(result, ImobisoftSearchConstants.IndexFields.SystemNodeName)
                   ?? string.Empty,
            ContentTypeAlias = ReadValue(result, ImobisoftSearchConstants.IndexFields.NodeTypeAlias) ?? string.Empty,
            IndexType = ReadValue(result, ImobisoftSearchConstants.IndexFields.IndexType) ?? string.Empty,
            Path = ReadValue(result, ImobisoftSearchConstants.IndexFields.Path) ?? ReadValue(result, "path"),
            Culture = ReadValue(result, ImobisoftSearchConstants.IndexFields.Culture),
        };

        if (Guid.TryParse(ReadValue(result, ImobisoftSearchConstants.IndexFields.Key), out Guid key))
        {
            item.Key = key;
        }

        // Every value is carried through post-processing so that sorting, faceting, de-duplication
        // and highlighting can all read whatever field the rules point them at. The set is trimmed
        // to the profile's return fields once the page has been cut.
        foreach (KeyValuePair<string, string> value in result.Values)
        {
            item.Fields[value.Key] = value.Value;
        }

        return item;
    }

    private static string? ReadValue(ISearchResult result, string field)
        => result.Values.TryGetValue(field, out var value) ? value : null;

    /// <summary>
    /// Cuts each result down to the fields the profile asked for. Done after paging so only the
    /// returned page is walked, and so post-processing keeps the full document until then.
    /// </summary>
    private static void TrimReturnedFields(SearchResponse response, SearchPlan plan)
    {
        IList<string> configured = plan.Rules.Results.ReturnFields;

        if (configured.Count == 0)
        {
            return;
        }

        var keep = new HashSet<string>(configured, StringComparer.OrdinalIgnoreCase);

        foreach (var field in AlwaysReturnedFields)
        {
            keep.Add(field);
        }

        foreach (SearchResultItem item in response.Results)
        {
            var trimmed = item.Fields
                .Where(kvp => keep.Contains(kvp.Key))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

            item.Fields = trimmed;
        }
    }

    /// <summary>
    /// Resolves a front-end URL per result. This needs a published cache, which is not guaranteed
    /// outside a front-end request, so it is best effort - a null URL is preferable to a failed
    /// search, and the backoffice preview does not need URLs at all.
    /// </summary>
    private void ResolveUrls(SearchResponse response)
    {
        if (response.Results.Count == 0)
        {
            return;
        }

        try
        {
            using var contextReference = _umbracoContextFactory.EnsureUmbracoContext();

            foreach (SearchResultItem item in response.Results)
            {
                if (!int.TryParse(item.Id, out var id))
                {
                    continue;
                }

                try
                {
                    var url = item.IndexType.Equals(ImobisoftSearchConstants.IndexTypes.Media, StringComparison.OrdinalIgnoreCase)
                        ? _publishedUrlProvider.GetMediaUrl(item.Key ?? Guid.Empty, culture: item.Culture)
                        : _publishedUrlProvider.GetUrl(id, culture: item.Culture);

                    item.Url = string.IsNullOrWhiteSpace(url) || url == "#" ? null : url;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not resolve a URL for search result {Id}.", item.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No published cache available, returning search results without URLs.");
        }
    }

    private bool IsProtectedPath(string path)
    {
        try
        {
            return _publicAccessService.IsProtected(path).Success;
        }
        catch (Exception ex)
        {
            // Failing open here would leak protected content, so an error means "treat as protected".
            _logger.LogWarning(ex, "Could not determine public access for path {Path}; excluding it from results.", path);
            return true;
        }
    }
}
