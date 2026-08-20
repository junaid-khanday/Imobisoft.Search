using Imobisoft.Search.Models;
using Imobisoft.Search.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

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

    public SearchPreviewController(IImobisoftSearchService searchService) => _searchService = searchService;

    [HttpPost("preview")]
    [ProducesResponseType(typeof(SearchResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Preview(
        [FromBody] SearchPreviewRequest request,
        CancellationToken cancellationToken)
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
                IncludeDiagnostics = true,
            },
            cancellationToken);

        return Ok(response);
    }
}
