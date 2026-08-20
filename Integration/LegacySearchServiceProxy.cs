using System.Linq;
using System.Reflection;
using Imobisoft.Search.Services;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Web;

namespace Imobisoft.Search.Integration;

/// <summary>
/// Stands in for a site's own search service, answering its interface with results from this
/// package.
/// <para>
/// A <see cref="DispatchProxy"/> is used because the interface being implemented is not known until
/// the site starts. Only the one search method is answered; anything else on the interface falls
/// through to the implementation the site originally registered, so a service with extra members
/// keeps working.
/// </para>
/// </summary>
public class LegacySearchServiceProxy : DispatchProxy
{
    private LegacySearchBridge _bridge = null!;
    private IImobisoftSearchService _search = null!;
    private IUmbracoContextAccessor _umbracoContextAccessor = null!;
    private ILogger _logger = null!;
    private object? _fallback;

    /// <summary>
    /// Builds a proxy for <paramref name="serviceType"/>.
    /// <para>
    /// The interface type is only known at run time, so <see cref="DispatchProxy.Create{T, TProxy}"/>
    /// has to be closed over it reflectively.
    /// </para>
    /// </summary>
    internal static object Create(
        Type serviceType,
        LegacySearchBridge bridge,
        IImobisoftSearchService search,
        IUmbracoContextAccessor umbracoContextAccessor,
        ILogger logger,
        object? fallback)
    {
        // Selected by shape rather than by name alone: DispatchProxy.Create has both a generic and a
        // non-generic overload, so asking for it by name is ambiguous.
        MethodInfo create = typeof(DispatchProxy)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == nameof(DispatchProxy.Create)
                         && m.IsGenericMethodDefinition
                         && m.GetGenericArguments().Length == 2
                         && m.GetParameters().Length == 0)
            .MakeGenericMethod(serviceType, typeof(LegacySearchServiceProxy));

        var proxy = (LegacySearchServiceProxy)create.Invoke(null, null)!;

        proxy._bridge = bridge;
        proxy._search = search;
        proxy._umbracoContextAccessor = umbracoContextAccessor;
        proxy._logger = logger;
        proxy._fallback = fallback;

        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null)
        {
            return null;
        }

        var isSearch = targetMethod.Name == _bridge.SearchMethod.Name
                       && targetMethod.GetParameters().Length == 1
                       && args?.Length == 1
                       && args[0] is not null;

        if (isSearch)
        {
            try
            {
                return _bridge.Invoke(args![0]!, _search, _umbracoContextAccessor, _logger);
            }
            catch (Exception ex)
            {
                // Falling back to the site's own search is far better than an error page. This is
                // logged as an error because it means the takeover is not working and needs looking
                // at, even though the visitor sees results.
                _logger.LogError(ex, "Imobisoft.Search could not answer the site's search; falling back to its own.");
            }
        }

        return _fallback is null ? null : targetMethod.Invoke(_fallback, args);
    }
}
