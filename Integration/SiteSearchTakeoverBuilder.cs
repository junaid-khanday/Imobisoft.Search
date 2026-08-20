using Imobisoft.Search.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Composing;

namespace Imobisoft.Search.Integration;

/// <summary>
/// Runs the site search takeover at the last possible moment before the container is built.
/// <para>
/// The takeover can only replace a registration that already exists, and composer order is not
/// something a package can rely on - a site's own composer may well run after this package's. Umbraco
/// calls <see cref="ICollectionBuilder.RegisterWith"/> on every collection builder during
/// <c>IUmbracoBuilder.Build()</c>, which is after every composer has run and before the service
/// provider exists. That makes this the only point where the site's own registrations are guaranteed
/// to be visible and still changeable.
/// </para>
/// </summary>
public class SiteSearchTakeoverBuilder : ICollectionBuilder
{
    private ImobisoftSearchOptions? _options;
    private ILogger? _logger;

    /// <summary>Supplies what the takeover needs. Called from the package's composer.</summary>
    internal void Configure(ImobisoftSearchOptions options, ILogger logger)
    {
        _options = options;
        _logger = logger;
    }

    public void RegisterWith(IServiceCollection services)
    {
        if (_options is null || _logger is null)
        {
            return;
        }

        SiteSearchTakeover.Apply(services, _options, _logger);
    }
}
