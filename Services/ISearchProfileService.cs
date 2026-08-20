using Imobisoft.Search.Models;

namespace Imobisoft.Search.Services;

/// <summary>The outcome of a write, so the API can distinguish "not found" from "alias taken".</summary>
public enum SearchProfileOperationStatus
{
    Success,
    NotFound,
    DuplicateAlias,
    InvalidAlias,
    CannotDeleteLastProfile,
    Failed,
}

/// <summary>Result of a profile write.</summary>
/// <param name="Status">What happened.</param>
/// <param name="Profile">The saved profile, when the write succeeded.</param>
public readonly record struct SearchProfileResult(SearchProfileOperationStatus Status, SearchProfile? Profile)
{
    public bool Success => Status == SearchProfileOperationStatus.Success;

    public static SearchProfileResult Ok(SearchProfile profile) => new(SearchProfileOperationStatus.Success, profile);

    public static SearchProfileResult Fail(SearchProfileOperationStatus status) => new(status, null);
}

/// <summary>
/// Reads and writes search profiles. Reads are cached, because every search on the site resolves a
/// profile and none of them should cost a database round trip.
/// </summary>
public interface ISearchProfileService
{
    IReadOnlyList<SearchProfile> GetAll();

    SearchProfile? Get(Guid key);

    SearchProfile? GetByAlias(string alias);

    /// <summary>The profile used when a search does not name one.</summary>
    SearchProfile? GetDefault();

    /// <summary>
    /// Resolves the profile a request should run against: the named one, else the default, else a
    /// transient unrestricted profile so that search still works on a site whose profiles were all
    /// deleted.
    /// </summary>
    SearchProfile ResolveForSearch(string? alias);

    SearchProfileResult Create(SearchProfile profile);

    SearchProfileResult Update(Guid key, SearchProfile profile);

    SearchProfileResult SetDefault(Guid key);

    SearchProfileOperationStatus Delete(Guid key);
}
