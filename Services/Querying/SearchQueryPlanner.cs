using Imobisoft.Search.Models;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace Imobisoft.Search.Services.Querying;

/// <summary>
/// Turns a rule set plus a term into a concrete <see cref="SearchPlan"/>. This is where the
/// dashboard's configuration stops being data and becomes a query.
/// </summary>
internal sealed class SearchQueryPlanner
{
    /// <summary>Upper bound on the fetch window, so a badly configured profile cannot pull an entire index into memory.</summary>
    private const int AbsoluteMaxFetch = 5000;

    /// <summary>Fields used when a profile has named none and the index reports nothing searchable.</summary>
    private static readonly string[] FallbackFields =
    {
        ImobisoftSearchConstants.IndexFields.NodeName,
        ImobisoftSearchConstants.IndexFields.SystemNodeName,
    };

    private readonly IIndexCatalogService _catalog;
    private readonly IIdKeyMap _idKeyMap;

    public SearchQueryPlanner(IIndexCatalogService catalog, IIdKeyMap idKeyMap)
    {
        _catalog = catalog;
        _idKeyMap = idKeyMap;
    }

    public SearchPlan Plan(SearchRuleSet rules, SearchRequest request)
    {
        MatchingRules matching = rules.Matching;
        var notes = new List<string>();

        var term = (request.Term ?? string.Empty).Trim();

        // Browse mode: no term, but the caller asked to run anyway. The plan matches every document
        // the source rules allow, which is what gives a search page its filter counts before a word
        // is typed, and what lets a filter selection alone return results.
        var browse = request.AllowEmptyTerm && term.Length == 0;

        if (term.Length == 0 && !browse)
        {
            return SearchPlan.Blocked(rules, "Empty search term.");
        }

        if (!browse && term.Length < Math.Max(1, matching.MinimumQueryLength))
        {
            return SearchPlan.Blocked(
                rules,
                $"Term is shorter than the {matching.MinimumQueryLength} character minimum.");
        }

        if (!browse && IsBlocked(term, rules.Ranking.BlockedTerms))
        {
            return SearchPlan.Blocked(rules, "Term is on the blocked list.");
        }

        IReadOnlyList<IReadOnlyList<string>> termGroups = BuildTermGroups(term, matching, notes);

        if (termGroups.Count == 0)
        {
            return SearchPlan.Blocked(rules, "Every word in the term was a stop word.");
        }

        IReadOnlyList<string> indexNames = _catalog.ResolveIndexNames(rules.Sources);

        if (indexNames.Count == 0)
        {
            return SearchPlan.Blocked(rules, "No Examine index matched the profile's index rules.");
        }

        if (rules.Sources.Indexes.Count == 0)
        {
            notes.Add($"No index restriction set, searching all {indexNames.Count} registered indexes.");
        }

        IReadOnlyList<string> cultures = ResolveCultures(rules.Sources, request);
        (IReadOnlySet<int> rootIds, IReadOnlySet<int> excludedIds) = ResolveNodeIds(rules.Sources, notes);

        if (rootIds.Count > 0)
        {
            // Worth saying out loud: the subtree filter runs after the index has handed over its
            // window, so a narrow subtree on a large site may want a bigger "maximum results
            // considered" to fill a page.
            notes.Add(
                $"Restricted to {rootIds.Count} subtree(s). This is applied to the top "
                + $"{rules.Results.MaxResults} matches rather than inside the index query.");
        }

        var indexPlans = new List<IndexQueryPlan>();

        foreach (var indexName in indexNames)
        {
            IndexQueryPlan? plan = BuildIndexPlan(indexName, rules, matching, termGroups, cultures, rootIds, browse);

            if (plan is not null)
            {
                indexPlans.Add(plan);
            }
            else
            {
                notes.Add($"Skipped index '{indexName}': it exposes no searchable field matching the profile.");
            }
        }

        if (indexPlans.Count == 0)
        {
            return SearchPlan.Blocked(rules, "No index exposed a searchable field matching the profile.");
        }

        return new SearchPlan
        {
            Rules = rules,
            TermGroups = termGroups,
            Indexes = indexPlans,
            RootNodeIds = rootIds,
            ExcludedNodeIds = excludedIds,
            ExcludeDescendants = rules.Sources.ExcludeDescendantsOfExcludedNodes,
            FetchSize = Math.Clamp(rules.Results.MaxResults <= 0 ? 500 : rules.Results.MaxResults, 1, AbsoluteMaxFetch),
            Notes = notes,
        };
    }

