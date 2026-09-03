using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;

namespace Imobisoft.Search.Api;

/// <summary>
/// Renders one of the package's Razor partials to an HTML string, outside the normal view pipeline.
/// <para>
/// This is what lets the backoffice test panel show the real thing. The panel builds its result
/// list in JavaScript, so before this it could only ever approximate the site - a theme that
/// changed the markup would preview as something the visitor would never see. Rendering the very
/// partial the front end renders means the preview cannot drift from the page.
/// </para>
/// </summary>
public interface ISearchRenderService
{
    /// <summary>
    /// Renders <paramref name="viewPath"/> with <paramref name="model"/> and returns the markup.
    /// </summary>
    /// <exception cref="InvalidOperationException">The view could not be found.</exception>
    Task<string> RenderAsync(HttpContext httpContext, string viewPath, object model);
}

/// <inheritdoc />
public sealed class SearchRenderService : ISearchRenderService
{
    private readonly IRazorViewEngine _viewEngine;
    private readonly ITempDataProvider _tempDataProvider;
    private readonly IModelMetadataProvider _metadataProvider;

    public SearchRenderService(
        IRazorViewEngine viewEngine,
        ITempDataProvider tempDataProvider,
        IModelMetadataProvider metadataProvider)
    {
        _viewEngine = viewEngine;
        _tempDataProvider = tempDataProvider;
        _metadataProvider = metadataProvider;
    }

    /// <inheritdoc />
    public async Task<string> RenderAsync(HttpContext httpContext, string viewPath, object model)
    {
        // GetView rather than FindView: the path is absolute, and it resolves both views compiled
        // into an assembly and .cshtml files a site dropped in - which is exactly the set a theme
        // can be built from.
        ViewEngineResult result = _viewEngine.GetView(executingFilePath: null, viewPath: viewPath, isMainPage: false);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"Could not find the view '{viewPath}'. Searched: {string.Join(", ", result.SearchedLocations)}");
        }

        var actionContext = new ActionContext(
            httpContext,
            httpContext.GetRouteData() ?? new RouteData(),
            new ActionDescriptor());

        await using var writer = new StringWriter();

        var viewData = new ViewDataDictionary(_metadataProvider, new ModelStateDictionary())
        {
            Model = model,
        };

        var viewContext = new ViewContext(
            actionContext,
            result.View,
            viewData,
            new TempDataDictionary(httpContext, _tempDataProvider),
            writer,
            new HtmlHelperOptions());

        await result.View.RenderAsync(viewContext);

        return writer.ToString();
    }
}
