using Imobisoft.Search.Models;

namespace Imobisoft.Search.Services;

/// <summary>
/// The package's search API. Inject this anywhere in a consuming site - a surface controller, a view
/// component, a Razor view, a hosted service - to run a search that obeys whatever rules have been
/// configured in the backoffice.
/// <para>
/// It is registered automatically when the package is installed, so the consuming project writes no
/// startup code.
/// </para>
/// </summary>
/// <example>
/// <code>
/// public class SearchController : SurfaceController
/// {
///     private readonly IImobisoftSearchService _search;
///
///     public async Task&lt;IActionResult&gt; Results(string q, int page = 1)
///     {
///         SearchResponse results = await _search.SearchAsync(q, page: page);
///         return View(results);
///     }
/// }
/// </code>
/// </example>
public interface IImobisoftSearchService
{
    /// <summary>Runs a search using a saved profile.</summary>
    /// <param name="term">What the visitor typed.</param>
    /// <param name="profileAlias">The profile to use. Null uses the default profile.</param>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Overrides the profile's page size when supplied.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<SearchResponse> SearchAsync(
        string term,
        string? profileAlias = null,
        int page = 1,
        int? pageSize = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a search with full control over paging, culture, facet filters and diagnostics. Supply
    /// <see cref="SearchRequest.Rules"/> to search with an unsaved rule set.
    /// </summary>
    Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Type-ahead suggestions for a partially typed term, drawn from page names in the index.
    /// Tuned for speed rather than relevance depth - call it on every keystroke.
    /// </summary>
    /// <param name="term">What has been typed so far.</param>
    /// <param name="profileAlias">Scopes suggestions to a profile's rules. Null uses the default.</param>
    /// <param name="take">How many to return. 0 uses the configured default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<SearchSuggestion>> AutocompleteAsync(
        string term,
        string? profileAlias = null,
        int take = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that a visitor opened a result, so the insights dashboard can show which searches
    /// actually answered the question and how far down the answer sat.
    /// <para>
    /// Call it from whatever handles the click-through on the site, passing
    /// <see cref="SearchResponse.QueryKey"/> from the search that produced the result. Returns
    /// immediately and never throws.
    /// </para>
    /// </summary>
    /// <param name="queryKey">The <see cref="SearchResponse.QueryKey"/> of the originating search.</param>
    /// <param name="nodeKey">The opened node's GUID key.</param>
    /// <param name="nodeName">The opened node's name, stored so reports read without a content lookup.</param>
    /// <param name="position">1-based position the result held in the list.</param>
    void RecordClick(Guid queryKey, Guid nodeKey, string nodeName, int position);
}
