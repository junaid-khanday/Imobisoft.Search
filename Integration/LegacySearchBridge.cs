using System.Collections;
using System.Reflection;
using Imobisoft.Search.Models;
using Imobisoft.Search.Services;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;

namespace Imobisoft.Search.Integration;

/// <summary>
/// Lets the package answer a site's own pre-existing search interface, without the package knowing
/// that interface exists.
/// <para>
/// Umbraco sites commonly ship their own <c>ISearchService</c> with a search page built against it.
/// Rewriting that page for every site defeats the point of a package, and this package cannot
/// reference a consumer's types to implement their interface directly. So the shape is discovered by
/// reflection at start-up instead, and only ever used when every piece of it checks out.
/// </para>
/// <para>
/// Nothing here guesses. <see cref="TryCreate"/> either finds every member it needs and returns a
/// working bridge, or returns null with the reason, and the site keeps the search it already had.
/// A half-understood interface is never proxied.
/// </para>
/// </summary>
internal sealed class LegacySearchBridge
{
    /// <summary>Property names the bridge expects on the site's search parameters type.</summary>
    private const string QueryProperty = "Query";
    private const string PageNumberProperty = "PageNumber";
    private const string ItemsPerPageProperty = "ItemsPerPage";
    private const string CultureProperty = "Culture";

    private readonly MethodInfo _searchMethod;
    private readonly Type _itemType;
    private readonly Type _itemSourceType;
    private readonly ConstructorInfo _resultsConstructor;
    private readonly ConstructorInfo _itemConstructor;
    private readonly PropertyInfo _queryProperty;
    private readonly PropertyInfo _pageNumberProperty;
    private readonly PropertyInfo _itemsPerPageProperty;
    private readonly PropertyInfo? _cultureProperty;

    private LegacySearchBridge(
        MethodInfo searchMethod,
        Type itemType,
        Type itemSourceType,
        ConstructorInfo resultsConstructor,
        ConstructorInfo itemConstructor,
        PropertyInfo queryProperty,
        PropertyInfo pageNumberProperty,
        PropertyInfo itemsPerPageProperty,
        PropertyInfo? cultureProperty)
    {
        _searchMethod = searchMethod;
        _itemType = itemType;
        _itemSourceType = itemSourceType;
        _resultsConstructor = resultsConstructor;
        _itemConstructor = itemConstructor;
        _queryProperty = queryProperty;
        _pageNumberProperty = pageNumberProperty;
        _itemsPerPageProperty = itemsPerPageProperty;
        _cultureProperty = cultureProperty;
    }

    /// <summary>The method on the site's interface this bridge answers.</summary>
    public MethodInfo SearchMethod => _searchMethod;

    /// <summary>
    /// Works out whether an interface can be answered by the package, and returns a bridge if so.
    /// </summary>
    /// <param name="serviceType">The site's search interface.</param>
    /// <param name="reason">Why it could not be bridged, when it could not.</param>
    public static LegacySearchBridge? TryCreate(Type serviceType, out string reason)
    {
        reason = string.Empty;

        MethodInfo[] methods = serviceType.GetMethods();

        // A single method taking one parameter and returning a collection of results.
        MethodInfo? search = methods.FirstOrDefault(m =>
            m.GetParameters().Length == 1
            && m.ReturnType != typeof(void)
            && typeof(IEnumerable).IsAssignableFrom(m.ReturnType));

        if (search is null)
        {
            reason = "no method taking one argument and returning a collection";
            return null;
        }

        Type parametersType = search.GetParameters()[0].ParameterType;
        Type resultsType = search.ReturnType;

        Type? itemType = ResolveItemType(resultsType);

        if (itemType is null)
        {
            reason = $"could not work out what {resultsType.Name} is a collection of";
            return null;
        }

        // The results type is expected to be built from the items, a total, and the parameters -
        // which is how paging information is carried back.
        ConstructorInfo? resultsConstructor = resultsType.GetConstructor(new[]
        {
            typeof(IEnumerable<>).MakeGenericType(itemType),
            typeof(long),
            parametersType,
        });

        if (resultsConstructor is null)
        {
            reason = $"{resultsType.Name} has no (items, total, parameters) constructor";
            return null;
        }

        // Each item is expected to wrap a published content item plus the term that was searched.
        ConstructorInfo? itemConstructor = itemType.GetConstructors().FirstOrDefault(c =>
        {
            ParameterInfo[] parameters = c.GetParameters();

            return parameters.Length == 2
                   && parameters[1].ParameterType == typeof(string)
                   && typeof(IPublishedContent).IsAssignableFrom(parameters[0].ParameterType);
        });

        if (itemConstructor is null)
        {
            reason = $"{itemType.Name} has no (publishedContent, term) constructor";
            return null;
        }

        PropertyInfo? query = parametersType.GetProperty(QueryProperty);
        PropertyInfo? pageNumber = parametersType.GetProperty(PageNumberProperty);
        PropertyInfo? itemsPerPage = parametersType.GetProperty(ItemsPerPageProperty);

        if (query is null || pageNumber is null || itemsPerPage is null)
        {
            reason = $"{parametersType.Name} is missing Query, PageNumber or ItemsPerPage";
            return null;
        }

        return new LegacySearchBridge(
            search,
            itemType,
            itemConstructor.GetParameters()[0].ParameterType,
            resultsConstructor,
            itemConstructor,
            query,
            pageNumber,
            itemsPerPage,
            parametersType.GetProperty(CultureProperty));
    }

