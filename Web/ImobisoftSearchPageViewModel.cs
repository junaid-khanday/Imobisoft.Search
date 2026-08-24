using Imobisoft.Search.Models;

namespace Imobisoft.Search.Web;

/// <summary>What the package's built-in search page renders.</summary>
public sealed class ImobisoftSearchPageViewModel
{
    /// <summary>What the visitor typed.</summary>
    public string Term { get; set; } = string.Empty;

    /// <summary>1-based page number.</summary>
    public int Page { get; set; } = 1;

    /// <summary>Null before a search has been run, which is how the page knows to show nothing yet.</summary>
    public SearchResponse? Response { get; set; }

    /// <summary>Path the page is served from, used to build its own links.</summary>
    public string PagePath { get; set; } = "/" + ImobisoftSearchConstants.Web.SearchPagePath;

    public bool HasSearched { get; set; }

    /// <summary>
    /// The facet dimensions the profile defines. These drive the filter bar so the dropdowns exist
    /// above the search box from the moment the page opens, with counts joining once a search - or
    /// the automatic browse pass - has produced them.
    /// </summary>
    public IList<FacetDefinition> ConfiguredFacets { get; set; } = new List<FacetDefinition>();

    /// <summary>The sort choices the profile defines, driving the page's "Sort by" dropdown.</summary>
    public IList<SortOption> ConfiguredSortOptions { get; set; } = new List<SortOption>();

    /// <summary>The currently selected sort alias, echoed so the right option renders selected.</summary>
    public string? SelectedSort { get; set; }
}
