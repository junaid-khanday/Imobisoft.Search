using Imobisoft.Search.Models;
using Imobisoft.Search.Web;
using Microsoft.AspNetCore.Http;

namespace Imobisoft.Search.Services;

/// <summary>
/// Builds what the search page renders: the term, the filters, the sort choice and the results.
/// <para>
/// Shared rather than living in the controller, because the search page is reached two ways and both
/// have to behave identically. The package serves it at its own route, and a site can render it
/// inside one of its own pages through <see cref="ImobisoftSearchPageViewComponent"/> - which is the
/// only way to get the site's header and footer around it when the site's layout declares a view
/// model of its own.
/// </para>
/// </summary>
public interface ISearchPageModelBuilder
{
    /// <summary>
    /// Reads the term, page, filters and sort from the current request and runs the search.
    /// </summary>
    /// <param name="term">
    /// What to search for. Null reads <c>q</c> from the query string, which is what a site rendering
    /// the view component wants - the search box posts to the page it is on.
    /// </param>
    /// <param name="page">1-based page number. 0 or less reads it from the query string.</param>
    /// <param name="cancellationToken">Abandons the search when the request goes away.</param>
    Task<ImobisoftSearchPageViewModel> BuildAsync(
        string? term = null,
        int page = 0,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class SearchPageModelBuilder : ISearchPageModelBuilder
{
    /// <summary>
    /// The query string parameter the term is read from. What this page's own form submits, and
    /// first in <see cref="TermParameters"/>.
    /// </summary>
    private const string DefaultTermParameter = "q";

    /// <summary>
    /// Parameters the term is accepted on, in order of preference.
    /// <para>
    /// More than one because a site whose search page this package has taken over has links,
    /// bookmarks and search engine results already pointing at it, written with whatever parameter
    /// the site's own search used. A visitor arriving on <c>/search?keyword=roofing</c> has asked a
    /// question, and answering it matters more than which word the site spelled it with. Everything
    /// this page then links to uses <see cref="DefaultTermParameter"/>, so a URL only looks like the
    /// site's old one until the first search is run.
    /// </para>
    /// </summary>
    private static readonly string[] TermParameters =
    {
        DefaultTermParameter,
        "term",
        "query",
        "search",
        "searchterm",
        "searchtext",
        "keyword",
        "keywords",
    };

    private readonly IImobisoftSearchService _search;
    private readonly ISearchProfileService _profiles;
    private readonly IHttpContextAccessor _http;

    public SearchPageModelBuilder(
        IImobisoftSearchService search,
        ISearchProfileService profiles,
        IHttpContextAccessor http)
    {
        _search = search;
        _profiles = profiles;
        _http = http;
    }

    /// <inheritdoc />
    public async Task<ImobisoftSearchPageViewModel> BuildAsync(
        string? term = null,
        int page = 0,
        CancellationToken cancellationToken = default)
    {
        HttpContext? context = _http.HttpContext;
        IQueryCollection? query = context?.Request.Query;

        SearchProfile? profile = _profiles.ResolveForSearch(null);

        // Which parameter this request carried the term on. Normally "q" - what this page's form
        // submits and what the header box is configured with - but a link written before the
        // package took this page over will have used the site's own name for it.
        var parameter = TermParameter(query, profile);

        var searchFor = (term ?? query?[parameter].ToString() ?? string.Empty).Trim();

        if (page <= 0)
        {
            page = int.TryParse(query?["page"].ToString(), out var fromQuery) && fromQuery > 0
                ? fromQuery
                : 1;
        }

        var hasQuery = !string.IsNullOrWhiteSpace(searchFor)
                       || (query?.ContainsKey(parameter) ?? false)
                       || (query?.ContainsKey("sort") ?? false)
                       || (context?.HasActiveFilters() ?? false);

        List<SortOption> configuredSorts = profile?.Rules.Results.SortOptions
            .Where(s => s.Enabled && !string.IsNullOrWhiteSpace(s.Alias) && !string.IsNullOrWhiteSpace(s.Label))
            .ToList() ?? new List<SortOption>();

        var selectedSort = query?["sort"].ToString() ?? string.Empty;

        if (!configuredSorts.Any(s => s.Alias.Equals(selectedSort, StringComparison.OrdinalIgnoreCase)))
        {
            selectedSort = string.Empty;
        }

        var model = new ImobisoftSearchPageViewModel
        {
            Term = searchFor,
            Page = page,

            // Where the page's own form posts back to. The current path when a site is rendering
            // search inside one of its own pages, so a search stays on that page rather than
            // jumping to the package's route.
            PagePath = CurrentPath(context),
            HasSearched = hasQuery,
            ConfiguredFacets = profile?.Rules.Results.Facets
                .Where(f => f.Enabled && !string.IsNullOrWhiteSpace(f.Alias))
                .ToList() ?? new List<FacetDefinition>(),
            ConfiguredSortOptions = configuredSorts,
            SelectedSort = selectedSort,

            // "Keep the searched term" lives on the profile's matching rules, so the box on this
            // page behaves the same way as the header box.
            KeepSearchTerm = profile?.Rules.Matching.KeepSearchTerm ?? true,
        };

        // With Load More disabled the profile serves exactly one page of PageSize items: later
        // pages are not served even when the URL asks for them, so the configured page size is the
        // hard limit on what a visitor can see.
        if (profile?.Rules.Results.EnableLoadMore != true)
        {
            model.Page = 1;
        }

        // "Results before searching" on the profile's Results tab: how many results the page opens
        // with. Above zero, the browse pass runs on a page nobody has typed into yet - which is the
        // whole point of the setting, and it has to hold on the first load of the page rather than
        // only once something has been submitted.
        var browseSize = profile?.Rules.Results.BrowsePageSize ?? 0;

        // A search runs when the visitor asked for one, or - before that - when the profile opens
        // the page with results, or when it defines filters, so the dropdowns can show their live
        // counts on an untouched page. An empty term with filters selected is a real request too:
        // picking a filter without typing anything must list what matches it.
        if (model.HasSearched
            || browseSize > 0
            || (profile?.Rules.Results.Facets.Count ?? 0) > 0
            || configuredSorts.Count > 0)
        {
            // Filters and the sort choice are read off the query string by the package, so nothing
            // is passed for them here. Page size comes from the profile unless the profile says
            // otherwise.
            model.Response = await _search.SearchAsync(
                new SearchRequest
                {
                    Term = searchFor,
                    Page = model.Page,
                    AllowEmptyTerm = string.IsNullOrWhiteSpace(searchFor),
                    Sort = string.IsNullOrWhiteSpace(selectedSort) ? null : selectedSort,
                },
                cancellationToken);
        }

        return model;
    }

    /// <summary>
    /// The parameter on this request that carries the term: the configured one if it is there, then
    /// the names a site's own search is likely to have used, and <see cref="DefaultTermParameter"/>
    /// when none of them is - so a request with no term at all still reports the name the page's
    /// own form will submit.
    /// </summary>
    private static string TermParameter(IQueryCollection? query, SearchProfile? profile)
    {
        if (query is null || query.Count == 0)
        {
            return DefaultTermParameter;
        }

        // The profile's Search Box tab can name the parameter, and what the box submits is what
        // this page has to read before anything it merely recognises.
        var configured = (profile?.Rules.SearchBox.QueryName ?? string.Empty).Trim();

        if (configured.Length > 0 && query.ContainsKey(configured))
        {
            return configured;
        }

        foreach (var candidate in TermParameters)
        {
            // Matched without case, and the key as the request spelled it is the one returned -
            // IQueryCollection is case-insensitive on lookup, but being exact costs nothing.
            var key = query.Keys.FirstOrDefault(
                k => k.Equals(candidate, StringComparison.OrdinalIgnoreCase));

            if (key is not null)
            {
                return key;
            }
        }

        return configured.Length > 0 ? configured : DefaultTermParameter;
    }

    private static string CurrentPath(HttpContext? context)
    {
        var path = context?.Request.Path.Value;

        return string.IsNullOrWhiteSpace(path)
            ? "/" + ImobisoftSearchConstants.Web.SearchPagePath
            : path;
    }
}
