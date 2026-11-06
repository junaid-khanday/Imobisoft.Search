using Imobisoft.Search.Configuration;
using Imobisoft.Search.Models;
using Imobisoft.Search.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
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
    private readonly ISearchPageModelBuilder _builder;
    private readonly ISiteLayoutLocator _layout;
    private readonly ISearchSettingsService _settings;
    private readonly ImobisoftSearchOptions _options;
    private readonly ILogger<ImobisoftSearchController> _logger;

    public ImobisoftSearchController(
        ISearchPageModelBuilder builder,
        ISiteLayoutLocator layout,
        ISearchSettingsService settings,
        IOptions<ImobisoftSearchOptions> options,
        ILogger<ImobisoftSearchController> logger)
    {
        _builder = builder;
        _layout = layout;
        _settings = settings;
        _options = options.Value;
        _logger = logger;
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

        // A site that has a page of its own for search has one search page, not two. Everything the
        // visitor asked for travels with them, so a term typed into the header box is answered on
        // the site's page rather than dropped on the way there. Found, not permanent: naming a
        // different page in the settings must not leave browsers redirecting to the old one.
        var sitePage = SitePage();

        if (sitePage is not null)
        {
            return Redirect(sitePage + Request.QueryString);
        }

        ImobisoftSearchPageViewModel model = await _builder.BuildAsync(q, page, cancellationToken);

        // This route is the package's own, so the page is on its own path rather than inside one of
        // the site's pages - its form posts back here.
        model.PagePath = "/" + ImobisoftSearchConstants.Web.SearchPagePath;

        // The site's layout where there is one this page can be rendered into, so search arrives
        // with the site's header and footer around it. Where there is not - which is any layout
        // that declares a view model of its own, because a layout shares its model with the page
        // inside it - the page writes its own document and the site renders search inside its own
        // pages instead, through ImobisoftSearchPageViewComponent.
        model.LayoutPath = _layout.Find();
        model.RenderDocumentShell = model.LayoutPath is null;

        return View("SearchPage", model);
    }

    /// <summary>
    /// The first site page search is rendered into, as a path to send a visitor to, or null when the
    /// site has none and this route is the search page.
    /// </summary>
    private string? SitePage()
    {
        try
        {
            var path = _settings.Get().SearchPage.SitePagePaths
                .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p))
                ?.Trim();

            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            // Only ever somewhere on this site: a path from the settings is not a place to send a
            // visitor off to, however it was typed.
            if (!path.StartsWith('/') || path.StartsWith("//"))
            {
                return null;
            }

            // Never to this route, which would be a redirect to the page doing the redirecting.
            // Said plainly in the log, because a site page given this URL is also shadowed by this
            // route - an attribute route wins over Umbraco's content route - so the page would
            // appear to have been ignored with nothing to say why.
            if (path.Trim('/').Equals(ImobisoftSearchConstants.Web.SearchPagePath, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Search → Settings names {Path} as the site page to render search into, but that "
                    + "is this package's own route and it is serving this request. A site page at "
                    + "that URL can never be reached. Give the page a URL of its own - /site-search, "
                    + "/find - and name that instead; this route will then send visitors to it.",
                    path);

                return null;
            }

            return path;
        }
        catch
        {
            // Settings live in the database. A failed read leaves this route serving the search
            // page itself, which is what it did before there was a setting.
            return null;
        }
    }
}