    /// <summary>Runs a search on the package and returns it in the site's own result type.</summary>
    public object? Invoke(
        object parameters,
        IImobisoftSearchService search,
        IUmbracoContextAccessor umbracoContextAccessor,
        ILogger logger)
    {
        var term = _queryProperty.GetValue(parameters) as string ?? string.Empty;
        var pageNumber = _pageNumberProperty.GetValue(parameters) as int? ?? 1;
        var itemsPerPage = _itemsPerPageProperty.GetValue(parameters) as int? ?? 0;

        var request = new SearchRequest
        {
            Term = term,
            Page = pageNumber <= 0 ? 1 : pageNumber,

            // A page size set by the site wins; with none set, the profile's "Results per page" rule
            // decides, so it can be changed in the Search section without touching the site.
            PageSize = itemsPerPage > 0 ? itemsPerPage : null,
        };

        if (_cultureProperty?.GetValue(parameters) is string culture && !string.IsNullOrWhiteSpace(culture))
        {
            request.Cultures.Add(culture);
        }

        SearchResponse response = search.SearchAsync(request).GetAwaiter().GetResult();

        IList items = BuildItems(response, term, umbracoContextAccessor, logger);

        // The site works out "has more" by dividing by this, so it has to be the size the engine
        // actually used rather than whatever came in.
        _itemsPerPageProperty.SetValue(parameters, response.PageSize);

        return _resultsConstructor.Invoke(new[] { items, (object)(long)response.TotalResults, parameters });
    }

    /// <summary>
    /// Turns index hits back into the site's own result items.
    /// <para>
    /// The site's item type reads document type models and generated properties, so it needs the
    /// real published node rather than the indexed values. A hit whose node has since been
    /// unpublished, deleted, or which is not of the type the site expects, is dropped.
    /// </para>
    /// </summary>
    private IList BuildItems(
        SearchResponse response,
        string term,
        IUmbracoContextAccessor umbracoContextAccessor,
        ILogger logger)
    {
        var items = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(_itemType))!;

        if (!umbracoContextAccessor.TryGetUmbracoContext(out IUmbracoContext? context) || context is null)
        {
            return items;
        }

        foreach (SearchResultItem result in response.Results)
        {
            try
            {
                IPublishedContent? node = null;

                if (result.Key.HasValue)
                {
                    node = context.Content?.GetById(result.Key.Value) ?? context.Media?.GetById(result.Key.Value);
                }

                if (node is null && int.TryParse(result.Id, out var id))
                {
                    node = context.Content?.GetById(id) ?? context.Media?.GetById(id);
                }

                if (node is not null && _itemSourceType.IsInstanceOfType(node))
                {
                    items.Add(_itemConstructor.Invoke(new object[] { node, term }));
                }
            }
            catch (Exception ex)
            {
                // One unresolvable node must not take down the whole result page.
                logger.LogDebug(ex, "Could not map search result {Id} to the site's result type.", result.Id);
            }
        }

        return items;
    }

    /// <summary>Finds what a results collection is a collection of.</summary>
    private static Type? ResolveItemType(Type resultsType)
        => resultsType
            .GetInterfaces()
            .Concat(new[] { resultsType })
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()
            .FirstOrDefault();
}
