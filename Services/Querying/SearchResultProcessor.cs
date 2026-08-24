using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Imobisoft.Search.Models;

namespace Imobisoft.Search.Services.Querying;

/// <summary>
/// Everything that happens to results after the index has handed them over: the filters the index
/// cannot answer reliably, then ranking, faceting, de-duplication, ordering and paging.
/// <para>
/// This all runs over the fetch window rather than the whole index, which is what
/// <see cref="ResultRules.MaxResults"/> bounds.
/// </para>
/// </summary>
internal sealed partial class SearchResultProcessor
{
    private readonly Func<string, bool> _isProtectedPath;

    /// <param name="isProtectedPath">
    /// Answers whether a node path sits behind public access. Injected as a delegate so the
    /// processor stays free of Umbraco services and can be exercised on its own.
    /// </param>
    public SearchResultProcessor(Func<string, bool> isProtectedPath) => _isProtectedPath = isProtectedPath;

    public SearchResponse Process(
        IReadOnlyList<SearchResultItem> matches,
        SearchPlan plan,
        SearchRequest request,
        string profileAlias)
    {
        SearchRuleSet rules = plan.Rules;
        var notes = new List<string>(plan.Notes);

        IReadOnlyList<SearchResultItem> items = ApplySourceFilters(matches, plan, notes);

        items = ApplyRanking(items, plan, notes);

        // Facet counts are computed against the results that survive every filter except the facet's
        // own, which is what lets a visitor widen a selection without the other counts collapsing.
        IList<FacetResult> facets = BuildFacets(items, rules.Results.Facets, request.Filters);

        items = ApplyFacetFilters(items, rules.Results.Facets, request.Filters, notes);

        // A minimum score is a threshold on relevance, and nothing has a relevance score without a
        // term to be relevant to - applying one to a browse listing would empty it entirely.
        items = ApplyMinimumScore(items, plan.TermGroups.Count == 0 ? 0 : rules.Matching.MinimumScore, notes);
        items = Deduplicate(items, rules.Results.DeduplicateByField, notes);
        items = Sort(items, rules.Ranking.SortBy);

        var pageSize = request.PageSize is > 0 ? request.PageSize.Value : Math.Max(1, rules.Results.PageSize);
        var page = Math.Max(1, request.Page);
        var total = items.Count;

        List<SearchResultItem> pageItems = items.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        if (rules.Results.Highlight.Enabled)
        {
            ApplyHighlighting(pageItems, plan, rules.Results.Highlight);
        }

        var response = new SearchResponse
        {
            Term = request.Term,
            ProfileAlias = profileAlias,
            Results = pageItems,
            TotalResults = total,
            Page = page,
            PageSize = pageSize,
            EnableLoadMore = rules.Results.EnableLoadMore,
            Facets = facets,
        };

        if (rules.Results.GroupByContentType)
        {
            response.Groups = items
                .GroupBy(x => x.ContentTypeAlias, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        }

        if (request.IncludeDiagnostics)
        {
            response.Diagnostics = new SearchDiagnostics
            {
                IndexesSearched = plan.Indexes.Select(x => x.IndexName).ToList(),
                FieldsSearched = plan.Indexes.SelectMany(x => x.Fields).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                ResolvedTerms = plan.TermGroups.SelectMany(g => g).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                Queries = plan.Indexes.ToDictionary(x => x.IndexName, x => x.Query, StringComparer.OrdinalIgnoreCase),
                Notes = notes,
            };
        }

        return response;
    }

    /// <summary>
    /// The filters that are answered against the returned document rather than in the query, because
    /// how they are indexed varies by field type, Umbraco version and index.
    /// </summary>
    private IReadOnlyList<SearchResultItem> ApplySourceFilters(
        IReadOnlyList<SearchResultItem> items,
        SearchPlan plan,
        List<string> notes)
    {
        SourceRules sources = plan.Rules.Sources;
        var before = items.Count;

        var filtered = items.Where(item =>
        {
            if (plan.RootNodeIds.Count > 0 && !IsUnderAnyRoot(item, plan.RootNodeIds))
            {
                return false;
            }

            if (plan.ExcludedNodeIds.Count > 0 && IsExcluded(item, plan))
            {
                return false;
            }

            if (sources.RespectNaviHide && IsHidden(item))
            {
                return false;
            }

            if (sources.PublishedOnly && IsExplicitlyUnpublished(item))
            {
                return false;
            }

            if (sources.ExcludeProtected && !string.IsNullOrEmpty(item.Path) && _isProtectedPath(item.Path))
            {
                return false;
            }

            return true;
        }).ToList();

        if (filtered.Count != before)
        {
            notes.Add($"Source rules removed {before - filtered.Count} of {before} matches.");
        }

        return filtered;
    }

    private static bool IsUnderAnyRoot(SearchResultItem item, IReadOnlySet<int> rootIds)
    {
        foreach (var id in ParsePath(item.Path))
        {
            if (rootIds.Contains(id))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsExcluded(SearchResultItem item, SearchPlan plan)
    {
        if (int.TryParse(item.Id, out var itemId) && plan.ExcludedNodeIds.Contains(itemId))
        {
            return true;
        }

        if (!plan.ExcludeDescendants)
        {
            return false;
        }

        return ParsePath(item.Path).Any(plan.ExcludedNodeIds.Contains);
    }

    /// <summary>Splits an Umbraco path such as <c>-1,1050,1099</c> into its node ids.</summary>
    private static IEnumerable<int> ParsePath(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            yield break;
        }

        foreach (var segment in path.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(segment.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                yield return id;
            }
        }
    }

    private static bool IsHidden(SearchResultItem item)
        => item.Fields.TryGetValue(ImobisoftSearchConstants.IndexFields.NaviHide, out var value) && IsTruthy(value);

    /// <summary>
    /// Only drops a document when the index explicitly says it is unpublished. A published-only index
    /// carries no such field, and absence must not be read as "unpublished".
    /// </summary>
    private static bool IsExplicitlyUnpublished(SearchResultItem item)
        => item.Fields.TryGetValue(ImobisoftSearchConstants.IndexFields.Published, out var value)
           && !string.IsNullOrEmpty(value)
           && !IsTruthy(value);

    private static bool IsTruthy(string? value)
        => value is not null
           && (value.Equals("1", StringComparison.Ordinal)
               || value.Equals("y", StringComparison.OrdinalIgnoreCase)
               || value.Equals("true", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Applies the editorial thumb on the scale: per document type multipliers, a recency lift, and
    /// finally best bets, which override scoring entirely.
    /// </summary>
    private static IReadOnlyList<SearchResultItem> ApplyRanking(
        IReadOnlyList<SearchResultItem> items,
        SearchPlan plan,
        List<string> notes)
    {
        RankingRules ranking = plan.Rules.Ranking;

        foreach (SearchResultItem item in items)
        {
            var score = item.RawScore;

            if (ranking.ContentTypeBoosts.TryGetValue(item.ContentTypeAlias, out var boost))
            {
                score *= boost;
            }

            if (ranking.Recency.Enabled)
            {
                score *= RecencyMultiplier(item, ranking.Recency);
            }

            item.Score = score;
        }

        var ordered = items.OrderByDescending(x => x.Score).ToList();

        IReadOnlySet<Guid> pinned = ResolveBestBetKeys(plan, ranking);

        if (pinned.Count == 0)
        {
            return ordered;
        }

        var promoted = new List<SearchResultItem>();
        var rest = new List<SearchResultItem>();

        foreach (SearchResultItem item in ordered)
        {
            if (item.Key.HasValue && pinned.Contains(item.Key.Value))
            {
                item.IsBestBet = true;
                promoted.Add(item);
            }
            else
            {
                rest.Add(item);
            }
        }

        if (promoted.Count > 0)
        {
            notes.Add($"Pinned {promoted.Count} best bet result(s) to the top.");
        }

        promoted.AddRange(rest);
        return promoted;
    }

    /// <summary>Best bets fire when any of their terms appears in what the visitor searched for.</summary>
    private static IReadOnlySet<Guid> ResolveBestBetKeys(SearchPlan plan, RankingRules ranking)
    {
        if (ranking.BestBets.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var searchedWords = new HashSet<string>(
            plan.TermGroups.SelectMany(g => g),
            StringComparer.OrdinalIgnoreCase);

        var keys = new HashSet<Guid>();

        foreach (BestBet bet in ranking.BestBets)
        {
            var fires = bet.Terms.Any(t =>
                !string.IsNullOrWhiteSpace(t)
                && t.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(searchedWords.Contains));

            if (fires)
            {
                foreach (Guid key in bet.NodeKeys)
                {
                    keys.Add(key);
                }
            }
        }

        return keys;
    }

    /// <summary>Exponential decay: a document at the half-life age keeps half of the configured lift.</summary>
    private static float RecencyMultiplier(SearchResultItem item, RecencyBoost recency)
    {
        if (!item.Fields.TryGetValue(recency.Field, out var raw) || !TryParseDate(raw, out DateTime date))
        {
            return 1f;
        }

        var ageDays = (DateTime.UtcNow - date).TotalDays;

        if (ageDays <= 0)
        {
            return 1f + recency.Weight;
        }

        var halfLife = Math.Max(1, recency.HalfLifeDays);
        var decay = Math.Pow(0.5, ageDays / halfLife);

        return 1f + (recency.Weight * (float)decay);
    }

    private static IReadOnlyList<SearchResultItem> ApplyMinimumScore(
        IReadOnlyList<SearchResultItem> items,
        float minimumScore,
        List<string> notes)
    {
        if (minimumScore <= 0)
        {
            return items;
        }

        // A pinned result was chosen by an editor, so a score threshold must not throw it away.
        var kept = items.Where(x => x.IsBestBet || x.Score >= minimumScore).ToList();

        if (kept.Count != items.Count)
        {
            notes.Add($"Minimum score of {minimumScore} removed {items.Count - kept.Count} result(s).");
        }

        return kept;
    }

    private static IReadOnlyList<SearchResultItem> Deduplicate(
        IReadOnlyList<SearchResultItem> items,
        string field,
        List<string> notes)
    {
        if (string.IsNullOrWhiteSpace(field))
        {
            return items;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kept = new List<SearchResultItem>();

        foreach (SearchResultItem item in items)
        {
            var value = ReadSortableValue(item, field);

            // A document with no value for the field cannot be judged a duplicate, so it is kept.
            if (string.IsNullOrEmpty(value) || seen.Add(value))
            {
                kept.Add(item);
            }
        }

        if (kept.Count != items.Count)
        {
            notes.Add($"De-duplication on '{field}' removed {items.Count - kept.Count} result(s).");
        }

        return kept;
    }

    /// <summary>
    /// Sorting happens here rather than in the index so that multiple sort levels, results merged
    /// from several indexes, and pinned best bets all obey one consistent ordering.
    /// </summary>
    private static IReadOnlyList<SearchResultItem> Sort(IReadOnlyList<SearchResultItem> items, IList<SortRule> sortBy)
    {
        var effective = sortBy.Where(s => !string.IsNullOrWhiteSpace(s.Field)).ToList();

        if (effective.Count == 0)
        {
            return items;
        }

        // Best bets stay at the top whatever the sort, otherwise pinning them would be pointless.
        IOrderedEnumerable<SearchResultItem> ordered = items.OrderByDescending(x => x.IsBestBet);

        foreach (SortRule rule in effective)
        {
            SortRule captured = rule;

            ordered = captured.Direction == SortDirection.Ascending
                ? ordered.ThenBy(x => SortKey(x, captured.Field), SortKeyComparer.Instance)
                : ordered.ThenByDescending(x => SortKey(x, captured.Field), SortKeyComparer.Instance);
        }

        return ordered.ToList();
    }

    private static object? SortKey(SearchResultItem item, string field)
    {
        if (field.Equals(SortRule.ScoreField, StringComparison.OrdinalIgnoreCase))
        {
            return item.Score;
        }

        var raw = ReadSortableValue(item, field);

        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        if (TryParseDate(raw, out DateTime date))
        {
            return date;
        }

        if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        return raw;
    }

    private static string? ReadSortableValue(SearchResultItem item, string? field)
    {
        if (item is null || string.IsNullOrWhiteSpace(field))
        {
            return null;
        }

        if (field.Equals(ImobisoftSearchConstants.IndexFields.NodeName, StringComparison.OrdinalIgnoreCase) || field.Equals("name", StringComparison.OrdinalIgnoreCase))
        {
            return item.Name;
        }

        if (field.Equals(ImobisoftSearchConstants.IndexFields.NodeTypeAlias, StringComparison.OrdinalIgnoreCase) || field.Equals("contentTypeAlias", StringComparison.OrdinalIgnoreCase) || field.Equals("contentType", StringComparison.OrdinalIgnoreCase))
        {
            return item.ContentTypeAlias;
        }

        if (field.Equals(ImobisoftSearchConstants.IndexFields.Key, StringComparison.OrdinalIgnoreCase) || field.Equals("key", StringComparison.OrdinalIgnoreCase))
        {
            return item.Key?.ToString();
        }

        if (field.Equals(ImobisoftSearchConstants.IndexFields.NodeId, StringComparison.OrdinalIgnoreCase) || field.Equals("id", StringComparison.OrdinalIgnoreCase))
        {
            return item.Id;
        }

        if (field.Equals(ImobisoftSearchConstants.IndexFields.Path, StringComparison.OrdinalIgnoreCase) || field.Equals("path", StringComparison.OrdinalIgnoreCase))
        {
            return item.Path;
        }

        // Umbraco writes a sort-optimised twin for sortable fields; prefer it when it is present.
        return item.Fields.TryGetValue(ImobisoftSearchConstants.IndexFields.SortPrefix + field, out var sortValue)
            ? sortValue
            : FieldValue(item, field);
    }

    private static string? FieldValue(SearchResultItem item, string? field)
    {
        if (item is null || string.IsNullOrWhiteSpace(field))
        {
            return null;
        }

        if (item.Fields.TryGetValue(field, out var value))
        {
            return value;
        }

        if (field.Equals(ImobisoftSearchConstants.IndexFields.NodeTypeAlias, StringComparison.OrdinalIgnoreCase) || field.Equals("contentTypeAlias", StringComparison.OrdinalIgnoreCase) || field.Equals("contentType", StringComparison.OrdinalIgnoreCase))
        {
            return item.ContentTypeAlias;
        }

        if (field.Equals(ImobisoftSearchConstants.IndexFields.NodeName, StringComparison.OrdinalIgnoreCase) || field.Equals("name", StringComparison.OrdinalIgnoreCase))
        {
            return item.Name;
        }

        if (field.Equals(ImobisoftSearchConstants.IndexFields.Key, StringComparison.OrdinalIgnoreCase) || field.Equals("key", StringComparison.OrdinalIgnoreCase))
        {
            return item.Key?.ToString();
        }

        if (field.Equals(ImobisoftSearchConstants.IndexFields.Path, StringComparison.OrdinalIgnoreCase) || field.Equals("path", StringComparison.OrdinalIgnoreCase))
        {
            return item.Path;
        }

        // Case-insensitive fallback across all field keys on item.Fields
        var match = item.Fields.FirstOrDefault(kvp => kvp.Key.Equals(field, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(match.Key))
        {
            return match.Value;
        }

        return null;
    }

    private static IList<FacetResult> BuildFacets(
        IReadOnlyList<SearchResultItem> items,
        IList<FacetDefinition> definitions,
        IDictionary<string, IList<string>> selected)
    {
        var results = new List<FacetResult>();

        foreach (FacetDefinition definition in definitions.Where(d => d.Enabled && !string.IsNullOrWhiteSpace(d.Alias)))
        {
            // Every other facet's selection narrows the counts, but this facet's own does not -
            // otherwise selecting one bucket would zero out its siblings.
            IReadOnlyList<SearchResultItem> scope = ApplyFacetFilters(
                items,
                definitions.Where(d => d.Enabled && !d.Alias.Equals(definition.Alias, StringComparison.OrdinalIgnoreCase)).ToList(),
                selected,
                new List<string>());

            selected.TryGetValue(definition.Alias, out IList<string>? chosen);
            var chosenSet = new HashSet<string>(chosen ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

            var hasCustomRanges = definition.Ranges != null && definition.Ranges.Count > 0;
            IList<FacetValue> values = (!hasCustomRanges)
                ? BuildFieldFacet(scope, definition, chosenSet)
                : BuildRangeFacet(scope, definition, chosenSet);

            if (definition.HideEmpty)
            {
                values = values.Where(v => v.Count > 0 || v.IsSelected).ToList();
            }

            results.Add(new FacetResult
            {
                Alias = definition.Alias,
                Label = string.IsNullOrWhiteSpace(definition.Label) ? definition.Alias : definition.Label,
                Kind = definition.Kind,
                Values = values,
            });
        }

        return results;
    }

    private static IList<FacetValue> BuildFieldFacet(
        IReadOnlyList<SearchResultItem> items,
        FacetDefinition definition,
        IReadOnlySet<string> chosen)
    {
        var targetField = !string.IsNullOrWhiteSpace(definition.Field) ? definition.Field : (definition.Alias ?? string.Empty);
        return items
            .Select(item => FieldValue(item, targetField) ?? ReadSortableValue(item, targetField))
            .Where(v => !string.IsNullOrEmpty(v))
            .SelectMany(v => v!.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, definition.MaxValues > 0 ? definition.MaxValues : 50))
            .Select(g => new FacetValue
            {
                Value = g.Key,
                Label = g.Key,
                Count = g.Count(),
                IsSelected = chosen.Contains(g.Key),
            })
            .ToList();
    }

    private static IList<FacetValue> BuildRangeFacet(
        IReadOnlyList<SearchResultItem> items,
        FacetDefinition definition,
        IReadOnlySet<string> chosen)
        => (definition.Ranges ?? Array.Empty<FacetRange>())
            .Select(range => new FacetValue
            {
                Value = range.Alias,
                Label = string.IsNullOrWhiteSpace(range.Label) ? range.Alias : range.Label,
                Count = items.Count(item => FallsInRange(item, definition, range)),
                IsSelected = chosen.Contains(range.Alias),
            })
            .ToList();

    private static IReadOnlyList<SearchResultItem> ApplyFacetFilters(
        IReadOnlyList<SearchResultItem> items,
        IList<FacetDefinition> definitions,
        IDictionary<string, IList<string>> selected,
        List<string> notes)
    {
        if (selected.Count == 0 || definitions.Count == 0)
        {
            return items;
        }

        var before = items.Count;
        IEnumerable<SearchResultItem> filtered = items;

        foreach (FacetDefinition definition in definitions.Where(d => d.Enabled))
        {
            if (!selected.TryGetValue(definition.Alias, out IList<string>? chosen) || chosen.Count == 0)
            {
                continue;
            }

            var chosenSet = new HashSet<string>(chosen, StringComparer.OrdinalIgnoreCase);
            FacetDefinition captured = definition;
            var hasCustomRanges = captured.Ranges != null && captured.Ranges.Count > 0;

            filtered = (!hasCustomRanges)
                ? filtered.Where(item => {
                    var targetField = !string.IsNullOrWhiteSpace(captured.Field) ? captured.Field : captured.Alias;
                    var val = FieldValue(item, targetField) ?? ReadSortableValue(item, targetField);
                    if (string.IsNullOrEmpty(val)) return false;
                    var tokens = val.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    return tokens.Any(t => chosenSet.Contains(t)) || chosenSet.Contains(val);
                })
                : filtered.Where(item => {
                    var matchingRanges = (captured.Ranges ?? Array.Empty<FacetRange>())
                        .Where(r => chosenSet.Contains(r.Alias))
                        .ToList();

                    if (matchingRanges.Count > 0)
                    {
                        return matchingRanges.Any(r => FallsInRange(item, captured, r));
                    }

                    // Fallback for ad-hoc selection or aliases without explicit range bounds
                    return chosenSet.Any(val => FallsInRange(item, captured, new FacetRange { Alias = val, Label = val }));
                });
        }

        var result = filtered.ToList();

        if (result.Count != before && notes.Count >= 0 && before != result.Count)
        {
            notes.Add($"Facet filters removed {before - result.Count} result(s).");
        }

        return result;
    }

    private static bool FallsInRange(SearchResultItem item, FacetDefinition definition, FacetRange range)
    {
        if (item is null || definition is null || range is null)
        {
            return false;
        }

        var fieldName = !string.IsNullOrWhiteSpace(definition.Field) ? definition.Field : (definition.Alias ?? string.Empty);
        var raw = FieldValue(item, fieldName) ?? ReadSortableValue(item, fieldName);

        if (definition.Kind == FacetKind.Numeric)
        {
            if (string.IsNullOrEmpty(raw) || !double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
            {
                return false;
            }

            var lower = ParseNumericBound(range.From);
            var upper = ParseNumericBound(range.To);

            if (lower is null && upper is null)
            {
                (lower, upper) = ExtractNumericBounds(range.Alias, range.Label);
            }

            return (lower is null || value >= lower) && (upper is null || value < upper);
        }

        if (definition.Kind == FacetKind.DateRange)
        {
            if (string.IsNullOrEmpty(raw) || !TryParseDate(raw, out DateTime date))
            {
                return false;
            }

            DateTime? from = ParseDateBound(range.From);
            DateTime? to = ParseDateBound(range.To);

            if (from is null && to is null)
            {
                (from, to) = ExtractDateBounds(range.Alias, range.Label);
            }

            return (from is null || date >= from) && (to is null || date < to);
        }

        // Subtree / Path / Content page matching (e.g. Specific Policy Page, Subtree Root)
        if (fieldName.Equals(ImobisoftSearchConstants.IndexFields.Path, StringComparison.OrdinalIgnoreCase) ||
            fieldName.Equals("path", StringComparison.OrdinalIgnoreCase) ||
            fieldName.Equals(ImobisoftSearchConstants.IndexFields.Key, StringComparison.OrdinalIgnoreCase) ||
            fieldName.Equals("key", StringComparison.OrdinalIgnoreCase) ||
            fieldName.Equals(ImobisoftSearchConstants.IndexFields.NodeId, StringComparison.OrdinalIgnoreCase) ||
            fieldName.Equals("id", StringComparison.OrdinalIgnoreCase))
        {
            var matchTarget = !string.IsNullOrWhiteSpace(range.From) ? range.From.Trim() : (range.Alias ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(matchTarget))
            {
                return false;
            }

            if (matchTarget.StartsWith("umb://document/", StringComparison.OrdinalIgnoreCase))
            {
                matchTarget = matchTarget.Substring("umb://document/".Length).Replace("-", "");
            }

            if (item.Key.HasValue)
            {
                var keyStr = item.Key.Value.ToString();
                var keyStrN = item.Key.Value.ToString("N");
                if (keyStr.Equals(matchTarget, StringComparison.OrdinalIgnoreCase) ||
                    keyStrN.Equals(matchTarget, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (!string.IsNullOrEmpty(item.Id) && item.Id.Equals(matchTarget, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(item.Path))
            {
                var segments = item.Path.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (segments.Contains(matchTarget, StringComparer.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        // Document Type matching (e.g. specific content types like policyPage, newsArticle)
        if (fieldName.Equals(ImobisoftSearchConstants.IndexFields.NodeTypeAlias, StringComparison.OrdinalIgnoreCase) ||
            fieldName.Equals("contentTypeAlias", StringComparison.OrdinalIgnoreCase) ||
            fieldName.Equals("contentType", StringComparison.OrdinalIgnoreCase))
        {
            var matchType = !string.IsNullOrWhiteSpace(range.From) ? range.From.Trim() : (range.Alias ?? string.Empty).Trim();
            return !string.IsNullOrEmpty(item.ContentTypeAlias) &&
                   (item.ContentTypeAlias.Equals(matchType, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(range.Alias) && item.ContentTypeAlias.Equals(range.Alias, StringComparison.OrdinalIgnoreCase)));
        }

        // Generic field value matching
        var targetVal = !string.IsNullOrWhiteSpace(range.From) ? range.From.Trim() : (range.Alias ?? string.Empty).Trim();
        return !string.IsNullOrEmpty(raw) &&
               ((!string.IsNullOrEmpty(targetVal) && raw.Equals(targetVal, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(range.Alias) && raw.Equals(range.Alias, StringComparison.OrdinalIgnoreCase)));
    }

    private static double? ParseNumericBound(string value)
        => double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static (double? Lower, double? Upper) ExtractNumericBounds(string? alias, string? label)
    {
        var combined = $"{alias} {label}".Trim();
        if (string.IsNullOrWhiteSpace(combined))
        {
            return (null, null);
        }

        // e.g. 25-to-50, 25-50, 25 to 50, 25_50
        Match matchRange = Regex.Match(combined, @"(\d+(?:\.\d+)?)\s*(?:to|-|_)\s*(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
        if (matchRange.Success)
        {
            double? l = double.TryParse(matchRange.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var lVal) ? lVal : null;
            double? u = double.TryParse(matchRange.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var uVal) ? uVal : null;
            return (l, u);
        }

        // e.g. under-25, under 25, <25, less-than-25
        Match matchUnder = Regex.Match(combined, @"(?:under|<|less(?:_|-|\s)?than)\s*(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
        if (matchUnder.Success && double.TryParse(matchUnder.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var upper))
        {
            return (null, upper);
        }

        // e.g. over-100, 100+, 100-and-above, >100, 100-plus
        Match matchOver = Regex.Match(combined, @"(?:over|>|above|\+)\s*(\d+(?:\.\d+)?)|(\d+(?:\.\d+)?)\s*(?:\+|and(?:_|-|\s)?above|&above|plus)", RegexOptions.IgnoreCase);
        if (matchOver.Success)
        {
            var valStr = !string.IsNullOrEmpty(matchOver.Groups[1].Value) ? matchOver.Groups[1].Value : matchOver.Groups[2].Value;
            if (double.TryParse(valStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var lower))
            {
                return (lower, null);
            }
        }

        return (null, null);
    }

    private static (DateTime? From, DateTime? To) ExtractDateBounds(string? alias, string? label)
    {
        var combined = $"{alias} {label}".Trim();
        if (string.IsNullOrWhiteSpace(combined))
        {
            return (null, null);
        }

        // e.g. Year "2026", "2025", "2024"
        Match matchYear = Regex.Match(combined, @"\b(20\d\d)\b");
        if (matchYear.Success && int.TryParse(matchYear.Groups[1].Value, out var year))
        {
            return (new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        }

        // e.g. "past-24h", "24-hours", "last-24-hours", "today"
        if (Regex.IsMatch(combined, @"(?:24\s*h|today|last\s*24|past\s*24)", RegexOptions.IgnoreCase))
        {
            return (DateTime.UtcNow.AddHours(-24), DateTime.UtcNow);
        }

        // e.g. "past-week", "7-days", "last-7-days", "7d"
        if (Regex.IsMatch(combined, @"(?:7\s*d|week|past\s*7|last\s*7)", RegexOptions.IgnoreCase))
        {
            return (DateTime.UtcNow.AddDays(-7), DateTime.UtcNow);
        }

        // e.g. "past-month", "30-days", "last-30-days", "30d"
        if (Regex.IsMatch(combined, @"(?:30\s*d|month|past\s*30|last\s*30)", RegexOptions.IgnoreCase))
        {
            return (DateTime.UtcNow.AddDays(-30), DateTime.UtcNow);
        }

        // e.g. "past-year", "1-year", "last-year", "1y"
        if (Regex.IsMatch(combined, @"(?:1\s*y|year|past\s*year|last\s*year)", RegexOptions.IgnoreCase))
        {
            return (DateTime.UtcNow.AddYears(-1), DateTime.UtcNow);
        }

        return (null, null);
    }

    /// <summary>
    /// Accepts an ISO date or a relative expression such as <c>now-7d</c>, so that a "last week"
    /// facet keeps meaning last week rather than going stale the day it is configured.
    /// </summary>
    private static DateTime? ParseDateBound(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();

        if (value.StartsWith("now", StringComparison.OrdinalIgnoreCase))
        {
            var offset = value[3..].Trim();

            if (offset.Length == 0)
            {
                return DateTime.UtcNow;
            }

            Match match = RelativeDatePattern().Match(offset);

            if (!match.Success)
            {
                return null;
            }

            var amount = int.Parse(match.Groups["amount"].Value, CultureInfo.InvariantCulture);
            var sign = match.Groups["sign"].Value == "-" ? -1 : 1;

            return match.Groups["unit"].Value.ToLowerInvariant() switch
            {
                "d" => DateTime.UtcNow.AddDays(sign * amount),
                "w" => DateTime.UtcNow.AddDays(sign * amount * 7),
                "m" => DateTime.UtcNow.AddMonths(sign * amount),
                "y" => DateTime.UtcNow.AddYears(sign * amount),
                "h" => DateTime.UtcNow.AddHours(sign * amount),
                _ => null,
            };
        }

        return TryParseDate(value, out DateTime parsed) ? parsed : null;
    }

    /// <summary>
    /// Reads a date out of an index, whichever way it was written.
    /// <para>
    /// A date can reach us three ways: Lucene's <c>yyyyMMddHHmmssfff</c> string, a raw tick count
    /// from an Examine numeric date field, or an ordinary formatted date. The fixed-width formats
    /// are tried first, because <c>20260820155959000</c> is also a valid tick count and would
    /// otherwise be read as the year 65.
    /// </para>
    /// </summary>
    private static bool TryParseDate(string raw, out DateTime date)
    {
        date = default;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        raw = raw.Trim();

        string[] luceneFormats = { "yyyyMMddHHmmssfff", "yyyyMMddHHmmss", "yyyyMMddHHmm", "yyyyMMdd" };

        if (DateTime.TryParseExact(
                raw,
                luceneFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal,
                out date))
        {
            return true;
        }

        // Examine's DateTime field type indexes the tick count, so an all-digit value that is too
        // long to be one of the formats above is read as ticks.
        if (raw.Length > 17
            && raw.All(char.IsDigit)
            && long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            && ticks >= DateTime.MinValue.Ticks
            && ticks <= DateTime.MaxValue.Ticks)
        {
            date = new DateTime(ticks, DateTimeKind.Utc);
            return true;
        }

        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out date);
    }

    /// <summary>
    /// Builds a snippet around the first match. The snippet is HTML-encoded before the highlight tags
    /// go in, so indexed content can never inject markup into the consumer's page.
    /// </summary>
    private static void ApplyHighlighting(IList<SearchResultItem> items, SearchPlan plan, HighlightRules highlight)
    {
        var words = plan.TermGroups.SelectMany(g => g).Where(w => w.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (words.Count == 0)
        {
            return;
        }

        var pattern = string.Join("|", words.Select(Regex.Escape));
        var matcher = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        foreach (SearchResultItem item in items)
        {
            var source = ResolveHighlightSource(item, plan, highlight, matcher);

            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            item.Highlight = BuildSnippet(source, matcher, highlight);
        }
    }

    /// <summary>
    /// Chooses the text a snippet is cut from.
    /// <para>
    /// With no field named, this picks the longest searched field that actually contains the term.
    /// Taking the first field with any value instead produces a snippet from whichever field happens
    /// to come first - often a short one like a title or a URL segment - which shows the word with
    /// no sentence around it. The word has to be read in context to be worth showing at all.
    /// </para>
    /// </summary>
    private static string? ResolveHighlightSource(
        SearchResultItem item,
        SearchPlan plan,
        HighlightRules highlight,
        Regex matcher)
    {
        if (!string.IsNullOrWhiteSpace(highlight.Field))
        {
            return FieldValue(item, highlight.Field);
        }

        string? containingTerm = null;
        string? anyValue = null;

        foreach (var fieldName in plan.Indexes.SelectMany(x => x.Fields).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var value = FieldValue(item, fieldName);

            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            anyValue ??= value;

            if (matcher.IsMatch(value) && (containingTerm is null || value.Length > containingTerm.Length))
            {
                containingTerm = value;
            }
        }

        // The term matched a field we cannot show - a culture-suffixed twin, or a field the profile
        // does not return - so fall back to any text this document has, then to its name.
        return containingTerm ?? anyValue ?? item.Name;
    }

    private static string BuildSnippet(string source, Regex matcher, HighlightRules highlight)
    {
        Match first = matcher.Match(source);

        (int start, int length) = highlight.Mode == SnippetMode.Sentence
            ? FindSentence(source, first, highlight)
            : FindCharacterWindow(source, first, highlight);

        var snippet = source.Substring(start, length).Trim();

        var builder = new StringBuilder();

        if (start > 0)
        {
            builder.Append('…');
        }

        // Encoded first, so indexed content can never inject markup, then the highlight tags are
        // added around the matched words - which is why they survive the encoding.
        var encoded = WebUtility.HtmlEncode(snippet);

        builder.Append(
            highlight.HighlightMatches && !string.IsNullOrEmpty(highlight.StartTag)
                ? matcher.Replace(encoded, m => highlight.StartTag + m.Value + highlight.EndTag)
                : encoded);

        if (start + length < source.Length)
        {
            builder.Append('…');
        }

        return builder.ToString();
    }

    /// <summary>
    /// The sentence the match sits in, plus any requested neighbours.
    /// <para>
    /// Sentence boundaries are detected on <c>.</c>, <c>!</c>, <c>?</c> and line breaks, ignoring a
    /// full stop that is part of a decimal or an abbreviation like "e.g." - splitting on those
    /// produces fragments rather than sentences.
    /// </para>
    /// </summary>
    private static (int Start, int Length) FindSentence(string source, Match match, HighlightRules highlight)
    {
        var ceiling = Math.Max(40, highlight.SnippetLength);
        var anchor = match.Success ? match.Index : 0;

        var start = anchor;
        var end = anchor + (match.Success ? match.Length : 0);

        // Walk back to the start of this sentence.
        for (var i = anchor - 1; i >= 0; i--)
        {
            if (IsSentenceEnd(source, i))
            {
                start = i + 1;
                break;
            }

            start = i;
        }

        // Walk forward to the end of it.
        for (var i = end; i < source.Length; i++)
        {
            end = i + 1;

            if (IsSentenceEnd(source, i))
            {
                break;
            }
        }

        // Then widen by whole sentences on either side, if asked for.
        for (var n = 0; n < highlight.SentenceContext; n++)
        {
            start = ExpandBackOneSentence(source, start);
            end = ExpandForwardOneSentence(source, end);
        }

        while (start < source.Length && char.IsWhiteSpace(source[start]))
        {
            start++;
        }

        var length = Math.Min(end - start, source.Length - start);

        // A single runaway sentence still has to fit, so fall back to a character window around the
        // match rather than returning a paragraph.
        return length > ceiling ? FindCharacterWindow(source, match, highlight) : (start, Math.Max(0, length));
    }

    private static bool IsSentenceEnd(string source, int index)
    {
        var c = source[index];

        if (c is '\n' or '\r')
        {
            return true;
        }

        if (c is not ('.' or '!' or '?'))
        {
            return false;
        }

        // "3.5" and "e.g." are not sentence ends; a terminator followed by whitespace is.
        var next = index + 1 < source.Length ? source[index + 1] : ' ';

        return char.IsWhiteSpace(next) || next == '"' || next == '\'';
    }

    private static int ExpandBackOneSentence(string source, int start)
    {
        for (var i = start - 2; i >= 0; i--)
        {
            if (IsSentenceEnd(source, i))
            {
                return i + 1;
            }
        }

        return start > 0 ? 0 : start;
    }

    private static int ExpandForwardOneSentence(string source, int end)
    {
        for (var i = end; i < source.Length; i++)
        {
            if (IsSentenceEnd(source, i))
            {
                return i + 1;
            }
        }

        return source.Length;
    }

    /// <summary>A fixed span centred on the match, nudged to word boundaries so it never cuts mid-word.</summary>
    private static (int Start, int Length) FindCharacterWindow(string source, Match match, HighlightRules highlight)
    {
        var length = Math.Max(40, highlight.SnippetLength);
        var start = match.Success ? Math.Max(0, match.Index - (length / 3)) : 0;

        while (start > 0 && !char.IsWhiteSpace(source[start - 1]))
        {
            start--;
        }

        var take = Math.Min(length, source.Length - start);

        while (start + take < source.Length && !char.IsWhiteSpace(source[start + take - 1]))
        {
            take--;

            if (take <= 0)
            {
                take = Math.Min(length, source.Length - start);
                break;
            }
        }

        return (start, take);
    }

    [GeneratedRegex(@"^(?<sign>[+-])(?<amount>\d+)(?<unit>[dwmyh])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RelativeDatePattern();

    /// <summary>
    /// Orders mixed value types without throwing: nulls sort last, and values of different kinds fall
    /// back to comparing their text.
    /// </summary>
    private sealed class SortKeyComparer : IComparer<object?>
    {
        public static readonly SortKeyComparer Instance = new();

        public int Compare(object? x, object? y)
        {
            if (x is null && y is null)
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            if (x.GetType() == y.GetType() && x is IComparable comparable)
            {
                return comparable.CompareTo(y);
            }

            return string.Compare(x.ToString(), y.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
