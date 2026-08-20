using Imobisoft.Search.Models;
using Imobisoft.Search.Persistence;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Services;
using Umbraco.Extensions;

namespace Imobisoft.Search.Services;

/// <summary>Site-wide package settings, as opposed to the per-profile search rules.</summary>
public interface ISearchSettingsService
{
    SearchSettings Get();

    void Save(SearchSettings settings);
}

/// <inheritdoc />
public sealed class SearchSettingsService : ISearchSettingsService
{
    /// <summary>
    /// Cached with an expiry rather than only cleared on write, so a change on one server in a
    /// load-balanced setup reaches the others without a cache refresher.
    /// </summary>
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly IKeyValueService _keyValueService;
    private readonly AppCaches _appCaches;
    private readonly ILogger<SearchSettingsService> _logger;

    public SearchSettingsService(
        IKeyValueService keyValueService,
        AppCaches appCaches,
        ILogger<SearchSettingsService> logger)
    {
        _keyValueService = keyValueService;
        _appCaches = appCaches;
        _logger = logger;
    }

    /// <inheritdoc />
    public SearchSettings Get()
        => _appCaches.RuntimeCache.GetCacheItem(
               ImobisoftSearchConstants.Settings.CacheKey,
               Load,
               CacheDuration)
           ?? new SearchSettings();

    /// <inheritdoc />
    public void Save(SearchSettings settings)
    {
        _keyValueService.SetValue(
            ImobisoftSearchConstants.Settings.KeyValueKey,
            SearchProfileJson.Serialize(settings));

        _appCaches.RuntimeCache.ClearByKey(ImobisoftSearchConstants.Settings.CacheKey);
    }

    /// <summary>
    /// Settings live in Umbraco's own key/value table rather than a table of ours, because one JSON
    /// document needs no schema and no migration when a new setting is added.
    /// </summary>
    private SearchSettings Load()
    {
        try
        {
            var json = _keyValueService.GetValue(ImobisoftSearchConstants.Settings.KeyValueKey);
            return SearchProfileJson.DeserializeOrDefault<SearchSettings>(json);
        }
        catch (Exception ex)
        {
            // Settings failing to load must never take search down; the defaults are safe.
            _logger.LogWarning(ex, "Could not read Imobisoft.Search settings; falling back to defaults.");
            return new SearchSettings();
        }
    }
}
