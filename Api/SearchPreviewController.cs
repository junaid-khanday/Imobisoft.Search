using Imobisoft.Search.Models;
using Imobisoft.Search.Services;
using Imobisoft.Search.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Imobisoft.Search.Api;

/// <summary>What the dashboard's test panel posts to try a rule set before it is saved.</summary>
public sealed class SearchPreviewRequest
{
    /// <summary>The term to try.</summary>
    public string Term { get; set; } = string.Empty;

    /// <summary>
    /// Rules to run with. Supplying these previews unsaved edits; leaving them null runs the saved
    /// profile named by <see cref="ProfileAlias"/>.
    /// </summary>
    public SearchRuleSet? Rules { get; set; }

    public string? ProfileAlias { get; set; }

    public int Page { get; set; } = 1;

    public int? PageSize { get; set; }

    /// <summary>
    /// Alias of the sort option to apply. The backoffice has no query string to read this from the
    /// way a search page does, so the panel passes the editor's choice explicitly.
    /// </summary>
    public string? Sort { get; set; }

    public IList<string> Cultures { get; set; } = new List<string>();

    public IDictionary<string, IList<string>> Filters { get; set; } =
        new Dictionary<string, IList<string>>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Runs a search from the backoffice. Always returns diagnostics, so an editor can see the query
/// that ran and which rules changed the outcome rather than guessing why a result did or did not
/// appear.
/// </summary>
public sealed class SearchPreviewController : ImobisoftSearchControllerBase
{
    private readonly IImobisoftSearchService _searchService;
    private readonly ISearchProfileService _profileService;
    private readonly ISearchThemeService _themes;
    private readonly ISearchRenderService _renderer;
    private readonly ILogger<SearchPreviewController> _logger;

    public SearchPreviewController(
        IImobisoftSearchService searchService,
        ISearchProfileService profileService,
        ISearchThemeService themes,
        ISearchRenderService renderer,
        ILogger<SearchPreviewController> logger)
    {
        _searchService = searchService;
        _profileService = profileService;
        _themes = themes;
        _renderer = renderer;
        _logger = logger;
    }

    [HttpPost("preview")]
    [ProducesResponseType(typeof(SearchResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Preview(
        [FromBody] SearchPreviewRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            SearchResponse response = await _searchService.SearchAsync(
                new SearchRequest
                {
                    Term = request.Term,
                    Rules = request.Rules,
                    ProfileAlias = request.ProfileAlias,
                    Page = request.Page,
                    PageSize = request.PageSize,
                    Cultures = request.Cultures,
                    Filters = request.Filters,
                    Sort = request.Sort,
                    IncludeDiagnostics = true,

                    // A blank term browses rather than being refused, which is what lets the test
                    // panel open with the profile's filters and their counts already on screen -
                    // the same browse pass the front-end search page runs before a word is typed.
                    // An editor testing filters should not have to invent a search term first.
                    AllowEmptyTerm = string.IsNullOrWhiteSpace(request.Term),
                },
                cancellationToken);

            return Ok(response);
        }
        catch (Exception ex)
        {
            var fallback = SearchResponse.Empty(request.Term, request.ProfileAlias ?? "default", request.Page, request.PageSize ?? 20);
            fallback.Diagnostics = new SearchDiagnostics
            {
                Notes = new List<string> { $"Search preview error: {ex.Message}" }
            };
            return Ok(fallback);
        }
    }

    /// <summary>
    /// Runs a search and returns it rendered through the profile's theme, as the markup the site
    /// would serve. The test panel injects this so what an editor previews is the page itself
    /// rather than the panel's own idea of a result list.
    /// </summary>
    [HttpPost("preview/render")]
    [ProducesResponseType(typeof(SearchRenderResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Render(
        [FromBody] SearchPreviewRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            SearchResponse response = await _searchService.SearchAsync(
                new SearchRequest
                {
                    Term = request.Term,
                    Rules = request.Rules,
                    ProfileAlias = request.ProfileAlias,
                    Page = request.Page,
                    PageSize = request.PageSize,
                    Cultures = request.Cultures,
                    Filters = request.Filters,
                    Sort = request.Sort,
                    AllowEmptyTerm = string.IsNullOrWhiteSpace(request.Term),
                },
                cancellationToken);

            // Unsaved edits win, so an editor previewing a theme change sees it before saving.
            SearchProfile? profile = _profileService.ResolveForSearch(request.ProfileAlias);
            SearchRuleSet rules = request.Rules ?? profile?.Rules ?? new SearchRuleSet();

            var model = new ImobisoftSearchListingViewModel
            {
                Term = request.Term,
                Page = response.Page,
                Response = response,
                HasSearched = !string.IsNullOrWhiteSpace(request.Term) || request.Filters.Count > 0,
                ConfiguredFacets = rules.Results.Facets
                    .Where(f => f.Enabled && !string.IsNullOrWhiteSpace(f.Alias))
                    .ToList(),
                ConfiguredSortOptions = rules.Results.SortOptions
                    .Where(s => s.Enabled && !string.IsNullOrWhiteSpace(s.Alias) && !string.IsNullOrWhiteSpace(s.Label))
                    .ToList(),
                // Echo whatever actually applied, so the theme's sort dropdown marks the right
                // option rather than snapping back to Relevance after every change.
                SelectedSort = response.SelectedSort ?? request.Sort,
                Theme = rules.Results.Theme,
                ResetFilter = rules.Results.ResetFilter,
            };

            // Two fragments rather than one, so the panel can put its search box between them and
            // read filters -> box -> results the way a search page does. The search box and Load
            // More are left out of both because the panel supplies working ones, and a second set
            // inside a sandboxed frame would just be controls that do nothing.
            var filtersHtml = await _renderer.RenderAsync(
                HttpContext,
                "~/Views/Partials/Search/preview-filters.cshtml",
                model);

            var resultsHtml = await _renderer.RenderAsync(
                HttpContext,
                "~/Views/Partials/Search/preview-results.cshtml",
                model);

            return Ok(new SearchRenderResponse
            {
                FiltersHtml = filtersHtml,
                ResultsHtml = resultsHtml,
                Theme = rules.Results.Theme ?? string.Empty,
                TotalResults = response.TotalResults,
            });
        }
        catch (Exception ex)
        {
            // A broken theme must not take the test panel down with it - the editor needs to be
            // told which view failed so they can go and fix it.
            _logger.LogWarning(ex, "Could not render the themed search preview.");

            return Ok(new SearchRenderResponse
            {
                Error = ex.Message,
            });
        }
    }
}

/// <summary>The rendered preview returned to the dashboard's test panel.</summary>
public sealed class SearchRenderResponse
{
    /// <summary>
    /// The theme's filter strip, as its own document. Kept apart from the results so the panel can
    /// place its search box between the two.
    /// </summary>
    public string FiltersHtml { get; set; } = string.Empty;

    /// <summary>The theme's result markup, as its own document.</summary>
    public string ResultsHtml { get; set; } = string.Empty;

    /// <summary>Theme that produced it, echoed so the panel can label the preview.</summary>
    public string Theme { get; set; } = string.Empty;

    /// <summary>Matches behind the render, so the panel can show a count beside the preview.</summary>
    public int TotalResults { get; set; }

    /// <summary>Set when the theme's own view threw; the panel shows this instead of blank markup.</summary>
    public string? Error { get; set; }
}
