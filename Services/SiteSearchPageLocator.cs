using Examine;
using Examine.Search;
using Imobisoft.Search.Configuration;
using Imobisoft.Search.Models;
using Imobisoft.Search.Services.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Extensions;

namespace Imobisoft.Search.Services;

/// <summary>How the package came to be answering a page of the site's.</summary>
public enum SiteSearchPageSource
{
    /// <summary>Not a search page of the site's; nothing to do.</summary>
    None,

    /// <summary>Named in Search → Settings. Someone placed search here deliberately.</summary>
    Configured,

    /// <summary>
    /// Recognised as the site's own search page without anyone saying so. The package is taking it
    /// over, which is why the site's own search markup on it is stood down.
    /// </summary>
    Discovered,
}

/// <summary>
/// Finds the search page a site already had, so installing the package is enough to take it over.
/// <para>
/// A site that built search before the package has a page for it - an Umbraco node, or a route of
/// its own - and the visitor's search link points there. Left alone that leaves two search pages on
/// one site: the site's, which the links go to, and the package's. This recognises the site's and
/// the package answers it instead, with no path typed into Search → Settings and no change to the
/// site's content, template or links.
/// </para>
/// <para>
/// Turn it off with <c>Imobisoft:Search:TakeOverSiteSearchPage</c>, or in Search → Settings.
/// </para>
/// </summary>
public interface ISiteSearchPageLocator
{
    /// <summary>
    /// Every path on this site that search is answered on - configured first, then discovered. Used
    /// where a list of paths is needed up front rather than a decision about one request: the
    /// header box, which must not open a second box on a search page, and the package's own route,
    /// which sends visitors to the site's page where there is one.
    /// </summary>
    IReadOnlyList<string> Find();

    /// <summary>
    /// Whether this request is a search page of the site's, and how it was decided. The answer for
    /// a single request, which is the reliable one: a page is recognised from the document type
    /// Umbraco actually routed, not from a list of paths assembled earlier.
    /// </summary>
    SiteSearchPageSource Match(HttpContext context);
}

/// <inheritdoc />
public sealed class SiteSearchPageLocator : ISiteSearchPageLocator
{
    /// <summary>
    /// URL segments that are a search page whatever the document type is called. Matched whole, so
    /// <c>/search</c> is a search page and <c>/search-tips</c> is an article about one.
    /// </summary>
    private static readonly string[] Segments =
    {
        "search",
        "searchresults",
        "search-results",
        "site-search",
        "sitesearch",
        "search-page",
        "searchpage",
        "find",
    };

    /// <summary>
    /// How a document type alias has to end before an alias containing "search" is taken to be a
    /// page rather than a settings node or an element type. Keeps <c>searchPage</c>,
    /// <c>siteSearch</c> and <c>searchResults</c>; leaves <c>searchBoxSettings</c> alone.
    /// </summary>
    private static readonly string[] AliasEndings = { "search", "page", "result", "results" };

    /// <summary>
    /// How many matches are read back. A site has one search page, or one per culture; a number
    /// far past that means the recognition is matching something it should not, and reading more
    /// of them would not help.
    /// </summary>
    private const int MaxPages = 16;

    /// <summary>
    /// How many matches are read before confirming them. More than is kept, because the query is a
    /// narrowing and some of what it returns is discarded.
    /// </summary>
    private const int FetchSize = 50;

    /// <summary>
    /// Long enough that a page view is not paying for a tree walk, short enough that adding a
    /// search page to the site takes effect without a restart.
    /// </summary>
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private const string CacheKey = "Imobisoft.Search.SiteSearchPages";

    private readonly ISearchSettingsService _settings;
    private readonly IExamineManager _examine;
    private readonly IContentTypeService _contentTypes;
    private readonly IUmbracoContextAccessor _context;
    private readonly IPublishedUrlProvider _urls;
    private readonly AppCaches _caches;
    private readonly ImobisoftSearchOptions _options;
    private readonly ILogger<SiteSearchPageLocator> _logger;

