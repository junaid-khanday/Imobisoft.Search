using Imobisoft.Search.Api;
using Imobisoft.Search.Api.OpenApi;
using Imobisoft.Search.Api.Security;
using Imobisoft.Search.Configuration;
using Imobisoft.Search.Integration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Imobisoft.Search.Migrations;
using Imobisoft.Search.Persistence;
using Imobisoft.Search.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Infrastructure.BackgroundJobs;

namespace Imobisoft.Search.Composing;

/// <summary>
/// Wires the whole package up. Umbraco discovers composers by type scanning, which is what lets a
/// consumer install the NuGet package and write no startup code at all - no service registration,
/// no route mapping, no migration trigger.
/// </summary>
public sealed class ImobisoftSearchComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        RegisterServices(builder);
        RegisterAuthorization(builder);
        RegisterOpenApi(builder);

        builder.AddNotificationAsyncHandler<UmbracoApplicationStartingNotification, ImobisoftSearchInstaller>();

        TakeOverSiteSearch(builder);
    }

    /// <summary>
    /// Puts the package behind whatever search service the site already had, so an existing search
    /// page needs no changes.
    /// <para>
    /// Deferred to a collection builder rather than done here, because a site's own composer may run
    /// after this one and the takeover can only replace a registration that already exists.
    /// </para>
    /// </summary>
    private static void TakeOverSiteSearch(IUmbracoBuilder builder)
    {
        var options = new ImobisoftSearchOptions();
        builder.Config.GetSection(ImobisoftSearchOptions.SectionName).Bind(options);

        // Composition happens before there is a service provider, so this logs through the builder's
        // own factory rather than a resolved logger.
        ILogger logger = builder.BuilderLoggerFactory.CreateLogger(typeof(SiteSearchTakeover));

        builder.WithCollectionBuilder<SiteSearchTakeoverBuilder>().Configure(options, logger);
    }

    private static void RegisterServices(IUmbracoBuilder builder)
    {
        // Bound with a colon-separated section name, so a consuming site writes
        // "Imobisoft": { "Search": { ... } } and nothing else.
        builder.Services.Configure<ImobisoftSearchOptions>(
            builder.Config.GetSection(ImobisoftSearchOptions.SectionName));

        builder.Services.AddHttpContextAccessor();

        // Pooled connections for the OpenAI-compatible provider. Registered rather than newing an
        // HttpClient per call, which is what exhausts sockets under load.
        builder.Services.AddHttpClient();

        builder.Services.AddSingleton<ISearchProfileRepository, SearchProfileRepository>();
        builder.Services.AddSingleton<ISearchProfileService, SearchProfileService>();
        builder.Services.AddSingleton<IIndexCatalogService, IndexCatalogService>();

        // Themes are files on disk or views compiled into an assembly, so the set cannot change
        // between deployments - the service caches what it finds and is safely shared.
        builder.Services.AddSingleton<ISearchThemeService, SearchThemeService>();

        // Creates the "Imobi Search" document type and template on first boot, so a site can add a
        // search page from the Content tree without writing anything.
        builder.Services.AddSingleton(sp => new Migrations.SearchPageScaffolder(
            sp.GetRequiredService<Umbraco.Cms.Core.Services.IContentTypeService>(),
            sp.GetRequiredService<Umbraco.Cms.Core.Services.ITemplateService>(),
            sp.GetRequiredService<Umbraco.Cms.Core.Strings.IShortStringHelper>(),
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ImobisoftSearchOptions>>().Value,
            sp.GetRequiredService<ILogger<Migrations.SearchPageScaffolder>>()));

        // Renders a themed partial to a string so the backoffice test panel previews the markup the
        // front end actually serves, rather than its own approximation of it.
        builder.Services.AddSingleton<ISearchRenderService, SearchRenderService>();
        builder.Services.AddSingleton<ISearchSettingsService, SearchSettingsService>();

        // Singleton because it owns the provider client and the per-minute rate window - both of
        // which have to be shared across requests to mean anything.
        builder.Services.AddSingleton<Services.Ai.IAiSearchService, Services.Ai.SearchAiService>();
        builder.Services.AddSingleton<ISearchAnalyticsRepository, SearchAnalyticsRepository>();

        // Singleton because it owns the queue that the background writer drains.
        builder.Services.AddSingleton<ISearchAnalyticsService, SearchAnalyticsService>();
        builder.Services.AddHostedService<SearchAnalyticsWriter>();

        // Umbraco exposes no public helper for registering a custom recurring job, so this mirrors
        // how it registers its own: the job as a singleton, driven by Umbraco's hosted service, which
        // is what honours the job's ServerRoles and MainDom on a load-balanced site.
        builder.Services.AddSingleton<SearchAnalyticsRetentionJob>();
        builder.Services.AddHostedService<RecurringBackgroundJobHostedService<SearchAnalyticsRetentionJob>>();

        // Scoped, because resolving result URLs reaches for the current request's Umbraco context.
        builder.Services.AddScoped<IImobisoftSearchService, ImobisoftSearchService>();
    }

    private static void RegisterAuthorization(IUmbracoBuilder builder)
    {
        builder.Services.AddSingleton<IAuthorizationHandler, ImobisoftSearchSectionHandler>();

        // No authentication scheme is named here on purpose. ASP.NET Core merges the policies from
        // both [Authorize] attributes on the controller base, so this policy runs against the user
        // that Umbraco's own BackOfficeAccess policy has already authenticated.
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(
                ImobisoftSearchConstants.Api.SectionAccessPolicy,
                policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.Requirements.Add(new ImobisoftSearchSectionRequirement());
                });
    }

    private static void RegisterOpenApi(IUmbracoBuilder builder)
        => builder.Services.ConfigureOptions<ConfigureSearchSwaggerOptions>();
}
