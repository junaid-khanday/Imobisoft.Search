using System.Text;
using System.Text.Encodings.Web;
using Imobisoft.Search.Configuration;
using Imobisoft.Search.Models;
using Imobisoft.Search.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Razor.TagHelpers;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;

namespace Imobisoft.Search.Web;

/// <summary>
/// Renders the whole search page inside one of the site's own pages, without the site writing a
/// line of code.
/// <para>
/// This is what gets search the site's header, footer and navigation. The package's own route
/// cannot: a layout shares its model with the page inside it, and a site layout declared as
/// <c>@inherits UmbracoViewPage&lt;SomePageViewModel&gt;</c> needs a model that only the site's own
/// controllers can build - from the routed Umbraco page, with its SEO data, its settings and its
/// navigation. Nothing in this package can manufacture one.
/// </para>
/// <para>
/// So the site renders its own page exactly as it always does - its document type, its controller,
/// its template, its layout - and the search page is placed into it as a block. A tag helper
/// component is how a library adds markup to a view it does not own, which is the same mechanism
/// the header search box already arrives by.
/// </para>
/// <para>
/// To use it: create a page in Umbraco with whatever document type the site uses for an ordinary
/// content page, then put its path in Search → Settings → Search Page. A site that wants to place
/// search exactly rather than have it placed can render
/// <see cref="ImobisoftSearchPageViewComponent"/> from its own template instead, and this stands
/// aside when it has.
/// </para>
/// <para>
/// A site that already had a search page before the package needs none of that: the page is
/// recognised by <see cref="ISiteSearchPageLocator"/> and taken over where it stands, so the links
/// already pointing at it arrive at the package's search. On a page taken over this way the site's
/// own search form and results are stood down too - that is what keeps one search page from
/// showing two searches.
/// </para>
/// </summary>
public sealed class SearchPageInjectionTagHelperComponent : TagHelperComponent
{
    /// <summary>
    /// Where search is moved to once the page has loaded: the site's main content area, by the
    /// names sites actually give it. First match wins.
    /// </summary>
    private const string DefaultContainer = "main, [role=\"main\"], #main, #content, .main-content, .page-content, article";

    /// <summary>
    /// The site's own search markup, by the names sites actually give it. Only ever looked for
    /// inside the page's content area, and only on a page the package recognised and took over.
    /// </summary>
    private const string DefaultReplaced =
        "form[role=\"search\"], form[action*=\"search\" i], .search-form, #search-form, "
        + "[class*=\"search-result\"], [id*=\"search-result\"], [class*=\"searchresult\"], "
        + "[id*=\"searchresult\"], .no-results, .search-pagination";

    private const string HostElementId = "imobisoft-search-page";

    private const string ScriptPath = "/App_Plugins/" + ImobisoftSearchConstants.PackageName + "/searchpage.js";

    private readonly ImobisoftSearchOptions _options;
    private readonly ISearchSettingsService _settings;
    private readonly ISearchPageRenderer _renderer;
    private readonly ISiteSearchPageLocator _locator;
    private readonly IHttpContextAccessor _http;

    public SearchPageInjectionTagHelperComponent(
        IOptions<ImobisoftSearchOptions> options,
        ISearchSettingsService settings,
        ISearchPageRenderer renderer,
        ISiteSearchPageLocator locator,
        IHttpContextAccessor http)
    {
        _options = options.Value;
        _settings = settings;
        _renderer = renderer;
        _locator = locator;
        _http = http;
    }

    /// <summary>
    /// After the header search box, so the script that places this runs after the script that
    /// drives the box has been written.
    /// </summary>
    public override int Order => 1100;

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (!_options.Enabled)
        {
            return;
        }

        // The end of <body>: by the time this runs the site's page has been rendered, so a page
        // that renders the view component itself has already said so and is left alone.
        if (!string.Equals(context.TagName, "body", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        HttpContext? http = _http.HttpContext;

        if (http is null || !Wanted(http))
        {
            return;
        }

        SearchPageSettings? page = Settings();

        if (page is null)
        {
            return;
        }

        // Named in the settings, or recognised as the search page this site already had. The
        // difference matters once: a page someone named is left as they built it, while a page the
        // package recognised is one it is taking over, so the site's own search on it stands down.
        SiteSearchPageSource source = _locator.Match(http);

        if (source == SiteSearchPageSource.None)
        {
            return;
        }

        var markup = await _renderer.RenderAsync(http, http.RequestAborted);

        if (string.IsNullOrEmpty(markup))
        {
            return;
        }

        http.Items[ImobisoftSearchConstants.Web.PageRenderedKey] = true;

        output.PostContent.AppendHtml(Wrap(markup, page, Replaced(page, source)));
    }

    /// <summary>
    /// The site's own search markup to stand down on this page, or null to leave the page's
    /// content as it is.
    /// </summary>
    private static string? Replaced(SearchPageSettings page, SiteSearchPageSource source)
    {
        // A page whose path was typed into the settings is not being taken over - search was
        // placed into it on purpose, and the rest of that page is the site's to keep.
        if (source != SiteSearchPageSource.Discovered || !page.ReplaceSitePageSearch)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(page.SitePageSearchSelector)
            ? DefaultReplaced
            : page.SitePageSearchSelector.Trim();
    }

    /// <summary>
    /// Whether this request is one to place search into at all: a visitor's own GET of a page of the
    /// site, and not a page that has already rendered search itself.
    /// </summary>
    private static bool Wanted(HttpContext http)
    {
        if (!HttpMethods.IsGet(http.Request.Method))
        {
            return false;
        }

        // The backoffice is not the site, and its own search has nothing to do with this one.
        if (http.Request.Path.StartsWithSegments("/umbraco", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Nor the package's own route, which is the search page already. Listing that path in the
        // settings would otherwise put a second copy of the page inside the first.
        if (http.Request.Path.StartsWithSegments(
                "/" + ImobisoftSearchConstants.Web.SearchPagePath,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !http.Items.ContainsKey(ImobisoftSearchConstants.Web.PageRenderedKey);
    }

    private SearchPageSettings? Settings()
    {
        try
        {
            return _settings.Get().SearchPage;
        }
        catch
        {
            // Settings live in the database. A page of the site's must still render if that read
            // fails, so search simply does not appear rather than taking the request with it.
            return null;
        }
    }

    /// <summary>
    /// The rendered page, hidden until it has been moved into the site's content area, so it is
    /// never seen at the foot of the page first. A site with scripting off gets it where it is
    /// rather than not at all.
    /// </summary>
    private static string Wrap(string markup, SearchPageSettings page, string? replaced)
    {
        HtmlEncoder encoder = HtmlEncoder.Default;

        var target = string.IsNullOrWhiteSpace(page.SitePageContainer)
            ? DefaultContainer
            : page.SitePageContainer.Trim();

        var html = new StringBuilder();

        html.Append("<div id=\"").Append(HostElementId).Append('"')
            .Append(" data-container=\"").Append(encoder.Encode(target)).Append('"');

        // Only written on a page the package took over, so the script has nothing to stand down on
        // a page that merely has search placed into it.
        if (!string.IsNullOrWhiteSpace(replaced))
        {
            html.Append(" data-replace=\"").Append(encoder.Encode(replaced)).Append('"');
        }

        html.Append(" hidden>")
            .Append(markup)
            .Append("</div>");

        html.Append("<noscript><style>#").Append(HostElementId)
            .Append("{display:block !important}</style></noscript>");

        html.Append("<script src=\"").Append(ScriptPath).Append("\" defer></script>");

        return html.ToString();
    }
}
