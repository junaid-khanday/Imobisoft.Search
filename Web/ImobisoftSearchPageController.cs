using Imobisoft.Search.Configuration;
using Imobisoft.Search.Models;
using Imobisoft.Search.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Imobisoft.Search.Web;

/// <summary>
/// A working search page, served by the package.
/// <para>
/// It exists so a site has search the moment the package is installed - no controller, no view, no
/// route of its own. Everything configured in the Search section is already in effect here:
/// what is included and excluded, field weighting, sorting, filters, snippets and page size.
/// </para>
/// <para>
/// The markup is deliberately plain and carries no styling. To make it look like the site, create
/// <c>Views/ImobisoftSearch/Index.cshtml</c> in the project - a view of that name in the site wins
/// over the one shipped here, and nothing else has to change.
/// </para>
/// </summary>
[Route("~/" + ImobisoftSearchConstants.Web.SearchPagePath)]
public sealed class ImobisoftSearchController : Controller
{
    private readonly IImobisoftSearchService _search;
    private readonly ISearchProfileService _profiles;
    private readonly ImobisoftSearchOptions _options;

    public ImobisoftSearchController(
        IImobisoftSearchService search,
        ISearchProfileService profiles,
        IOptions<ImobisoftSearchOptions> options)
    {
        _search = search;
        _profiles = profiles;
        _options = options.Value;
    }

    /// <summary>The search page. A request with no term renders the form on its own.</summary>
    [HttpGet("")]
    public async Task<IActionResult> Index(
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        CancellationToken cancellationToken = default)
    {
        if (!_options.EnableSearchPage)
        {
            return NotFound();
        }

        ImobisoftSearchPageViewModel model = await BuildModel(q, page, cancellationToken);

        return View(model);
    }

    /// <summary>
    /// Just the results, for the "load more" button to append. Rendering the same partial the page
    /// uses means the appended results can never drift from the first page's.
    /// </summary>
    [HttpGet("results")]
    public async Task<IActionResult> Results(
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        CancellationToken cancellationToken = default)
    {
        if (!_options.EnableSearchPage)
        {
            return NotFound();
        }

        ImobisoftSearchPageViewModel model = await BuildModel(q, page, cancellationToken);

        return PartialView("_Results", model);
    }

    private async Task<ImobisoftSearchPageViewModel> BuildModel(string? q, int page, CancellationToken cancellationToken)
    {
        var term = (q ?? string.Empty).Trim();
        var hasQuery = !string.IsNullOrWhiteSpace(term)
                       || (Request != null && Request.Query.ContainsKey("q"))
                       || (Request != null && Request.Query.ContainsKey("sort"))
                       || HttpContext.HasActiveFilters();
        SearchProfile? profile = _profiles.ResolveForSearch(null);

        List<SortOption> configuredSorts = profile?.Rules.Results.SortOptions
            .Where(s => s.Enabled && !string.IsNullOrWhiteSpace(s.Alias) && !string.IsNullOrWhiteSpace(s.Label))
            .ToList() ?? new List<SortOption>();

        var selectedSort = Request?.Query["sort"].ToString() ?? string.Empty;
        if (!configuredSorts.Any(s => s.Alias.Equals(selectedSort, StringComparison.OrdinalIgnoreCase)))
        {
            selectedSort = string.Empty;
        }

        var model = new ImobisoftSearchPageViewModel
        {
            Term = term,
            Page = page < 1 ? 1 : page,
            PagePath = "/" + ImobisoftSearchConstants.Web.SearchPagePath,
            HasSearched = hasQuery,
            ConfiguredFacets = profile?.Rules.Results.Facets
                .Where(f => f.Enabled && !string.IsNullOrWhiteSpace(f.Alias))
                .ToList() ?? new List<FacetDefinition>(),
            ConfiguredSortOptions = configuredSorts,
            SelectedSort = selectedSort,
        };

        // With Load More disabled the profile serves exactly one page of PageSize items: later
        // pages are not served even when the URL asks for them, so the configured page size is the
        // hard limit on what a visitor can see.
        if (profile?.Rules.Results.EnableLoadMore != true)
        {
            model.Page = 1;
        }

        // A search runs when the visitor asked for one, or - before that - when the profile defines
        // filters, so the dropdowns can show their live counts on an untouched page. An empty term
        // with filters selected is a real request too: picking a filter without typing anything
        // must list what matches it.
        if (model.HasSearched || (profile?.Rules.Results.Facets.Count ?? 0) > 0 || configuredSorts.Count > 0)
        {
            // Filters and the sort choice are read off the query string by the package, so nothing
            // is passed for them here. Page size comes from the profile unless the profile says
            // otherwise.
            model.Response = await _search.SearchAsync(
                new SearchRequest
                {
                    Term = term,
                    Page = model.Page,
                    AllowEmptyTerm = string.IsNullOrWhiteSpace(term),
                    Sort = string.IsNullOrWhiteSpace(selectedSort) ? null : selectedSort,
                },
                cancellationToken);
        }

        return model;
    }
}
