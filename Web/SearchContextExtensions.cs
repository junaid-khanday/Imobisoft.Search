using Imobisoft.Search.Models;
using Microsoft.AspNetCore.Http;

namespace Imobisoft.Search.Web;

/// <summary>
/// Everything a search page needs from the last search that ran on the current request.
/// <para>
/// A search returns more than a list of results - filters with counts, a spelling suggestion, the
/// sentence each word was found in. Rather than require every site to thread that through its own
/// view models, the package leaves it on the request and exposes it here. A Razor view can call
/// these directly.
/// </para>
/// </summary>
/// <example>
/// <code>
/// @foreach (var filter in Context.GetSearchFilters())
/// {
///     foreach (var choice in filter.Values)
///     {
///         &lt;a href="@Context.ToggleFilterUrl(filter.Alias, choice.Value)"&gt;@choice.Label (@choice.Count)&lt;/a&gt;
///     }
/// }
/// </code>
/// </example>
public static class SearchContextExtensions
{
    /// <summary>Key the package stores the response under on <see cref="HttpContext.Items"/>.</summary>
    public const string ResponseItemKey = "Imobisoft.Search.Response";

    /// <summary>Response header naming the search profile that ran.</summary>
    public const string EngineHeader = "X-Imobisoft-Search";

    /// <summary>
    /// The response from the most recent search on this request, or null when none has run. A null
    /// return is the signal for a view to render no filters at all, which is what lets the same
    /// markup work whether or not the package is the active engine.
    /// </summary>
    public static SearchResponse? GetImobisoftSearchResponse(this HttpContext? context)
        => context is not null && context.Items.TryGetValue(ResponseItemKey, out var value)
            ? value as SearchResponse
            : null;

    /// <summary>Filter choices to render, with live counts. Empty when there are none.</summary>
    public static IReadOnlyList<FacetResult> GetSearchFilters(this HttpContext? context)
        => context.GetImobisoftSearchResponse()?.Facets?.ToList() ?? new List<FacetResult>();

    /// <summary>The correction to offer as "did you mean", or null when the search went fine.</summary>
    public static string? GetSearchSuggestion(this HttpContext? context)
        => context.GetImobisoftSearchResponse()?.Suggestion;

    /// <summary>
    /// The text the searched word was found in, with the word marked up - or null when snippets are
    /// switched off in the backoffice.
    /// <para>
    /// The package HTML-encodes the text and leaves only the configured highlight tag intact, so it
    /// is safe to render with <c>Html.Raw</c>.
    /// </para>
    /// </summary>
    public static string? GetSearchHighlight(this HttpContext? context, Guid nodeKey)
        => context.GetImobisoftSearchResponse()?.Results.FirstOrDefault(r => r.Key == nodeKey)?.Highlight;

    /// <inheritdoc cref="GetSearchHighlight(HttpContext, Guid)"/>
    public static string? GetSearchHighlight(this HttpContext? context, string? nodeKey)
        => Guid.TryParse(nodeKey, out Guid key) ? context.GetSearchHighlight(key) : null;

    /// <summary>The filter selections currently applied, keyed by filter alias.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> GetActiveFilters(
        this HttpContext? context,
        string prefix = "f_")
    {
        var active = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        if (context is null)
        {
            return active;
        }

        foreach (var key in context.Request.Query.Keys)
        {
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var values = context.Request.Query[key]
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!)
                .ToList();

            if (values.Count > 0)
            {
                active[key[prefix.Length..]] = values;
            }
        }

        return active;
    }

    /// <summary>True when any filter is currently applied.</summary>
    public static bool HasActiveFilters(this HttpContext? context, string prefix = "f_")
        => context.GetActiveFilters(prefix).Count > 0;

    /// <summary>
    /// Rebuilds the current search as a URL with one filter choice flipped on or off, keeping the
    /// term and every other choice intact. This is the href for a filter link.
    /// </summary>
    public static string ToggleFilterUrl(
        this HttpContext? context,
        string facetAlias,
        string value,
        string prefix = "f_")
    {
        if (context is null)
        {
            return "?";
        }

        var term = context.Request.Query["q"].ToString() ?? string.Empty;
        var parts = new List<string> { "q=" + Uri.EscapeDataString(term) };
        var removed = false;

        foreach (KeyValuePair<string, IReadOnlyList<string>> filter in context.GetActiveFilters(prefix))
        {
            var isThisFilter = filter.Key.Equals(facetAlias, StringComparison.OrdinalIgnoreCase);

            foreach (var existing in filter.Value)
            {
                // Dropping the value rather than repeating it is what turns the choice off.
                if (isThisFilter && string.Equals(existing, value, StringComparison.OrdinalIgnoreCase))
                {
                    removed = true;
                    continue;
                }

                parts.Add($"{prefix}{Uri.EscapeDataString(filter.Key)}={Uri.EscapeDataString(existing)}");
            }
        }

        if (!removed)
        {
            parts.Add($"{prefix}{Uri.EscapeDataString(facetAlias)}={Uri.EscapeDataString(value)}");
        }

        return "?" + string.Join("&", parts);
    }

    /// <summary>The current search with every filter cleared.</summary>
    public static string ClearFiltersUrl(this HttpContext? context)
        => "?q=" + Uri.EscapeDataString(context?.Request.Query["q"].ToString() ?? string.Empty);

    /// <summary>
    /// The current search at a different page, keeping the term and every filter. This is the href
    /// for a paging or "load more" link.
    /// </summary>
    public static string SearchUrlForPage(this HttpContext? context, int page, string prefix = "f_")
    {
        if (context is null)
        {
            return "?page=" + page;
        }

        var parts = new List<string>
        {
            "q=" + Uri.EscapeDataString(context.Request.Query["q"].ToString() ?? string.Empty),
        };

        foreach (KeyValuePair<string, IReadOnlyList<string>> filter in context.GetActiveFilters(prefix))
        {
            foreach (var value in filter.Value)
            {
                parts.Add($"{prefix}{Uri.EscapeDataString(filter.Key)}={Uri.EscapeDataString(value)}");
            }
        }

        if (page > 1)
        {
            parts.Add("page=" + page);
        }

        return "?" + string.Join("&", parts);
    }
}
