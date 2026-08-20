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
    /// Populates <see cref="SearchResponse.Diagnostics"/> with the queries that ran and the rules
    /// that were applied. The dashboard turns this on; site code normally leaves it off.
    /// </summary>
    public bool IncludeDiagnostics { get; set; }
}
