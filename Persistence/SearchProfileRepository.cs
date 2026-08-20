using Imobisoft.Search.Models;
using Microsoft.Extensions.Logging;
using NPoco;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Extensions;

namespace Imobisoft.Search.Persistence;

/// <inheritdoc />
public sealed class SearchProfileRepository : ISearchProfileRepository
{
    private readonly IScopeProvider _scopeProvider;
    private readonly ILogger<SearchProfileRepository> _logger;

    public SearchProfileRepository(IScopeProvider scopeProvider, ILogger<SearchProfileRepository> logger)
    {
        _scopeProvider = scopeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public IEnumerable<SearchProfile> GetAll()
    {
        using IScope scope = _scopeProvider.CreateScope(autoComplete: true);
        List<SearchProfileSchema> rows = scope.Database.Fetch<SearchProfileSchema>(
            scope.SqlContext.Sql().SelectAll().From<SearchProfileSchema>());

        return rows.Select(Map).ToList();
    }

    /// <inheritdoc />
    public SearchProfile? Get(Guid key)
    {
        using IScope scope = _scopeProvider.CreateScope(autoComplete: true);
        SearchProfileSchema? row = scope.Database.FirstOrDefault<SearchProfileSchema>(
            scope.SqlContext.Sql().SelectAll().From<SearchProfileSchema>().Where<SearchProfileSchema>(x => x.UniqueId == key));

        return row is null ? null : Map(row);
    }

    /// <inheritdoc />
    public SearchProfile? GetByAlias(string alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            return null;
        }

        using IScope scope = _scopeProvider.CreateScope(autoComplete: true);
        SearchProfileSchema? row = scope.Database.FirstOrDefault<SearchProfileSchema>(
            scope.SqlContext.Sql().SelectAll().From<SearchProfileSchema>().Where<SearchProfileSchema>(x => x.ProfileAlias == alias));

        return row is null ? null : Map(row);
    }

    /// <inheritdoc />
    public SearchProfile? GetDefault()
    {
        using IScope scope = _scopeProvider.CreateScope(autoComplete: true);
        SearchProfileSchema? row = scope.Database.FirstOrDefault<SearchProfileSchema>(
            scope.SqlContext.Sql().SelectAll().From<SearchProfileSchema>().Where<SearchProfileSchema>(x => x.IsDefault));

        // A site whose default flag was lost (e.g. the default profile was deleted) still needs to
        // search, so fall back to whatever profile exists rather than returning nothing.
        row ??= scope.Database.FirstOrDefault<SearchProfileSchema>(
            scope.SqlContext.Sql().SelectAll().From<SearchProfileSchema>());

        return row is null ? null : Map(row);
    }

    /// <inheritdoc />
    public SearchProfile Save(SearchProfile profile)
    {
        if (profile.Key == Guid.Empty)
        {
            profile.Key = Guid.NewGuid();
        }

        using IScope scope = _scopeProvider.CreateScope();

        SearchProfileSchema? existing = scope.Database.FirstOrDefault<SearchProfileSchema>(
            scope.SqlContext.Sql().SelectAll().From<SearchProfileSchema>().Where<SearchProfileSchema>(x => x.UniqueId == profile.Key));

        DateTime now = DateTime.UtcNow;

        if (existing is null)
        {
            var row = new SearchProfileSchema
            {
                UniqueId = profile.Key,
                ProfileAlias = profile.Alias,
                ProfileName = profile.Name,
                IsDefault = profile.IsDefault,
                IsEnabled = profile.Enabled,
                ConfigJson = SearchProfileJson.Serialize(profile.Rules),
                CreateDate = now,
                UpdateDate = now,
            };

            scope.Database.Insert(row);
            profile.CreateDate = now;
        }
        else
        {
            existing.ProfileAlias = profile.Alias;
            existing.ProfileName = profile.Name;
            existing.IsDefault = profile.IsDefault;
            existing.IsEnabled = profile.Enabled;
            existing.ConfigJson = SearchProfileJson.Serialize(profile.Rules);
            existing.UpdateDate = now;

            scope.Database.Update(existing);
            profile.CreateDate = existing.CreateDate;
        }

        profile.UpdateDate = now;

        if (profile.IsDefault)
        {
            ClearDefaultExcept(scope, profile.Key);
        }

        scope.Complete();
        return profile;
    }

