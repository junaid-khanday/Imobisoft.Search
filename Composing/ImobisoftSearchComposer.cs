using Imobisoft.Search.Api.OpenApi;
using Imobisoft.Search.Api.Security;
using Imobisoft.Search.Configuration;
using Imobisoft.Search.Integration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Imobisoft.Search.Migrations;
using Imobisoft.Search.Persistence;
using Imobisoft.Search.Services;
using Imobisoft.Search.Services.Ai;
using Imobisoft.Search.Services.Templates;
using Imobisoft.Search.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Razor.TagHelpers;
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

        builder.Services.AddSingleton<ISearchProfileRepository, SearchProfileRepository>();
        builder.Services.AddSingleton<ISearchProfileService, SearchProfileService>();
        builder.Services.AddSingleton<IIndexCatalogService, IndexCatalogService>();
        builder.Services.AddSingleton<ISearchSettingsService, SearchSettingsService>();
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

        // What the search page renders, built the same way whether the package's own route is
        // serving it or a site is rendering it inside one of its own pages. Scoped, because it runs
        // the search through the scoped search service.
        builder.Services.AddScoped<ISearchPageModelBuilder, SearchPageModelBuilder>();

        // Finds the layout the site renders its own pages with. Singleton: the answer is the same
        // for every request and is cached behind this anyway.
        builder.Services.AddSingleton<ISiteLayoutLocator, SiteLayoutLocator>();

        // Finds the search page a site already had, so installing the package on a site that
        // already has search leaves it with one search page rather than two. Scoped, because
        // recognising a page reads the content Umbraco routed for this request and resolves URLs
        // through the request's own URL provider; the tree walk behind it is cached.
        builder.Services.AddScoped<ISiteSearchPageLocator, SiteSearchPageLocator>();

        // What puts the header search box on a site without the site placing it: a tag helper
        // component adds its stylesheet and script to every Razor view, so the existing search
        // link starts opening a box in place instead of leaving for an empty search page. Honours
        // the backoffice toggle on every request.
        builder.Services.AddTransient<ITagHelperComponent, HeaderSearchBoxTagHelperComponent>();

        // What gets search the site's header and footer: the site renders one of its own pages and
        // the whole search page is placed into it. Does nothing until a site page's path is named
        // in Search → Settings.
        builder.Services.AddScoped<ISearchPageRenderer, SearchPageRenderer>();
        builder.Services.AddTransient<ITagHelperComponent, SearchPageInjectionTagHelperComponent>();

        RegisterAi(builder);

        // Lets the backoffice show the shipped source of the package's views and write an editable
        // copy of one into the site.
        builder.Services.AddSingleton<ISearchTemplateService, SearchTemplateService>();
    }

    /// <summary>
    /// The live text tools. One client per wire format rather than per vendor, registered together
    /// so the service can pick by protocol - which is what lets a key from any supported provider
    /// work without new code.
    /// </summary>
    private static void RegisterAi(IUmbracoBuilder builder)
    {
        builder.Services.AddHttpClient(
            HttpCompletionClientBase.HttpClientName,
            client => client.Timeout = TimeSpan.FromSeconds(60));

        builder.Services.AddSingleton<IAiCredentialStore, AiCredentialStore>();
        builder.Services.AddSingleton<IAiContentReader, AiContentReader>();

        // The one place a profile's AI rules become an actual provider call. Singleton: it holds no
        // per-request state, and everything it needs is a singleton already.
        builder.Services.AddSingleton<IAiGateway, AiGateway>();

        // The two stages that run inside a search. Singletons for the same reason, and deliberately
        // free of any dependency on IImobisoftSearchService - the search service depends on these,
        // so a reference back the other way would be a cycle the container could not build.
        builder.Services.AddSingleton<IAiQueryPlanner, AiQueryPlanner>();
        builder.Services.AddSingleton<IAiReranker, AiReranker>();

        builder.Services.AddSingleton<IAiCompletionClient, AnthropicCompletionClient>();
        builder.Services.AddSingleton<IAiCompletionClient, OpenAiCompatibleCompletionClient>();
        builder.Services.AddSingleton<IAiCompletionClient, GeminiCompletionClient>();

        // Scoped, not singleton: answering a question runs a real search, and the search service is
        // itself scoped because it reaches for the current request's Umbraco context. The rate-limit
        // counters it keeps are static, so they survive across requests regardless.
        builder.Services.AddScoped<IAiTextService, AiTextService>();
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
