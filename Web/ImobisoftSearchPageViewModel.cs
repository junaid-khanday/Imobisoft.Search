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

    /// <summary>
    /// Whether the search box still holds what was searched for, from the profile's matching rules.
    /// Off, the box is cleared after a search.
    /// </summary>
    public bool KeepSearchTerm { get; set; } = true;

    /// <summary>
    /// What the visitor typed, before AI query understanding rewrote it. Null when nothing was
    /// rewritten.
    /// <para>
    /// Kept apart from <see cref="Term"/> so the page can say which words it actually searched for
    /// and offer the original back. <see cref="Term"/> is what ran, because that is what the box
    /// and the paging links have to carry.
    /// </para>
    /// </summary>
    public string? OriginalTerm { get; set; }

    /// <summary>
    /// True when this search was run with the AI stages deliberately off, because the visitor asked
    /// for their own words. Stops the page offering a link to do what it has already done.
    /// </summary>
    public bool AiDisabled { get; set; }

    /// <summary>
    /// The layout to render the page into, or null for no layout at all - either because the site
    /// has none that can be used, or because the page is already inside one of the site's own pages.
    /// </summary>
    public string? LayoutPath { get; set; }

    /// <summary>
    /// Whether the page writes its own <c>&lt;html&gt;</c>, <c>&lt;head&gt;</c> and
    /// <c>&lt;body&gt;</c>. False when something else is already providing them: a site layout, or
    /// the site page the view component is being rendered from. A second document nested inside the
    /// site's own would be invalid.
    /// </summary>
    public bool RenderDocumentShell { get; set; } = true;
}
