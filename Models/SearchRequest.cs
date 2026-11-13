namespace Imobisoft.Search.Models;

/// <summary>A single search execution.</summary>
public sealed class SearchRequest
{
    /// <summary>What the user typed.</summary>
    public string Term { get; set; } = string.Empty;

    /// <summary>
    /// Which saved profile to use. Null or empty uses the default profile.
    /// Ignored when <see cref="Rules"/> is supplied.
    /// </summary>
    public string? ProfileAlias { get; set; }

    /// <summary>
    /// Ad-hoc rules that bypass the saved profile entirely. This is how the dashboard previews
    /// unsaved changes.
    /// </summary>
    public SearchRuleSet? Rules { get; set; }

    /// <summary>1-based page number.</summary>
    public int Page { get; set; } = 1;

    /// <summary>Overrides the profile's page size when set.</summary>
    public int? PageSize { get; set; }

    /// <summary>
    /// Restricts to these cultures for this query only, on top of whatever the profile allows.
    /// </summary>
    public IList<string> Cultures { get; set; } = new List<string>();

    /// <summary>
    /// Facet buckets the visitor has selected, keyed by <see cref="FacetDefinition.Alias"/>. Values
    /// within one facet are OR'd together; separate facets are AND'd.
    /// </summary>
    public IDictionary<string, IList<string>> Filters { get; set; } =
        new Dictionary<string, IList<string>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Alias of the <see cref="SortOption"/> the visitor picked in the sort dropdown. Must match an
    /// <em>enabled</em> option on the resolved profile; anything else is ignored and the profile's
    /// ranking order applies. Null or empty means "relevance / profile default".
    /// </summary>
    public string? Sort { get; set; }

    /// <summary>
    /// Runs the search even when <see cref="Term"/> is empty, matching everything the source rules
    /// allow. This is what lets a search page show filter dimensions before anything is searched,
    /// and what lets a visitor filter without having to type a word first. A short but non-empty
    /// term still has to clear the profile's minimum query length.
    /// </summary>
    public bool AllowEmptyTerm { get; set; }

    /// <summary>
    /// Runs this search with the AI stages off, however the profile is configured.
    /// <para>
    /// This is what the "search for X instead" link on the results page sets. A feature that reads
    /// a query and quietly searches for something else has to be undoable by the person who typed
    /// it - otherwise a visitor whose query was misread has no way to insist, and no way to tell
    /// that insisting is even possible.
    /// </para>
    /// <para>
    /// Only the stages that change the query or the order. It does not touch the text tools, which
    /// nothing runs without being asked.
    /// </para>
    /// </summary>
    public bool DisableAi { get; set; }

    /// <summary>
    /// Populates <see cref="SearchResponse.Diagnostics"/> with the queries that ran and the rules
    /// that were applied. The dashboard turns this on; site code normally leaves it off.
    /// </summary>
    public bool IncludeDiagnostics { get; set; }
}
