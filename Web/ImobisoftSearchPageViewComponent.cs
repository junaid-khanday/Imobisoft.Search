using Imobisoft.Search.Services;
using Microsoft.AspNetCore.Mvc;

namespace Imobisoft.Search.Web;

/// <summary>
/// The whole search page, renderable from inside one of the site's own pages.
/// <para>
/// This is how search gets the site's header, footer and navigation around it on a site whose layout
/// declares a view model of its own. A layout shares its model with the page inside it, so the
/// package's own route cannot render into such a layout - it would hand the layout a model it cannot
/// bind. A view component has no such problem: it brings its own model and does not care what the
/// surrounding page's model is.
/// </para>
/// <para>
/// Add a page to the site - any document type, with any template - and render this from the
/// template:
/// </para>
/// <code>
/// @await Component.InvokeAsync("ImobisoftSearchPage")
/// </code>
/// <para>
/// That is the whole of it. The term, paging, filters and sort are read from the query string, the
/// search runs against the configured profile, and the page posts back to whatever URL it is on, so
/// searching stays on the site's own page. Everything else - the result cards, the filters, the AI
/// text tools, the styling - comes from the same <c>SearchPage.cshtml</c> that the package's own
/// route renders and that Search → Search Box → Templates edits, so there is still only one file to
/// design.
/// </para>
/// </summary>
public sealed class ImobisoftSearchPageViewComponent : ViewComponent
{
    /// <summary>
    /// Rendered by path rather than by the usual component view convention, so the file a site
    /// edits is the file this renders. A copy at
    /// <c>Views/Shared/Components/ImobisoftSearchPage/Default.cshtml</c> in the site would otherwise
    /// silently take over, leaving two files where the backoffice only edits one.
    /// </summary>
    private const string ViewPath = "~/Views/ImobisoftSearch/SearchPage.cshtml";

    private readonly ISearchPageModelBuilder _builder;

    public ImobisoftSearchPageViewComponent(ISearchPageModelBuilder builder) => _builder = builder;

    /// <param name="term">
    /// What to search for. Left unset it is read from <c>q</c> on the query string, which is what the
    /// page's own search box submits.
    /// </param>
    /// <param name="page">1-based page number. Left unset it is read from the query string.</param>
    public async Task<IViewComponentResult> InvokeAsync(string? term = null, int page = 0)
    {
        ImobisoftSearchPageViewModel model = await _builder.BuildAsync(
            term,
            page,
            HttpContext?.RequestAborted ?? CancellationToken.None);

        // No layout and no document of its own: the page this is being rendered into is already
        // providing both, and a second <html> nested inside the site's own would be invalid.
        model.LayoutPath = null;
        model.RenderDocumentShell = false;

        // Said on the request so the injector stands aside: a site that renders search itself has
        // put it where it wants it, and must not then be given a second copy because this page's
        // path is also listed in Search → Settings.
        if (HttpContext is not null)
        {
            HttpContext.Items[ImobisoftSearchConstants.Web.PageRenderedKey] = true;
        }

        return View(ViewPath, model);
    }
}