    public SiteSearchPageLocator(
        ISearchSettingsService settings,
        IExamineManager examine,
        IContentTypeService contentTypes,
        IUmbracoContextAccessor context,
        IPublishedUrlProvider urls,
        AppCaches caches,
        IOptions<ImobisoftSearchOptions> options,
        ILogger<SiteSearchPageLocator> logger)
    {
        _settings = settings;
        _examine = examine;
        _contentTypes = contentTypes;
        _context = context;
        _urls = urls;
        _caches = caches;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Find()
    {
        var paths = new List<string>();

        SearchPageSettings page = Settings();

        // Configured first. The list is also what the header box uses to decide where a box with
        // nowhere else to send a visitor sends them, and a path someone typed is a better answer
        // than one this worked out.
        foreach (var configured in Configured(page))
        {
            Add(paths, configured);
        }

        if (TakeoverWanted(page))
        {
            foreach (var discovered in Discovered())
            {
                Add(paths, discovered);
            }
        }

        return paths;
    }

    /// <inheritdoc />
    public SiteSearchPageSource Match(HttpContext context)
    {
        PathString path = context.Request.Path;

        SearchPageSettings page = Settings();

        if (Contains(Configured(page), path))
        {
            return SiteSearchPageSource.Configured;
        }

        if (!TakeoverWanted(page))
        {
            return SiteSearchPageSource.None;
        }

        // The backoffice is not the site, and its own search has nothing to do with this one.
        if (path.StartsWithSegments("/umbraco", StringComparison.OrdinalIgnoreCase))
        {
            return SiteSearchPageSource.None;
        }

        // The package's own search page is not a page of the site's to be taken over.
        if (path.StartsWithSegments(
                "/" + ImobisoftSearchConstants.Web.SearchPagePath,
                StringComparison.OrdinalIgnoreCase))
        {
            return SiteSearchPageSource.None;
        }

        // The document type Umbraco routed, which is what a site's search page is actually
        // identified by: the URL is whatever an editor named the node, and often translated.
        if (RoutedAsSearchPage())
        {
            return SiteSearchPageSource.Discovered;
        }

        // Then the URL, which is all there is to go on when the site's search page is a route of
        // its own rather than a content node.
        return IsSearchSegment(LastSegment(path))
            ? SiteSearchPageSource.Discovered
            : SiteSearchPageSource.None;
    }

    /// <summary>
    /// Whether the content Umbraco routed for this request is a search page of the site's. False
    /// when there is no routed content, which is the case for an MVC route of the site's own.
    /// </summary>
    private bool RoutedAsSearchPage()
    {
        try
        {
            if (!_context.TryGetUmbracoContext(out IUmbracoContext? umbraco) || umbraco is null)
            {
                return false;
            }

            IPublishedContent? content = umbraco.PublishedRequest?.PublishedContent;

            return content is not null && IsSearchPage(content);
        }
        catch (Exception ex)
        {
            // Reading the routed request must never take a page of the site's down with it. Not
            // recognising the page leaves the site's own search in place, which is what it had.
            _logger.LogDebug(ex, "Could not read the routed content while looking for the site's search page.");

            return false;
        }
    }

    /// <summary>
    /// The site's search pages as paths, found in the published content index. Cached: this is only
    /// needed where a list is wanted up front, and the answer is the same for every request.
    /// <para>
    /// Read from the index rather than by walking the content tree because that is what this
    /// package already does with the site's content, and because it answers in one query however
    /// deep the tree is - a search page is as likely to be filed under a section as at the root.
    /// </para>
    /// </summary>
    private IReadOnlyList<string> Discovered()
        => _caches.RuntimeCache.GetCacheItem(CacheKey, Search, CacheDuration) ?? new List<string>();

    private List<string> Search()
    {
        var found = new List<string>();

        try
        {
            var query = Query();

            if (query.Length == 0)
            {
                return found;
            }

            if (!_examine.TryGetIndex(
                    Umbraco.Cms.Core.Constants.UmbracoIndexes.ExternalIndexName,
                    out IIndex? index))
            {
                _logger.LogDebug(
                    "The published content index is not registered, so the site's own search page "
                    + "could not be looked for.");

                return found;
            }

            ISearchResults results = index.Searcher
                .CreateQuery()
                .NativeQuery(query)
                .Execute(QueryOptions.SkipTake(0, FetchSize));

            foreach (ISearchResult result in results)
            {
                if (found.Count >= MaxPages)
                {
                    break;
                }

                if (Confirmed(result))
                {
                    Add(found, Url(result));
                }
            }

            if (found.Count > 0)
            {
                _logger.LogInformation(
                    "Imobisoft.Search is answering the search page this site already had: {Paths}. "
                    + "Nothing in the site had to change. Set Imobisoft:Search:TakeOverSiteSearchPage "
                    + "to false, or turn off taking over the site's search page in Search → "
                    + "Settings, to leave it alone.",
                    string.Join(", ", found));
            }
        }
        catch (Exception ex)
        {
            // An index not yet built, an empty site, or a failed read all mean the same thing here:
            // nothing discovered, and the package's own page is still there to search on. The
            // per-request recognition in Match does not depend on this, so a site's search page is
            // still taken over when a visitor arrives on it.
            _logger.LogDebug(ex, "Could not look for the site's own search page in the content index.");
        }

        return found;
    }

    /// <summary>
    /// The query that narrows the index down to the site's likely search pages: a published content
    /// node whose URL segment reads like a search page's, or whose document type does. Empty when
    /// there is nothing to ask for.
    /// <para>
    /// Deliberately a narrowing rather than the decision. An index field is analysed, so
    /// <c>urlName:search</c> also comes back with a page at <c>/search-tips</c> - the word is in
    /// there. <see cref="Confirmed"/> is what decides, on the values the index returns, by exactly
    /// the rules that recognise a page at request time.
    /// </para>
    /// </summary>
    private string Query()
    {
        var alternatives = new[]
            {
                LuceneSyntax.FieldIn(ImobisoftSearchConstants.IndexFields.UrlName, Segments),
                LuceneSyntax.FieldIn(ImobisoftSearchConstants.IndexFields.NodeTypeAlias, SearchDocumentTypes()),
            }
            .Where(c => c.Length > 0)
            .ToList();

        if (alternatives.Count == 0)
        {
            return string.Empty;
        }

        // Written out rather than built with LuceneSyntax.Join, because the grouping here is load
        // bearing: "+a:x +(b OR c)" and "+a:x +b OR c" are different queries.
        return "+" + LuceneSyntax.EscapeFieldName(ImobisoftSearchConstants.IndexFields.IndexType)
               + ":" + ImobisoftSearchConstants.IndexTypes.Content
               + " +(" + string.Join(" OR ", alternatives) + ")";
    }

    /// <summary>
    /// Whether an indexed node really is the site's search page, by the same rules that recognise
    /// one at request time - so a page the analysed query merely brought back does not become a
    /// page visitors are sent to.
    /// </summary>
    private static bool Confirmed(ISearchResult result)
        => IsSearchSegment(Value(result, ImobisoftSearchConstants.IndexFields.UrlName))
           || IsSearchAlias(Value(result, ImobisoftSearchConstants.IndexFields.NodeTypeAlias));

    private static string? Value(ISearchResult result, string field)
        => result.Values.TryGetValue(field, out var value) ? value : null;

    /// <summary>
    /// The site's own document types that read like a search page. Taken from the site's types
    /// rather than guessed, so the aliases asked for are ones that exist.
    /// </summary>
    private IEnumerable<string> SearchDocumentTypes()
    {
        try
        {
            return _contentTypes.GetAll()
                .Where(t => !t.IsElement && IsSearchAlias(t.Alias))
                .Select(t => t.Alias)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read the site's document types while looking for its search page.");

            return Array.Empty<string>();
        }
    }

    /// <summary>The indexed node's relative URL, or empty where it has none that can be linked to.</summary>
    private string Url(ISearchResult result)
    {
        if (!int.TryParse(result.Id, out var id))
        {
            return string.Empty;
        }

        try
        {
            var url = _urls.GetUrl(id, UrlMode.Relative);

            // Umbraco answers "#" for a node with no routable URL - unpublished, or outside a
            // domain - which is not a path to send anyone to.
            return string.IsNullOrWhiteSpace(url) || url == "#" ? string.Empty : url;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not resolve the URL of node {Node} while looking for the site's search page.", id);

            return string.Empty;
        }
    }

    /// <summary>
    /// Whether this node is the site's search page: its document type says so, or its URL segment
    /// does. Never the package's own route, which is not the site's page to take over.
    /// </summary>
    private static bool IsSearchPage(IPublishedContent node)
        => IsSearchSegment(node.UrlSegment) || IsSearchAlias(node.ContentType?.Alias);

    /// <summary>
    /// Whether a document type alias reads like a search page's. An alias merely mentioning search
    /// is not enough: a site has types for search settings and search box configuration too, and
    /// neither is the page that shows results.
    /// </summary>
    private static bool IsSearchAlias(string? contentTypeAlias)
    {
        var alias = Simplify(contentTypeAlias);

        if (!alias.Contains("search", StringComparison.Ordinal))
        {
            return false;
        }

        // "search" on its own is a page. Otherwise it has to read like one.
        return alias == "search" || AliasEndings.Any(e => alias.EndsWith(e, StringComparison.Ordinal));
    }

    /// <summary>
    /// Whether a URL segment is a search page's. Matched whole and without case, so a page whose
    /// URL merely mentions searching is not taken for the search page itself.
    /// </summary>
    private static bool IsSearchSegment(string? segment)
    {
        var value = (segment ?? string.Empty).Trim().Trim('/');

        if (value.Length == 0)
        {
            return false;
        }

        // Not the package's own page: it is already the search page, and placing search into it
        // would put a second copy inside the first.
        if (value.Equals(ImobisoftSearchConstants.Web.SearchPagePath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return Segments.Contains(value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>An alias reduced to what two of them can be compared by: lower case and letters only.</summary>
    private static string Simplify(string? alias)
        => new string((alias ?? string.Empty).Where(char.IsLetter).ToArray()).ToLowerInvariant();

    private static string LastSegment(PathString path)
    {
        var value = (path.Value ?? string.Empty).Trim('/');

        if (value.Length == 0)
        {
            return string.Empty;
        }

        var cut = value.LastIndexOf('/');

        return cut < 0 ? value : value[(cut + 1)..];
    }

    private bool TakeoverWanted(SearchPageSettings page)
    {
        if (!_options.Enabled || !_options.TakeOverSiteSearchPage)
        {
            return false;
        }

        return page.TakeOverSitePage;
    }

    /// <summary>
    /// The paths named in the settings. The settings are one JSON document in the database, so an
    /// older one - written before this list existed - can leave it absent rather than empty.
    /// </summary>
    private static IEnumerable<string> Configured(SearchPageSettings page)
        => page.SitePagePaths ?? Enumerable.Empty<string>();

    private SearchPageSettings Settings()
    {
        try
        {
            return _settings.Get().SearchPage;
        }
        catch
        {
            // Settings live in the database. A failed read must leave a page of the site's
            // rendering, so this falls back to the defaults rather than throwing into a view.
            return new SearchPageSettings();
        }
    }

    private static bool Contains(IEnumerable<string> paths, PathString path)
    {
        var current = SearchPagePath.Normalise(path.Value);

        return current.Length > 0
               && paths.Any(p => SearchPagePath.Normalise(p) == current);
    }

    /// <summary>Adds a path once, normalised, skipping what is empty or already there.</summary>
    private static void Add(List<string> paths, string? path)
    {
        var value = SearchPagePath.Normalise(path);

        if (value.Length == 0 || paths.Contains(value, StringComparer.Ordinal))
        {
            return;
        }

        paths.Add(value);
    }
}

/// <summary>
/// Reducing a page path to what two of them can be compared by. Shared, so the path a site typed
/// into the settings, the path a node resolves to and the path on the request are all compared the
/// same way - a trailing slash or a capital letter must not decide whether search appears.
/// </summary>
public static class SearchPagePath
{
    /// <summary>
    /// The path as lower case, with a leading slash, no trailing slash and no query string. Empty
    /// for anything that is not a path on this site.
    /// </summary>
    public static string Normalise(string? path)
    {
        var value = (path ?? string.Empty).Trim();

        var cut = value.IndexOfAny(new[] { '?', '#' });

        if (cut >= 0)
        {
            value = value[..cut];
        }

        value = value.Trim();

        if (value.Length == 0)
        {
            return string.Empty;
        }

        if (!value.StartsWith('/'))
        {
            value = "/" + value;
        }

        // "//host/path" is somewhere else entirely, however it was typed.
        if (value.StartsWith("//", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        if (value.Length > 1 && value.EndsWith('/'))
        {
            value = value.TrimEnd('/');
        }

        return value.ToLowerInvariant();
    }
}
