using System.Collections.Concurrent;
using System.Diagnostics;
using Examine;
using Examine.Search;
using Imobisoft.Search.Configuration;
using Imobisoft.Search.Models;
using Imobisoft.Search.Services.Ai;
using Imobisoft.Search.Services.Indexing;
using Imobisoft.Search.Services.Querying;
using Imobisoft.Search.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
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
    private readonly IAiQueryPlanner _aiQuery;
    private readonly IAiReranker _aiRerank;
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
        IAiQueryPlanner aiQuery,
        IAiReranker aiRerank,
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

        // The two AI stages that run inside a search. Both are off until a profile switches them
        // on, and neither can fail a search: they are asked, and a no is the normal answer.
        _aiQuery = aiQuery;
        _aiRerank = aiRerank;

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
    public async Task<SearchResponse> SearchAsync(
        SearchRequest request,
        CancellationToken cancellationToken = default)
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
            return SearchResponse.Empty(request.Term, profileAlias, request.Page, pageSize);
        }

        if (profile is { Enabled: false })
        {
            _logger.LogDebug("Search profile '{Alias}' is disabled; returning no results.", profileAlias);
            return SearchResponse.Empty(request.Term, profileAlias, request.Page, pageSize);
        }

        ApplyQueryStringFilters(request);

        // Reads the query before it runs, so a sentence becomes the term, filters and sort order
        // the rest of this pipeline already knows how to apply. Returns null far more often than
        // not - the stage is off by default, and an ordinary keyword search has nothing to read.
        AiQueryPlan? plan = await PlanQueryAsync(rules, request, cancellationToken);

        SearchResponse response = await RunAsync(
            rules,
            request,
            profileAlias,
            pageSize,
            plan,
            stopwatch,
            cancellationToken);

        // A plan that filtered everything away has made the search worse, which is the one outcome
        // this must not ship. Running again without its filters costs a second index query and no
        // AI, and only happens on a search that was about to show nothing.
        if (response.TotalResults == 0 && plan is { Filters.Count: > 0 })
        {
            _logger.LogDebug(
                "Imobisoft.Search dropped the filters read out of \"{Term}\": they matched nothing.",
                request.Term);

            foreach (var alias in plan.Filters.Keys)
            {
                request.Filters.Remove(alias);
            }

            response = await RunAsync(
                rules,
                request,
                profileAlias,
                pageSize,
                new AiQueryPlan { Term = plan.Term, Expansions = plan.Expansions, Sort = plan.Sort },
                stopwatch,
                cancellationToken);

            response.Diagnostics?.Notes.Add(
                "AI filters matched nothing and were dropped; the search ran again without them.");
        }

        AddSuggestion(response, rules, request, profileAlias, cancellationToken);

        stopwatch.Stop();
        RecordAnalytics(response, request, (int)stopwatch.ElapsedMilliseconds);

        if (response.Diagnostics is not null)
        {
            response.Diagnostics.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
        }

        PublishToRequest(response);

        return response;
    }

    /// <summary>
    /// One pass of the engine with a plan applied: the sort the visitor or the plan asked for, a
    /// deeper first page when the results are to be re-ordered, and the re-ordering itself.
    /// </summary>
    private async Task<SearchResponse> RunAsync(
        SearchRuleSet rules,
        SearchRequest request,
        string profileAlias,
        int pageSize,
        AiQueryPlan? plan,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        SearchRuleSet planned = ApplyPlan(rules, request, plan);

        (SearchRuleSet effectiveRules, string? appliedSort) = ApplyRequestedSort(planned, request);

        // Re-ranking only reaches as far as the page it is given, so the page is fetched deeper
        // than it will be shown and trimmed afterwards. That is what lets it pull a result up from
        // eighteenth; without it the stage could only reorder the ten already on screen.
        var depth = RerankDepth(rules, request, appliedSort);
        var fetch = depth > pageSize ? depth : pageSize;

        // On the request, because that is where the pager reads the page size from - the argument
        // below only names the size an empty response reports. Put back afterwards: this request
        // is run a second time if the AI's filters turn out to match nothing, and the deeper fetch
        // is this method's own business rather than something the caller asked for.
        var asked = request.PageSize;

        if (fetch != pageSize)
        {
            request.PageSize = fetch;
        }

        SearchResponse response;

        try
        {
            response = Execute(
                effectiveRules,
                request,
                profileAlias,
                pageSize,
                stopwatch,
                cancellationToken);
        }
        finally
        {
            request.PageSize = asked;
        }

        response.SelectedSort = appliedSort;

        if (plan is not null)
        {
            response.AiQuery = plan.Describe();
            response.AiFilters = plan.DescribeFilters(rules);
            response.Diagnostics?.Notes.Add("AI read the query as: " + plan.Describe());
        }

        if (depth > 0)
        {
            await RerankAsync(response, rules, request, pageSize, cancellationToken);
        }

        // Back to the page size the caller asked for, whether or not the re-ranking happened: the
        // deeper fetch is this method's business and must not reach a view, which would otherwise
        // show twenty results on a page configured for ten.
        if (fetch != pageSize)
        {
            response.PageSize = pageSize;

            if (response.Results.Count > pageSize)
            {
                response.Results = response.Results.Take(pageSize).ToList();
            }
        }

        return response;
    }

    /// <summary>
    /// A rule set's AI rules, never null.
    /// <para>
    /// The property is declared non-nullable and a saved profile always has it, but an ad-hoc rule
    /// set is posted as JSON by the dashboard and can carry an explicit null - which would read as
    /// a missing section everywhere else and as a crash here. Defaults mean every AI stage is off,
    /// which is the right answer for a rule set that did not mention them.
    /// </para>
    /// </summary>
    private static AiRules AiRulesOf(SearchRuleSet rules) => rules.Ai ?? new AiRules();

    /// <summary>
    /// How many results to re-rank, or 0 when the stage should not run for this request.
    /// <para>
    /// The first page only: an order produced for the top twenty says nothing about what belongs on
    /// page three, and re-ranking each page independently would move results between pages as a
    /// visitor paged through them. And only on relevance - a visitor who picked "newest first" has
    /// given an instruction, and reordering underneath it would be ignoring it.
    /// </para>
    /// </summary>
    private int RerankDepth(SearchRuleSet rules, SearchRequest request, string? appliedSort)
    {
        AiSearchRules search = AiRulesOf(rules).Search;

        if (request.DisableAi
            || !search.Rerank
            || request.Page > 1
            || string.IsNullOrWhiteSpace(request.Term))
        {
            return 0;
        }

        // An explicit relevance choice is still relevance, so it is the only sort this runs under.
        if (!string.IsNullOrWhiteSpace(appliedSort))
        {
            SortOption? option = rules.Results.SortOptions.FirstOrDefault(
                o => o.Alias.Equals(appliedSort, StringComparison.OrdinalIgnoreCase));

            if (option is not null && !string.IsNullOrWhiteSpace(option.Field)
                                   && !option.Field.Equals(SortRule.ScoreField, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }
        }

        return Math.Clamp(search.RerankDepth, 5, 50);
    }

    /// <summary>
    /// Replaces the response's results with the AI's ordering, or leaves them exactly as they were.
    /// </summary>
    private async Task RerankAsync(
        SearchResponse response,
        SearchRuleSet rules,
        SearchRequest request,
        int pageSize,
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<SearchResultItem>? ordered = await _aiRerank.RerankAsync(
                request.Term,
                response.Results.ToList(),
                AiRulesOf(rules),
                cancellationToken);

            if (ordered is null)
            {
                return;
            }

            response.Results = ordered.ToList();
            response.AiReranked = true;

            response.Diagnostics?.Notes.Add(
                $"AI re-ordered the top {ordered.Count} result(s); the first {pageSize} are shown.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The visitor left. Nothing is waiting for this response.
            throw;
        }
        catch (Exception ex)
        {
            // The ordering is an improvement on the engine's, not a replacement for it. Anything
            // that goes wrong here leaves the engine's own ranking in place, which is a working
            // search - so this is logged and dropped rather than failing the request.
            _logger.LogWarning(ex, "Imobisoft.Search could not re-rank the results.");
        }
    }

    /// <summary>
    /// Asks the AI to read the query, swallowing anything that goes wrong. A stage that runs inside
    /// a search is never allowed to be the reason a search fails.
    /// </summary>
    private async Task<AiQueryPlan?> PlanQueryAsync(
        SearchRuleSet rules,
        SearchRequest request,
        CancellationToken cancellationToken)
    {
        if (request.DisableAi
            || !AiRulesOf(rules).Search.UnderstandQuery
            || string.IsNullOrWhiteSpace(request.Term))
        {
            return null;
        }

        try
        {
            return await _aiQuery.PlanAsync(request.Term, rules, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Imobisoft.Search could not read the query with AI.");

            return null;
        }
    }

    /// <summary>
    /// Applies a plan to the request and the rules: the corrected term and any filters and sort go
    /// onto the request, while the expansion words become synonyms of the term.
    /// <para>
    /// Synonyms rather than extra words in the term, because the query planner already knows how to
    /// expand a synonym into an OR group of its own. Added to the term the visitor actually typed,
    /// so the expansions widen what matches without being ranked as though the visitor had asked
    /// for them.
    /// </para>
    /// <para>
    /// The rules are cloned rather than edited. A saved profile is cached and shared by every
    /// request on the server, so writing one visitor's expansions into its synonym list would put
    /// them in everyone else's searches.
    /// </para>
    /// </summary>
    private static SearchRuleSet ApplyPlan(SearchRuleSet rules, SearchRequest request, AiQueryPlan? plan)
    {
        if (plan is null)
        {
            return rules;
        }

        // A filter the visitor selected themselves always wins: they ticked a box, and the plan is
        // reading a sentence.
        foreach (KeyValuePair<string, IList<string>> filter in plan.Filters)
        {
            if (!request.Filters.ContainsKey(filter.Key))
            {
                request.Filters[filter.Key] = filter.Value.ToList();
            }
        }

        // Same for the sort dropdown: an explicit choice is not overridden by one read out of words.
        if (!string.IsNullOrWhiteSpace(plan.Sort) && string.IsNullOrWhiteSpace(request.Sort))
        {
            request.Sort = plan.Sort;
        }

        var term = plan.Term.Length > 0 ? plan.Term : request.Term;

        if (plan.Expansions.Count == 0)
        {
            request.Term = term;

            return rules;
        }

        var synonyms = new Dictionary<string, IList<string>>(
            rules.Matching.Synonyms,
            StringComparer.OrdinalIgnoreCase);

        // Keyed on the term as it will be searched, so the expansions are found when it is looked up.
        synonyms[term] = synonyms.TryGetValue(term, out IList<string>? existing)
            ? existing.Concat(plan.Expansions).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : plan.Expansions.ToList();

        request.Term = term;

        return new SearchRuleSet
        {
            SearchBox = rules.SearchBox,
            Sources = rules.Sources,
            Matching = new MatchingRules
            {
                Fields = rules.Matching.Fields,
                DefaultOperator = rules.Matching.DefaultOperator,
                Fuzziness = rules.Matching.Fuzziness,
                MinimumQueryLength = rules.Matching.MinimumQueryLength,
                MinimumScore = rules.Matching.MinimumScore,
                StopWords = rules.Matching.StopWords,
                Synonyms = synonyms,
                AllTermsMustMatch = rules.Matching.AllTermsMustMatch,
                KeepSearchTerm = rules.Matching.KeepSearchTerm,
            },
            Ranking = rules.Ranking,
            Results = rules.Results,
            Ai = rules.Ai,
        };
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

        // An option with no field is the explicit "Relevance" choice: keep the profile's own
        // ranking untouched and just echo the alias so the dropdown can mark it as selected.
        if (string.IsNullOrWhiteSpace(option.Field))
        {
            return (rules, option.Alias);
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
        //
        // Every other section is carried across, including the ones this does not touch: a clone
        // that quietly drops the box rules or the AI rules would hand the rest of the pipeline a
        // rule set where they are at their defaults - which for the AI rules reads as "off".
        SearchRuleSet effective = new()
        {
            SearchBox = rules.SearchBox,
            Sources = rules.Sources,
            Matching = rules.Matching,
            Ranking = ranking,
            Results = rules.Results,
            Ai = rules.Ai,
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

        // The term goes in too, so what comes back can complete the word being typed rather than
        // only name the pages it was found in.
        return Task.FromResult(SuggestionEngine.BuildAutocomplete(response.Results.ToList(), size, term));
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

        // Runs after the processor rather than instead of it, so the filter counts above the list
        // still describe the whole site while the list itself is the editor's.
        ApplyCuratedBrowseResults(response, plan, request);

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

    /// <summary>
    /// Replaces the results of a browse - a search page nobody has typed into yet - with the pages
    /// the profile names, in the order it names them.
    /// <para>
    /// Read from the published cache rather than from the index, because an editor's choice has to
    /// appear whether or not it happens to fall inside the window of documents the browse query
    /// pulled back. A pick is an instruction, not a ranking signal.
    /// </para>
    /// <para>
    /// Does nothing on a real search, and nothing when the profile picks nothing. If not one pick
    /// can be resolved - every one of them unpublished, say - the automatic list is left exactly as
    /// it was, which is a working search page rather than an empty one.
    /// </para>
    /// </summary>
    private void ApplyCuratedBrowseResults(SearchResponse response, SearchPlan plan, SearchRequest request)
    {
        IList<Guid> keys = plan.Rules.Results.BrowseNodeKeys;

        if (keys.Count == 0 || !request.AllowEmptyTerm || !string.IsNullOrWhiteSpace(request.Term))
        {
            return;
        }

        var picked = new List<SearchResultItem>();

        try
        {
            using UmbracoContextReference reference = _umbracoContextFactory.EnsureUmbracoContext();
            IUmbracoContext context = reference.UmbracoContext;

            foreach (Guid key in keys)
            {
                // Media as well as content: the picker offers documents, but a profile saved
                // against a media key should still resolve rather than silently vanish.
                IPublishedContent? node = context.Content?.GetById(key) ?? context.Media?.GetById(key);

                if (node is null)
                {
                    continue;
                }

                picked.Add(Describe(node, plan.Rules.Results));
            }
        }
        catch (Exception ex)
        {
            // No published cache - the backoffice preview, for one. The automatic list stands.
            _logger.LogDebug(ex, "Could not read the curated browse results from the published cache.");

            return;
        }

        if (picked.Count == 0)
        {
            _logger.LogInformation(
                "The profile names {Count} page(s) to open the search page with, but none of them "
                + "could be found - unpublished or deleted, most likely. The search page is showing "
                + "its usual results instead.",
                keys.Count);

            return;
        }

        // The same pass the index results get, so a picked page carries a description and a real
        // date rather than being the one kind of result that does not.
        SearchResultProcessor.Describe(picked, plan.Rules.Results);

        response.Results = picked;
        response.TotalResults = picked.Count;
        response.Page = 1;

        // Every pick is on this one page, which is what makes TotalPages one and HasMore false -
        // both are worked out from these two numbers. Load More has nothing left to fetch.
        response.PageSize = picked.Count;
        response.EnableLoadMore = false;
    }

    /// <summary>One picked page, in the shape the rest of the pipeline and the view expect.</summary>
    private static SearchResultItem Describe(IPublishedContent node, ResultRules results)
    {
        var isMedia = node.ItemType == PublishedItemType.Media;

        var item = new SearchResultItem
        {
            Id = node.Id.ToString(),
            Key = node.Key,
            Name = node.Name ?? string.Empty,
            ContentTypeAlias = node.ContentType.Alias,
            IndexType = isMedia
                ? ImobisoftSearchConstants.IndexTypes.Media
                : ImobisoftSearchConstants.IndexTypes.Content,
            Path = node.Path,

            // Picked, not scored. Nothing here was ranked against anything.
            RawScore = 0,
            Score = 0,
        };

        // The same fields a result off the index carries, so a view reading updateDate for its
        // date line finds one here too.
        item.Fields[ImobisoftSearchConstants.IndexFields.NodeName] = item.Name;
        item.Fields[ImobisoftSearchConstants.IndexFields.NodeTypeAlias] = item.ContentTypeAlias;
        item.Fields[ImobisoftSearchConstants.IndexFields.CreateDate] = node.CreateDate.ToString("O");
        item.Fields[ImobisoftSearchConstants.IndexFields.UpdateDate] = node.UpdateDate.ToString("O");

        // The fields a description is looked for in, copied off the node so the shared pass can
        // find one here exactly as it does on a result that came from the index.
        IEnumerable<string> aliases = results.DescriptionFields.Count > 0
            ? results.DescriptionFields.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim())
            : SearchResultProcessor.DefaultDescriptionFields;

        foreach (var alias in aliases)
        {
            if (item.Fields.ContainsKey(alias))
            {
                continue;
            }

            // The property API rather than the Value() extension, which wants an
            // IPublishedValueFallback this does not need: the value is either on the node or it is
            // not, and the next alias in the list is the fallback.
            var value = node.GetProperty(alias)?.GetValue()?.ToString();

            if (!string.IsNullOrWhiteSpace(value))
            {
                item.Fields[alias] = value;
            }
        }

        AddExtractedContent(item, node);

        return item;
    }

    /// <summary>
    /// Builds the clean body-text field for a node read from the published cache, using the same
    /// rules the indexer uses.
    /// <para>
    /// A curated browse result - the pages a profile opens its search page with - never passes
    /// through Examine, so it cannot inherit the <c>extractedContent</c> the indexer writes. Left
    /// alone it falls through the description list to <c>bodyText</c>, which on a block-built page
    /// is the editor's raw JSON: the one kind of result that shows block layout data instead of a
    /// sentence, on the one page where every result needs a description because none of them
    /// matched anything.
    /// </para>
    /// <para>
    /// Source values, not rendered ones, because the source is what the indexer sees and the two
    /// have to agree - a browse description that read differently from the same page's search
    /// description would be a puzzle for whoever noticed.
    /// </para>
    /// </summary>
    private static void AddExtractedContent(SearchResultItem item, IPublishedContent node)
    {
        var field = ImobisoftSearchConstants.IndexFields.ExtractedContent;

        if (item.Fields.ContainsKey(field))
        {
            return;
        }

        try
        {
            var values = node.Properties
                .Select(p => new KeyValuePair<string, IEnumerable<object>>(
                    p.Alias,
                    new object[] { p.GetSourceValue() ?? string.Empty }))
                .ToList();

            var text = ExtractedContentBuilder.Build(values);

            if (text.Length > 0)
            {
                item.Fields[field] = text;
            }
        }
        catch
        {
            // A property that throws on read leaves this result without a description rather than
            // without a search page. The name and the link still work.
        }
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
