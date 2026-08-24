using Imobisoft.Search.Models;

namespace Imobisoft.Search.Web;

/// <summary>
/// What the embeddable search listing component renders. A site drops
/// <c>@await Component.InvokeAsync("ImobisoftSearchListing")</c> into any template and gets the
/// whole experience - filter dropdowns above the search box from the moment the page opens,
/// results, spelling suggestions and Load More - driven entirely by the profile configured in
/// the backoffice.
/// </summary>
public sealed class ImobisoftSearchListingViewModel
{
    /// <summary>What the visitor typed.</summary>
    public string Term { get; set; } = string.Empty;

    /// <summary>1-based page number this listing shows.</summary>
    public int Page { get; set; } = 1;

    /// <summary>Null before a search has run, which is how the view knows to show the welcome box.</summary>
    public SearchResponse? Response { get; set; }

    /// <summary>True once the visitor searched or picked a filter - controls results vs welcome box.</summary>
    public bool HasSearched { get; set; }

    /// <summary>
    /// The facet dimensions the profile defines. These drive the filter bar rather than the
    /// response's facet results do alone, which is what guarantees the dropdowns exist above the
    /// search box from the moment the page opens - even before any search has run to fill in
    /// their counts.
    /// </summary>
    public IList<FacetDefinition> ConfiguredFacets { get; set; } = new List<FacetDefinition>();

    /// <summary>
    /// The sort choices the profile defines (A-Z, Z-A, price low-to-high, ...). These drive the
    /// "Sort by" dropdown the same way <see cref="ConfiguredFacets"/> drives the filter bar: from
    /// definitions, so the dropdown exists before any search has run.
    /// </summary>
    public IList<SortOption> ConfiguredSortOptions { get; set; } = new List<SortOption>();

    /// <summary>The currently selected sort alias, echoed so the right option renders selected.</summary>
    public string? SelectedSort { get; set; }
}