    private IndexQueryPlan? BuildIndexPlan(
        string indexName,
        SearchRuleSet rules,
        MatchingRules matching,
        IReadOnlyList<IReadOnlyList<string>> termGroups,
        IReadOnlyList<string> cultures,
        IReadOnlySet<int> rootIds,
        bool browse)
    {
        var available = new HashSet<string>(
            _catalog.GetIndex(indexName)?.Fields.Select(f => f.Name) ?? Enumerable.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);

        var resolvedFieldNames = new List<string>();
        var fieldClauses = new List<string>();

        if (!browse)
        {
            IReadOnlyList<SearchFieldRule> fieldRules = ResolveFieldRules(indexName, matching);

            foreach (SearchFieldRule rule in fieldRules)
            {
                foreach (var fieldName in ExpandForCultures(rule.Name, cultures, available))
                {
                    // An index that does not carry this field simply does not contribute a clause -
                    // searching several indexes with different shapes has to degrade, not fail.
                    if (available.Count > 0 && !available.Contains(fieldName))
                    {
                        continue;
                    }

                    var termClause = BuildTermClause(termGroups, rule.MatchMode, matching);

                    if (string.IsNullOrEmpty(termClause))
                    {
                        continue;
                    }

                    var clause = $"{LuceneSyntax.EscapeFieldName(fieldName)}:{LuceneSyntax.Group(termClause)}";
                    fieldClauses.Add(LuceneSyntax.Boost(clause, rule.Boost));
                    resolvedFieldNames.Add(fieldName);
                }
            }
        }

        if (fieldClauses.Count == 0 && !browse)
        {
            return null;
        }

        var matchOperator = matching.DefaultOperator == SearchOperator.And ? "AND" : "OR";
        var parts = new List<string>();

        if (browse)
        {
            // Everything the type rules allow. With no type restrictions at all this is a bare
            // match-all, which Lucene reads as "every document in the index".
            var browseTypeClause = BuildTypeClause(rules.Sources);
            parts.Add(string.IsNullOrEmpty(browseTypeClause) ? "*:*" : LuceneSyntax.Require(browseTypeClause));
        }
        else
        {
            parts.Add(LuceneSyntax.Require(LuceneSyntax.Join(fieldClauses, matchOperator)));

            var typeClause = BuildTypeClause(rules.Sources);

            if (!string.IsNullOrEmpty(typeClause))
            {
                parts.Add(LuceneSyntax.Require(typeClause));
            }
        }

        var excludedTypes = rules.Sources.ExcludeContentTypes
            .Concat(rules.Sources.ExcludeMediaTypes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (excludedTypes.Count > 0)
        {
            parts.Add(LuceneSyntax.Prohibit(
                LuceneSyntax.FieldIn(ImobisoftSearchConstants.IndexFields.NodeTypeAlias, excludedTypes)));
        }

        // Subtree restriction is deliberately not expressed in the query. Umbraco indexes both
        // __Path and path as raw fields - stored but not analysed - so a Lucene clause against them
        // matches nothing at all. The check happens in memory against the returned document instead,
        // where the path is unambiguous.

        return new IndexQueryPlan
        {
            IndexName = indexName,
            Query = string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p))),
            Fields = resolvedFieldNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        };
    }

    /// <summary>
    /// Builds the clause that limits which kinds of document can match. Document type and media type
    /// restrictions have to be scoped to their own index type, otherwise excluding a document type
    /// would silently exclude every media item too.
    /// </summary>
    private static string BuildTypeClause(SourceRules sources)
    {
        var hasIndexTypeRule = sources.IndexTypes.Count > 0;
        var hasContentRule = sources.IncludeContentTypes.Count > 0;
        var hasMediaRule = sources.IncludeMediaTypes.Count > 0;

        if (!hasIndexTypeRule && !hasContentRule && !hasMediaRule)
        {
            return string.Empty;
        }

        var allowed = hasIndexTypeRule
            ? sources.IndexTypes
            : new List<string>
            {
                ImobisoftSearchConstants.IndexTypes.Content,
                ImobisoftSearchConstants.IndexTypes.Media,
                ImobisoftSearchConstants.IndexTypes.Member,
            };

        var clauses = new List<string>();

        foreach (var indexType in allowed)
        {
            var typeClause = LuceneSyntax.FieldIn(ImobisoftSearchConstants.IndexFields.IndexType, new[] { indexType });

            IList<string>? typeRestriction = indexType.Equals(ImobisoftSearchConstants.IndexTypes.Content, StringComparison.OrdinalIgnoreCase) && hasContentRule
                ? sources.IncludeContentTypes
                : indexType.Equals(ImobisoftSearchConstants.IndexTypes.Media, StringComparison.OrdinalIgnoreCase) && hasMediaRule
                    ? sources.IncludeMediaTypes
                    : null;

            if (typeRestriction is null)
            {
                clauses.Add(typeClause);
                continue;
            }

            var aliasClause = LuceneSyntax.FieldIn(ImobisoftSearchConstants.IndexFields.NodeTypeAlias, typeRestriction);
            clauses.Add(LuceneSyntax.Join(new[] { typeClause, aliasClause }, "AND"));
        }

        return LuceneSyntax.Join(clauses, "OR");
    }

    /// <summary>
    /// Builds the term half of a field clause: one bracketed group per word, each holding the word
    /// and any synonyms, joined by AND when every word must match and OR otherwise.
    /// </summary>
    private static string BuildTermClause(
        IReadOnlyList<IReadOnlyList<string>> termGroups,
        FieldMatchMode mode,
        MatchingRules matching)
    {
        var groupClauses = new List<string>();

        foreach (IReadOnlyList<string> group in termGroups)
        {
            var variants = group
                .Select(word => LuceneSyntax.ApplyMatchMode(LuceneSyntax.EscapeTerm(word), mode, matching.Fuzziness))
                .Where(v => !string.IsNullOrEmpty(v))
                .ToList();

            if (variants.Count > 0)
            {
                groupClauses.Add(string.Join(" OR ", variants));
            }
        }

        return LuceneSyntax.Join(groupClauses, matching.AllTermsMustMatch ? "AND" : "OR");
    }

    /// <summary>
    /// Works out which fields to search. A profile that names none gets whatever the index reports as
    /// searchable, with the node name weighted up - a title match should always beat a body match.
    /// </summary>
    private IReadOnlyList<SearchFieldRule> ResolveFieldRules(string indexName, MatchingRules matching)
    {
        var configured = matching.Fields.Where(f => f.Enabled && !string.IsNullOrWhiteSpace(f.Name)).ToList();

        if (configured.Count > 0)
        {
            return configured;
        }

        IReadOnlyList<IndexFieldInfo> discovered = _catalog.GetSearchableFields(indexName);

        if (discovered.Count == 0)
        {
            return FallbackFields
                .Select(name => new SearchFieldRule { Name = name, Boost = 1f, MatchMode = FieldMatchMode.Prefix })
                .ToList();
        }

        return discovered
            .Select(field => new SearchFieldRule
            {
                Name = field.Name,
                Boost = IsNameField(field.Name) ? 10f : 1f,
                MatchMode = FieldMatchMode.Prefix,
            })
            .ToList();
    }

    private static bool IsNameField(string name)
        => name.Equals(ImobisoftSearchConstants.IndexFields.NodeName, StringComparison.OrdinalIgnoreCase)
           || name.Equals(ImobisoftSearchConstants.IndexFields.SystemNodeName, StringComparison.OrdinalIgnoreCase)
           || name.StartsWith(ImobisoftSearchConstants.IndexFields.NodeName + "_", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Umbraco stores variant content as one document with culture-suffixed field names, so
    /// restricting to a culture means searching different fields rather than adding a filter. A field
    /// with no culture-specific twin is invariant and is searched as-is.
    /// </summary>
    private static IEnumerable<string> ExpandForCultures(
        string fieldName,
        IReadOnlyList<string> cultures,
        IReadOnlySet<string> available)
    {
        if (cultures.Count == 0 || fieldName.StartsWith("__", StringComparison.Ordinal))
        {
            yield return fieldName;
            yield break;
        }

        var matched = false;

        foreach (var culture in cultures)
        {
            var variant = $"{fieldName}_{culture.ToLowerInvariant()}";

            if (available.Contains(variant))
            {
                matched = true;
                yield return variant;
            }
        }

        if (!matched)
        {
            yield return fieldName;
        }
    }

    private static IReadOnlyList<string> ResolveCultures(SourceRules sources, SearchRequest request)
        => request.Cultures.Count > 0
            ? request.Cultures.Where(c => !string.IsNullOrWhiteSpace(c)).ToList()
            : sources.Cultures.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();

    /// <summary>
    /// Resolves the GUIDs an editor picked in the dashboard to the integer node ids the index holds.
    /// A key that no longer resolves is dropped and noted rather than failing the search.
    /// </summary>
    private (IReadOnlySet<int> RootIds, IReadOnlySet<int> ExcludedIds) ResolveNodeIds(SourceRules sources, List<string> notes)
    {
        var rootIds = new HashSet<int>();
        var excludedIds = new HashSet<int>();

        foreach (Guid key in sources.RootNodeKeys)
        {
            if (TryResolveId(key, out var id))
            {
                rootIds.Add(id);
            }
            else
            {
                notes.Add($"Root node {key} no longer exists and was ignored.");
            }
        }

        foreach (Guid key in sources.ExcludedNodeKeys)
        {
            if (TryResolveId(key, out var id))
            {
                excludedIds.Add(id);
            }
            else
            {
                notes.Add($"Excluded node {key} no longer exists and was ignored.");
            }
        }

        return (rootIds, excludedIds);
    }

    private bool TryResolveId(Guid key, out int id)
    {
        foreach (UmbracoObjectTypes objectType in new[] { UmbracoObjectTypes.Document, UmbracoObjectTypes.Media })
        {
            Attempt<int> attempt = _idKeyMap.GetIdForKey(key, objectType);

            if (attempt.Success)
            {
                id = attempt.Result;
                return true;
            }
        }

        id = 0;
        return false;
    }

    private static bool IsBlocked(string term, IList<string> blockedTerms)
        => blockedTerms.Any(blocked =>
            !string.IsNullOrWhiteSpace(blocked)
            && term.Equals(blocked.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Splits the term into words, drops stop words, and expands each surviving word with its
    /// synonyms. Each returned group is one word plus its alternatives.
    /// </summary>
    private static IReadOnlyList<IReadOnlyList<string>> BuildTermGroups(
        string term,
        MatchingRules matching,
        List<string> notes)
    {
        var stopWords = new HashSet<string>(
            matching.StopWords.Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => w.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var words = term
            .Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !stopWords.Contains(w))
            .ToList();

        if (stopWords.Count > 0 && words.Count == 0)
        {
            notes.Add("Every word was a stop word.");
        }

        var groups = new List<IReadOnlyList<string>>();

        foreach (var word in words)
        {
            var variants = new List<string> { word };

            if (matching.Synonyms.TryGetValue(word, out IList<string>? synonyms))
            {
                variants.AddRange(synonyms.Where(s => !string.IsNullOrWhiteSpace(s)));
                notes.Add($"Expanded '{word}' with {synonyms.Count} synonym(s).");
            }

            groups.Add(variants);
        }

        return groups;
    }
}
