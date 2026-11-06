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
/// </summary>
public sealed class SearchPageInjectionTagHelperComponent : TagHelperComponent
{
    /// <summary>
    /// Where search is moved to once the page has loaded: the site's main content area, by the
    /// names sites actually give it. First match wins.
    /// </summary>
    private const string DefaultContainer = "main, [role=\"main\"], #main, #content, .main-content, .page-content, article";

    private const string HostElementId = "imobisoft-search-page";

    private const string ScriptPath = "/App_Plugins/" + ImobisoftSearchConstants.PackageName + "/searchpage.js";

    private readonly ImobisoftSearchOptions _options;
    private readonly ISearchSettingsService _settings;
    private readonly ISearchPageRenderer _renderer;
    private readonly IHttpContextAccessor _http;

    public SearchPageInjectionTagHelperComponent(
        IOptions<ImobisoftSearchOptions> options,
        ISearchSettingsService settings,
        ISearchPageRenderer renderer,
        IHttpContextAccessor http)
    {
        _options = options.Value;
        _settings = settings;
        _renderer = renderer;
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

        if (page is null || !Matches(http.Request.Path, page.SitePagePaths))
        {
            return;
        }

        var markup = await _renderer.RenderAsync(http, http.RequestAborted);

        if (string.IsNullOrEmpty(markup))
        {
            return;
        }

        http.Items[ImobisoftSearchConstants.Web.PageRenderedKey] = true;

        output.PostContent.AppendHtml(Wrap(markup, page.SitePageContainer));
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
    /// Whether this is one of the pages search was asked to be placed into. Compared on the path
    /// alone and without case or a trailing slash, so the way the link was written does not decide
    /// whether search appears.
    /// </summary>
    private static bool Matches(PathString path, IList<string> configured)
    {
        if (configured.Count == 0)
        {
            return false;
        }

        var current = Normalise(path.Value);

        if (current.Length == 0)
        {
            return false;
        }

        foreach (var candidate in configured)
        {
            if (Normalise(candidate) == current)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A path reduced to what two of them can be compared by: lower case, leading slash, no
    /// trailing slash and no query string.
    /// </summary>
    private static string Normalise(string? path)
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

        if (value.Length > 1 && value.EndsWith('/'))
        {
            value = value.TrimEnd('/');
        }

        return value.ToLowerInvariant();
    }

    /// <summary>
    /// The rendered page, hidden until it has been moved into the site's content area, so it is
    /// never seen at the foot of the page first. A site with scripting off gets it where it is
    /// rather than not at all.
    /// </summary>
    private static string Wrap(string markup, string? container)
    {
        HtmlEncoder encoder = HtmlEncoder.Default;

        var target = string.IsNullOrWhiteSpace(container) ? DefaultContainer : container.Trim();

        var html = new StringBuilder();

        html.Append("<div id=\"").Append(HostElementId).Append('"')
            .Append(" data-container=\"").Append(encoder.Encode(target)).Append('"')
            .Append(" hidden>")
            .Append(markup)
            .Append("</div>");

        html.Append("<noscript><style>#").Append(HostElementId)
            .Append("{display:block !important}</style></noscript>");

        html.Append("<script src=\"").Append(ScriptPath).Append("\" defer></script>");

        return html.ToString();
    }
}
