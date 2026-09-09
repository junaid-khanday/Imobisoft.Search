using System.Text.Json.Serialization;

namespace Imobisoft.Search.Models;

/// <summary>How much text to return around a match.</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<SnippetMode>))]
public enum SnippetMode
{
    /// <summary>
    /// The whole sentence the matched word sits in. Reads naturally, because it starts and ends
    /// where the author's sentence does rather than mid-word.
    /// </summary>
    Sentence,

    /// <summary>A fixed number of characters centred on the match, cut at word boundaries.</summary>
    Characters,
}

/// <summary>Controls the text snippet returned alongside each result.</summary>
public sealed class HighlightRules
{
    /// <summary>Whether to return a snippet of matching text at all. On by default - snippets are
    /// what make a result list scannable.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether the matched word is wrapped in <see cref="StartTag"/> and <see cref="EndTag"/> inside
    /// the snippet. Independent of <see cref="Enabled"/>, so a site can show the sentence plain, or
    /// highlight without changing how much text comes back.
    /// </summary>
    public bool HighlightMatches { get; set; } = true;

    /// <summary>Whether the snippet is a whole sentence or a fixed span of characters.</summary>
    public SnippetMode Mode { get; set; } = SnippetMode.Sentence;

    /// <summary>
    /// Field the snippet is cut from. Empty means the first configured searchable field that has a
    /// value on the document.
    /// </summary>
    public string Field { get; set; } = string.Empty;

    /// <summary>
    /// Maximum snippet length in characters. In sentence mode this is a ceiling that stops a very
    /// long sentence running away, not the target length.
    /// </summary>
    public int SnippetLength { get; set; } = 200;

    /// <summary>
    /// How many sentences either side of the matching one to include, for a little more context.
    /// 0 returns just the sentence the word is in.
    /// </summary>
    public int SentenceContext { get; set; }

    /// <summary>Wraps the matched term when <see cref="HighlightMatches"/> is on.</summary>
    public string StartTag { get; set; } = "<mark>";

    public string EndTag { get; set; } = "</mark>";
}

/// <summary>Defines the <em>shape</em> of what comes back: paging, fields and post-processing.</summary>
public sealed class ResultRules
{
    public int PageSize { get; set; } = 10;
    public bool EnableLoadMore { get; set; } = false;

    /// <summary>
    /// How many results a search page shows before the visitor has asked for anything (browse
    /// mode). Defaults to the normal page size behaviour with 10. Set to 0 to open the page with
    /// no results at all - filters still show their counts, but nothing is listed until the
    /// visitor searches.
    /// <para>
    /// This caps the untouched page only. Picking a filter counts as asking, so a filtered request
    /// is served a full <see cref="PageSize"/> page even with no term typed - otherwise a cap of 0
    /// would leave a visitor who filtered with no way to see what they had matched.
    /// </para>
    /// </summary>
    public int BrowsePageSize { get; set; } = 10;

    /// <summary>
    /// Hard ceiling on how many documents are pulled from the index before post-processing. Keeps
    /// boosting and de-duplication bounded on large sites.
    /// </summary>
    public int MaxResults { get; set; } = 500;

    /// <summary>
    /// Index fields copied into each result. Empty means the package returns a useful default set
    /// (name, url, content type, dates) plus whatever fields were searched.
    /// </summary>
    public IList<string> ReturnFields { get; set; } = new List<string>();

    /// <summary>Adds a content type grouping breakdown to the response.</summary>
    public bool GroupByContentType { get; set; }

    /// <summary>
    /// Master switch for de-duplication. When off, the same page may appear once per index it is
    /// found in. When on, identical pages collapse to one result even without a field rule, and
    /// <see cref="DeduplicateByField"/> removes deeper copies as well.
    /// </summary>
    public bool EnableDeduplication { get; set; } = true;

    /// <summary>
    /// How many facet groups must be active before ANY of them narrows results. 0 (the default)
    /// lets a single filter act alone; 2 would make every selection inert until a second filter
    /// joins it.
    /// </summary>
    public int MinimumActiveFilters { get; set; }

    /// <summary>
    /// Collapses results sharing the same value for this field, keeping the highest scoring one.
    /// Empty keeps only the identity-level de-duplication.
    /// </summary>
    public string DeduplicateByField { get; set; } = string.Empty;

    /// <summary>
    /// Name of the theme the search page renders with - a folder under
    /// <c>Views/Partials/Search/Themes/</c> holding any subset of the overridable partials
    /// (<c>results</c>, <c>filters</c>, <c>noresults</c>, <c>loadmore</c>, <c>searchbar</c>).
    /// Anything the theme does not define falls back to the package's own partial, so a theme can
    /// restyle just the result cards and inherit the rest.
    /// <para>
    /// Empty means the built-in look. The package ships one theme, <c>modern</c>; a site authors
    /// its own under <c>Views/Partials/Search/Themes/</c> and they appear in the backoffice picker
    /// alongside it, applying to the front-end search page and the Test Search panel alike.
    /// </para>
    /// </summary>
    public string Theme { get; set; } = string.Empty;

    public HighlightRules Highlight { get; set; } = new();

    /// <summary>
    /// Filter dimensions returned with live counts so the site can render a filter sidebar. Empty
    /// means no faceting, which is the default.
    /// </summary>
    public IList<FacetDefinition> Facets { get; set; } = new List<FacetDefinition>();

    /// <summary>
    /// The visitor-facing sort filter: the choices offered in a "Sort by" dropdown, such as A-Z,
    /// Z-A, price lowest-first or highest-first. When a visitor picks one it overrides the
    /// profile's ranking order for that request; otherwise <see cref="RankingRules.SortBy"/> rules.
    /// </summary>
    public IList<SortOption> SortOptions { get; set; } = new List<SortOption>();

    /// <summary>
    /// The "reset filters" control offered beside the filter dropdowns - whether it appears, what
    /// it is called, and whether it clears every facet or only named ones. Off by default.
    /// </summary>
    public ResetFilterRules ResetFilter { get; set; } = new();
}
