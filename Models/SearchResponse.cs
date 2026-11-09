namespace Imobisoft.Search.Models;

/// <summary>One hit.</summary>
public sealed class SearchResultItem
{
    /// <summary>The Examine document id, which for content is the Umbraco node id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The node's GUID key, when the index carries one.</summary>
    public Guid? Key { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Document or media type alias.</summary>
    public string ContentTypeAlias { get; set; } = string.Empty;

    /// <summary><c>content</c>, <c>media</c> or <c>member</c>.</summary>
    public string IndexType { get; set; } = string.Empty;

    /// <summary>Which Examine index this hit came from.</summary>
    public string IndexName { get; set; } = string.Empty;

    /// <summary>Relevance after ranking rules have been applied.</summary>
    public float Score { get; set; }

    /// <summary>Relevance as Lucene returned it, before boosts and pinning.</summary>
    public float RawScore { get; set; }

    /// <summary>True when this result was pinned by a best bet rather than earned by scoring.</summary>
    public bool IsBestBet { get; set; }

    public string? Url { get; set; }

    public string? Culture { get; set; }

    public string? Path { get; set; }

    /// <summary>Snippet built from the highlight rules, when highlighting is on.</summary>
    public string? Highlight { get; set; }

    /// <summary>
    /// What the page says about itself - its meta description, summary or opening text - read from
    /// the first of <see cref="ResultRules.DescriptionFields"/> that has anything in it.
    /// <para>
    /// This is what a result line shows when there is no <see cref="Highlight"/> to show instead,
    /// which is every result on a search page nobody has typed into yet, and any result that
    /// matched on something other than its prose. A result with a name and nothing under it tells
    /// a visitor nothing about whether to click it.
    /// </para>
    /// <para>Plain text: markup is stripped and structured values flattened, so it is safe to print.</para>
    /// </summary>
    public string? Summary { get; set; }

    /// <summary>
    /// When the page was created, read out of the index whichever way it was written there - a
    /// Lucene date string, a tick count or a formatted date - so a view never has to print the raw
    /// value, which is a long run of digits.
    /// </summary>
    public DateTime? CreateDate { get; set; }

    /// <summary>When the page was last edited. Read the same way as <see cref="CreateDate"/>.</summary>
    public DateTime? UpdateDate { get; set; }

    /// <summary>The fields requested by <see cref="ResultRules.ReturnFields"/>.</summary>
    public IDictionary<string, string> Fields { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Explains what the engine did, for the dashboard's test panel.</summary>
public sealed class SearchDiagnostics
{
    /// <summary>Indexes that were actually queried after the source rules were resolved.</summary>
    public IList<string> IndexesSearched { get; set; } = new List<string>();

    /// <summary>Fields that carried the query, with the boost each contributed.</summary>
    public IList<string> FieldsSearched { get; set; } = new List<string>();

    /// <summary>The term after stop words and synonym expansion.</summary>
    public IList<string> ResolvedTerms { get; set; } = new List<string>();

    /// <summary>The Lucene query per index, as Examine rendered it.</summary>
    public IDictionary<string, string> Queries { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Human readable notes about rules that fired, e.g. "blocked term" or "3 best bets pinned".</summary>
    public IList<string> Notes { get; set; } = new List<string>();

    public long ElapsedMilliseconds { get; set; }
}

/// <summary>The result of a search.</summary>
public sealed class SearchResponse
{
    public string Term { get; set; } = string.Empty;

    /// <summary>Alias of the profile that ran, or <c>ad-hoc</c> for a preview.</summary>
    public string ProfileAlias { get; set; } = string.Empty;

    /// <summary>
    /// Identifies this search in the analytics record. Pass it back with
    /// <c>IImobisoftSearchService.RecordClick</c> when a visitor opens a result, so the reports can
    /// show which searches actually answered the question.
    /// </summary>
    public Guid QueryKey { get; set; } = Guid.NewGuid();

    public IList<SearchResultItem> Results { get; set; } = new List<SearchResultItem>();

    /// <summary>Total matches before paging, capped by <see cref="ResultRules.MaxResults"/>.</summary>
    public int TotalResults { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; }

    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalResults / (double)PageSize);

    /// <summary>True when there are more results available to fetch for Load More or Infinite Scroll.</summary>
    public bool HasMore => Page < TotalPages;

    /// <summary>Whether Load More pagination is configured for this profile.</summary>
    public bool EnableLoadMore { get; set; }

    /// <summary>Counts per document type, populated when <see cref="ResultRules.GroupByContentType"/> is on.</summary>
    public IDictionary<string, int> Groups { get; set; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Computed filter dimensions, populated when the profile defines facets. Counts reflect the
    /// matched result window before paging.
    /// </summary>
    public IList<FacetResult> Facets { get; set; } = new List<FacetResult>();

    /// <summary>
    /// Alias of the <see cref="SortOption"/> applied to this response, echoed back so the site can
    /// mark the right choice as selected. Null when the profile's default ranking was used.
    /// </summary>
    public string? SelectedSort { get; set; }

    /// <summary>
    /// A corrected term to offer as "did you mean", set when the query returned little or nothing
    /// and a close alternative exists in the index.
    /// </summary>
    public string? Suggestion { get; set; }

    /// <summary>
    /// What AI query understanding made of the query, in a few words - "term rewritten to "3
    /// bedroom house"; filter location=Leeds" - or null when the stage did not run or found nothing
    /// to change.
    /// <para>
    /// Here rather than only in the diagnostics because a visitor should be able to see that their
    /// search was interpreted, and undo it. A search silently answering a different question than
    /// the one typed is the failure mode of this feature, and the fix for it is saying so.
    /// </para>
    /// </summary>
    public string? AiQuery { get; set; }

    /// <summary>
    /// Filters AI query understanding selected, in the words a visitor reads - "Location: Leeds".
    /// Empty when it selected none.
    /// <para>
    /// These narrow the results without appearing in the URL, so the filter bar does not show them
    /// as ticked and "Clear active filters" does not reach them - both read the query string, which
    /// is the visitor's own record of what they asked for and not a place for this to write. So the
    /// page states them instead, beside the one link that undoes the whole interpretation. A
    /// narrowing nobody can see and nobody can lift is the failure mode of this feature.
    /// </para>
    /// </summary>
    public IList<string> AiFilters { get; set; } = new List<string>();

    /// <summary>
    /// True when the results were re-ordered by AI rather than by the profile's ranking alone.
    /// </summary>
    public bool AiReranked { get; set; }

    /// <summary>Only populated when <see cref="SearchRequest.IncludeDiagnostics"/> is set.</summary>
    public SearchDiagnostics? Diagnostics { get; set; }

    public static SearchResponse Empty(string term, string profileAlias, int page, int pageSize) => new()
    {
        Term = term,
        ProfileAlias = profileAlias,
        Page = page,
        PageSize = pageSize,
    };
}
