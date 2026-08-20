using System.Text.RegularExpressions;
using Imobisoft.Search.Models;
using Imobisoft.Search.Persistence;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Cache;
using Umbraco.Extensions;

namespace Imobisoft.Search.Services;

/// <inheritdoc />
public sealed partial class SearchProfileService : ISearchProfileService
{
    /// <summary>
    /// Profiles are cached with an expiry rather than only being cleared on write, so that a save on
    /// one server in a load balanced setup reaches the others without needing a cache refresher.
    /// </summary>
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly ISearchProfileRepository _repository;
    private readonly AppCaches _appCaches;
    private readonly ILogger<SearchProfileService> _logger;

    public SearchProfileService(
        ISearchProfileRepository repository,
        AppCaches appCaches,
        ILogger<SearchProfileService> logger)
    {
        _repository = repository;
        _appCaches = appCaches;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<SearchProfile> GetAll()
        => _appCaches.RuntimeCache.GetCacheItem(
               ImobisoftSearchConstants.Caching.ProfileCacheKey,
               () => _repository.GetAll().OrderByDescending(x => x.IsDefault).ThenBy(x => x.Name).ToList(),
               CacheDuration)
           ?? new List<SearchProfile>();

    /// <inheritdoc />
    public SearchProfile? Get(Guid key) => GetAll().FirstOrDefault(x => x.Key == key);

    /// <inheritdoc />
    public SearchProfile? GetByAlias(string alias)
        => string.IsNullOrWhiteSpace(alias)
            ? null
            : GetAll().FirstOrDefault(x => x.Alias.Equals(alias, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc />
    public SearchProfile? GetDefault()
    {
        IReadOnlyList<SearchProfile> all = GetAll();
        return all.FirstOrDefault(x => x.IsDefault) ?? all.FirstOrDefault();
    }

    /// <inheritdoc />
    public SearchProfile ResolveForSearch(string? alias)
    {
        SearchProfile? profile = string.IsNullOrWhiteSpace(alias) ? GetDefault() : GetByAlias(alias);

        if (profile is not null)
        {
            return profile;
        }

        // Falling back to an unrestricted transient profile keeps site search working even if the
        // profile table is empty or a caller asks for an alias that has been renamed.
        _logger.LogDebug(
            "No search profile resolved for alias '{Alias}', falling back to an unrestricted profile.",
            alias ?? "(default)");

        return SearchProfile.CreateDefault();
    }

    /// <inheritdoc />
    public SearchProfileResult Create(SearchProfile profile)
    {
        if (!IsValidAlias(profile.Alias))
        {
            return SearchProfileResult.Fail(SearchProfileOperationStatus.InvalidAlias);
        }

        if (GetByAlias(profile.Alias) is not null)
        {
            return SearchProfileResult.Fail(SearchProfileOperationStatus.DuplicateAlias);
        }

        profile.Key = profile.Key == Guid.Empty ? Guid.NewGuid() : profile.Key;

        // The first profile on a site has to be the default, or nothing would resolve.
        if (_repository.Count() == 0)
        {
            profile.IsDefault = true;
        }

        return Persist(profile);
    }

    /// <inheritdoc />
    public SearchProfileResult Update(Guid key, SearchProfile profile)
    {
        SearchProfile? existing = _repository.Get(key);

        if (existing is null)
        {
            return SearchProfileResult.Fail(SearchProfileOperationStatus.NotFound);
        }

        if (!IsValidAlias(profile.Alias))
        {
            return SearchProfileResult.Fail(SearchProfileOperationStatus.InvalidAlias);
        }

        SearchProfile? clash = GetByAlias(profile.Alias);

        if (clash is not null && clash.Key != key)
        {
            return SearchProfileResult.Fail(SearchProfileOperationStatus.DuplicateAlias);
        }

        profile.Key = key;
        profile.CreateDate = existing.CreateDate;

        // The default flag is only ever moved through SetDefault, so an update cannot leave a site
        // with two defaults or none.
        profile.IsDefault = existing.IsDefault;

        return Persist(profile);
    }

    /// <inheritdoc />
    public SearchProfileResult SetDefault(Guid key)
    {
        if (!_repository.SetDefault(key))
        {
            return SearchProfileResult.Fail(SearchProfileOperationStatus.NotFound);
        }

        ClearCache();
        return SearchProfileResult.Ok(_repository.Get(key)!);
    }

    /// <inheritdoc />
    public SearchProfileOperationStatus Delete(Guid key)
    {
        if (_repository.Get(key) is null)
        {
            return SearchProfileOperationStatus.NotFound;
        }

        if (!_repository.Delete(key))
        {
            return SearchProfileOperationStatus.CannotDeleteLastProfile;
        }

        ClearCache();
        return SearchProfileOperationStatus.Success;
    }

    private SearchProfileResult Persist(SearchProfile profile)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(profile.Name))
            {
                profile.Name = profile.Alias;
            }

            SearchProfile saved = _repository.Save(profile);
            ClearCache();
            return SearchProfileResult.Ok(saved);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save search profile '{Alias}'.", profile.Alias);
            return SearchProfileResult.Fail(SearchProfileOperationStatus.Failed);
        }
    }

    private void ClearCache() => _appCaches.RuntimeCache.ClearByKey(ImobisoftSearchConstants.Caching.ProfileCacheKey);

    /// <summary>
    /// Aliases end up in URLs and in site code, so they are held to letters, digits, dash and
    /// underscore.
    /// </summary>
    private static bool IsValidAlias(string alias)
        => !string.IsNullOrWhiteSpace(alias) && alias.Length <= 255 && AliasPattern().IsMatch(alias);

    [GeneratedRegex("^[a-zA-Z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex AliasPattern();
}
