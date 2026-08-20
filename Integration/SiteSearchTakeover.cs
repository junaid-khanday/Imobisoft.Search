using Imobisoft.Search.Configuration;
using Imobisoft.Search.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Web;

namespace Imobisoft.Search.Integration;

/// <summary>
/// Finds a site's own search service and puts this package behind it, so an existing search page
/// starts obeying the Search section without a line of code being written in the site.
/// <para>
/// This runs at registration time. Every candidate is checked against
/// <see cref="LegacySearchBridge.TryCreate"/> first: if any part of the shape is not understood, the
/// registration is left exactly as it was and the reason is logged. A site whose search cannot be
/// bridged keeps working as it always did.
/// </para>
/// <para>
/// Turn it off with <c>Imobisoft:Search:TakeOverSiteSearch</c> set to <c>false</c>.
/// </para>
/// </summary>
internal static class SiteSearchTakeover
{
    /// <summary>
    /// Interface names treated as a site's search service. Matched on name rather than namespace,
    /// because every site puts it somewhere different.
    /// </summary>
    private static readonly string[] CandidateNames = { "ISearchService", "ISiteSearchService", "ISearchProvider" };

    public static void Apply(IServiceCollection services, ImobisoftSearchOptions options, ILogger logger)
    {
        if (!options.TakeOverSiteSearch)
        {
            logger.LogInformation("Imobisoft.Search is not taking over site search; TakeOverSiteSearch is off.");
            return;
        }

        List<ServiceDescriptor> candidates = services
            .Where(d => d.ServiceType.IsInterface && IsCandidate(d.ServiceType, options))
            .ToList();

        if (candidates.Count == 0)
        {
            // Nothing to take over is the normal case for a site with no search of its own. It will
            // use IImobisoftSearchService directly.
            logger.LogDebug("Imobisoft.Search found no existing site search service to take over.");
            return;
        }

        foreach (ServiceDescriptor descriptor in candidates)
        {
            Replace(services, descriptor, logger);
        }
    }

    private static bool IsCandidate(Type serviceType, ImobisoftSearchOptions options)
    {
        // Never proxy our own service - that would put the package behind itself.
        if (serviceType.Assembly == typeof(IImobisoftSearchService).Assembly)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(options.SiteSearchInterfaceName))
        {
            return serviceType.Name.Equals(options.SiteSearchInterfaceName, StringComparison.OrdinalIgnoreCase);
        }

        return CandidateNames.Contains(serviceType.Name, StringComparer.OrdinalIgnoreCase);
    }

    private static void Replace(IServiceCollection services, ServiceDescriptor descriptor, ILogger logger)
    {
        LegacySearchBridge? bridge = LegacySearchBridge.TryCreate(descriptor.ServiceType, out var reason);

        if (bridge is null)
        {
            logger.LogWarning(
                "Imobisoft.Search left {Interface} alone because {Reason}. That site's search is unchanged; "
                + "use IImobisoftSearchService directly if you want the package to answer it.",
                descriptor.ServiceType.FullName,
                reason);
            return;
        }

        services.Remove(descriptor);

        services.Add(new ServiceDescriptor(
            descriptor.ServiceType,
            provider => LegacySearchServiceProxy.Create(
                descriptor.ServiceType,
                bridge,
                provider.GetRequiredService<IImobisoftSearchService>(),
                provider.GetRequiredService<IUmbracoContextAccessor>(),
                provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(LegacySearchServiceProxy)),

                // The site's original implementation stays available, both for members of the
                // interface the package does not answer and as a fallback if a search throws.
                CreateOriginal(provider, descriptor, logger)),
            descriptor.Lifetime));

        logger.LogInformation(
            "Imobisoft.Search is now answering {Interface}. Site search follows the Search section; "
            + "set Imobisoft:Search:TakeOverSiteSearch to false to undo this.",
            descriptor.ServiceType.FullName);
    }

    /// <summary>
    /// Builds the implementation the site originally registered, so it can still be reached. A
    /// failure here is not fatal - the proxy simply has nothing to fall back to.
    /// </summary>
    private static object? CreateOriginal(IServiceProvider provider, ServiceDescriptor descriptor, ILogger logger)
    {
        try
        {
            if (descriptor.ImplementationInstance is not null)
            {
                return descriptor.ImplementationInstance;
            }

            if (descriptor.ImplementationFactory is not null)
            {
                return descriptor.ImplementationFactory(provider);
            }

            return descriptor.ImplementationType is not null
                ? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType)
                : null;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not construct the site's original {Interface}.", descriptor.ServiceType.Name);
            return null;
        }
    }
}