    /// <inheritdoc />
    public bool Delete(Guid key)
    {
        using IScope scope = _scopeProvider.CreateScope();

        SearchProfileSchema? row = scope.Database.FirstOrDefault<SearchProfileSchema>(
            scope.SqlContext.Sql().SelectAll().From<SearchProfileSchema>().Where<SearchProfileSchema>(x => x.UniqueId == key));

        if (row is null)
        {
            scope.Complete();
            return false;
        }

        // Refuse to leave the site with no profile at all - search would have nothing to run.
        var total = scope.Database.ExecuteScalar<int>(
            scope.SqlContext.Sql().SelectCount().From<SearchProfileSchema>());

        if (total <= 1)
        {
            _logger.LogWarning("Refused to delete search profile {Alias} because it is the only one left.", row.ProfileAlias);
            scope.Complete();
            return false;
        }

        scope.Database.Delete(row);

        // Promote another profile so the site always has a default.
        if (row.IsDefault)
        {
            SearchProfileSchema? next = scope.Database.FirstOrDefault<SearchProfileSchema>(
                scope.SqlContext.Sql().SelectAll().From<SearchProfileSchema>());

            if (next is not null)
            {
                next.IsDefault = true;
                next.UpdateDate = DateTime.UtcNow;
                scope.Database.Update(next);
            }
        }

        scope.Complete();
        return true;
    }

    /// <inheritdoc />
    public bool SetDefault(Guid key)
    {
        using IScope scope = _scopeProvider.CreateScope();

        SearchProfileSchema? row = scope.Database.FirstOrDefault<SearchProfileSchema>(
            scope.SqlContext.Sql().SelectAll().From<SearchProfileSchema>().Where<SearchProfileSchema>(x => x.UniqueId == key));

        if (row is null)
        {
            scope.Complete();
            return false;
        }

        row.IsDefault = true;
        row.UpdateDate = DateTime.UtcNow;
        scope.Database.Update(row);
        ClearDefaultExcept(scope, key);

        scope.Complete();
        return true;
    }

    /// <inheritdoc />
    public int Count()
    {
        using IScope scope = _scopeProvider.CreateScope(autoComplete: true);
        return scope.Database.ExecuteScalar<int>(scope.SqlContext.Sql().SelectCount().From<SearchProfileSchema>());
    }

    /// <inheritdoc />
    public bool TableExists()
    {
        try
        {
            using IScope scope = _scopeProvider.CreateScope(autoComplete: true);
            return scope.Database.DatabaseType is not null
                   && scope.SqlContext.SqlSyntax.DoesTableExist(scope.Database, ImobisoftSearchConstants.Database.ProfileTableName);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not determine whether the search profile table exists.");
            return false;
        }
    }

    private static void ClearDefaultExcept(IScope scope, Guid key)
        => scope.Database.Execute(
            $"UPDATE {scope.SqlContext.SqlSyntax.GetQuotedTableName(ImobisoftSearchConstants.Database.ProfileTableName)} " +
            $"SET {scope.SqlContext.SqlSyntax.GetQuotedColumnName("isDefault")} = @0 " +
            $"WHERE {scope.SqlContext.SqlSyntax.GetQuotedColumnName("uniqueId")} <> @1",
            false,
            key);

    private static SearchProfile Map(SearchProfileSchema row) => new()
    {
        Key = row.UniqueId,
        Alias = row.ProfileAlias,
        Name = row.ProfileName,
        IsDefault = row.IsDefault,
        Enabled = row.IsEnabled,
        CreateDate = row.CreateDate,
        UpdateDate = row.UpdateDate,
        Rules = SearchProfileJson.DeserializeOrDefault<SearchRuleSet>(row.ConfigJson),
    };
}
