using Imobisoft.Search.Models;
using Imobisoft.Search.Services;
using Microsoft.AspNetCore.Mvc;

namespace Imobisoft.Search.Web;

/// <summary>
/// The whole search experience as a single embeddable component, so any site - not just the
/// package's own page - gets it by writing one line:
/// <para><c>@await Component.InvokeAsync("ImobisoftSearchListing")</c></para>
/// <para>
/// Filter dropdowns render above the search box from the moment the page opens (with live
/// counts across everything the profile can see), picking one searches on its own without a
/// term, and Load More only ever appears when the profile enables it. Everything is driven by
/// the profile configured in the backoffice; sites customise styling only.
/// </para>
/// <para>
/// The default view lives in this package; a site can take over the markup by creating
/// <c>Views/Shared/Components/ImobisosoftSearchListing/Default.cshtml</c> in its own project -
/// that copy wins, nothing else changes.
/// </para>
/// </summary>
public sealed class ImobisoftSearchListingViewComponent : ViewComponent
{
    private readonly IImobisoftSearchService _search;
    private readonly ISearchProfileService _profiles;

    public ImobisoftSearchListingViewComponent(
        IImobisoftSearchService search,
        ISearchProfileService profiles)
    {
        _search = search;
        _profiles = profiles;
    }

    /// <summary>
    /// Renders the listing. Every argument is optional: anything left null is read from the
    /// query string, which keeps the page bookmarkable and means "Load more" works by fetching
    /// the same URL at the next page.
    /// </summary>
    /// <param name="searchTerm">Overrides the q query parameter when given.</param>
    /// <param name="pageNumber">Overrides the page query parameter when given.</param>
    /// <param name="pageSize">Overrides the profile's results-per-page rule when set.</param>
    /// <param name="profileAlias">
    /// Which saved profile to run. Null or empty uses the default profile - the same one the
    /// dashboard toggles apply to.
    /// </param>
    public async Task<IViewComponentResult> InvokeAsync(
        string? searchTerm = null,
        int? pageNumber = null,
        int? pageSize = null,
        string? profileAlias = null)
    {
        var term = (searchTerm ?? Request.Query["q"].ToString() ?? string.Empty).Trim();

        var page = pageNumber
                   ?? (int.TryParse(Request.Query["page"], out int requested) && requested > 0 ? requested : 1);

        SearchProfile? profile = _profiles.ResolveForSearch(profileAlias);

        // Definitions - not counts - decide whether the filter bar exists, so a page opens with
        // its dropdowns already above the search box exactly as configured in the backoffice.
        List<FacetDefinition> configuredFacets = profile?.Rules.Results.Facets
            .Where(f => f.Enabled && !string.IsNullOrWhiteSpace(f.Alias))
            .ToList() ?? new List<FacetDefinition>();

        // With Load More disabled the profile serves exactly one page of PageSize items: later
        // pages are not served even when the URL asks for them.
        if (profile?.Rules.Results.EnableLoadMore != true)
        {
            page = 1;
        }

        var hasSearched = !string.IsNullOrWhiteSpace(term)
                          || Request.Query.ContainsKey("q")
                          || HttpContext.HasActiveFilters();

        SearchResponse? response = null;

        // A search runs once the visitor has asked for one - or before that, when the profile
        // defines filters, so the dropdowns show live counts on an untouched page. AllowEmptyTerm
        // is what lets an empty box still answer: picking a filter without typing anything lists
        // everything the profile can see that matches it.
        if (hasSearched || (profile?.Rules.Results.Facets.Count ?? 0) > 0)
        {
            response = await _search.SearchAsync(
                new SearchRequest
                {
                    Term = term,
                    Page = page,
                    ProfileAlias = profileAlias,
                    PageSize = pageSize > 0 ? pageSize : null,
                    AllowEmptyTerm = string.IsNullOrWhiteSpace(term),
                });

            // Leave the response on the request so Context.GetSearchFilters() and friends work in
            // the host view too.
            HttpContext.Items[SearchContextExtensions.ResponseItemKey] = response;
        }

        return View(new ImobisoftSearchListingViewModel
        {
            Term = term,
            Page = page,
            Response = response,
            HasSearched = hasSearched,
            ConfiguredFacets = configuredFacets,
        });
    }
}
