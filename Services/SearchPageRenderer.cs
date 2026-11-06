using Imobisoft.Search.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Imobisoft.Search.Services;

/// <summary>
/// Renders the search page to markup outside of an action, so it can be placed into a page the site
/// is already rendering.
/// </summary>
public interface ISearchPageRenderer
{
    /// <summary>
    /// The search page as a block of markup - no layout, no document of its own - or null when it
    /// could not be rendered, which is never a reason to take the site's page down with it.
    /// </summary>
    Task<string?> RenderAsync(HttpContext context, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class SearchPageRenderer : ISearchPageRenderer
{
    /// <summary>
    /// The same file the package's own route renders and that Search → Templates edits, by path
    /// rather than by name so a site's own copy at this path wins and nothing else can.
    /// </summary>
    private const string ViewPath = "~/Views/ImobisoftSearch/SearchPage.cshtml";

    private readonly ICompositeViewEngine _views;
    private readonly ISearchPageModelBuilder _builder;
    private readonly ITempDataProvider _tempData;
    private readonly ILogger<SearchPageRenderer> _logger;

    public SearchPageRenderer(
        ICompositeViewEngine views,
        ISearchPageModelBuilder builder,
        ITempDataProvider tempData,
        ILogger<SearchPageRenderer> logger)
    {
        _views = views;
        _builder = builder;
        _tempData = tempData;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string?> RenderAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // isMainPage is false deliberately: this is a block inside a page, so the site's
            // _ViewStart - which exists to give the site's own pages the site's layout - must not
            // run and hand this one a layout it cannot be rendered into.
            ViewEngineResult found = _views.GetView(executingFilePath: null, viewPath: ViewPath, isMainPage: false);

            if (!found.Success)
            {
                _logger.LogWarning(
                    "The search page could not be rendered into this site's page because {View} was "
                    + "not found by the view engine.",
                    ViewPath);

                return null;
            }

            ImobisoftSearchPageViewModel model = await _builder.BuildAsync(
                cancellationToken: cancellationToken);

            // The site's page is already providing the layout and the document, which is the whole
            // point of rendering here rather than on the package's own route.
            model.LayoutPath = null;
            model.RenderDocumentShell = false;

            var actionContext = new ActionContext(
                context,
                context.GetRouteData() ?? new RouteData(),
                new ActionDescriptor());

            var viewData = new ViewDataDictionary<ImobisoftSearchPageViewModel>(
                new EmptyModelMetadataProvider(),
                new ModelStateDictionary())
            {
                Model = model,
            };

            await using var writer = new StringWriter();

            var viewContext = new ViewContext(
                actionContext,
                found.View,
                viewData,
                new TempDataDictionary(context, _tempData),
                writer,
                new HtmlHelperOptions());

            await found.View.RenderAsync(viewContext);

            return writer.ToString();
        }
        catch (OperationCanceledException)
        {
            // The visitor left. Nothing is waiting for this.
            return null;
        }
        catch (Exception ex)
        {
            // A page of the site's own must not 500 because the block being placed into it failed.
            _logger.LogError(ex, "The search page could not be rendered into this site's page.");

            return null;
        }
    }
}
