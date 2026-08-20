using Imobisoft.Search.Models;

namespace Imobisoft.Search.Persistence;

/// <summary>Raw data access for search profiles. Callers should normally use
/// <see cref="Services.ISearchProfileService"/>, which adds caching and default-profile handling.</summary>
public interface ISearchProfileRepository
{
    IEnumerable<SearchProfile> GetAll();

    SearchProfile? Get(Guid key);

    SearchProfile? GetByAlias(string alias);

    SearchProfile? GetDefault();

    /// <summary>Inserts or updates by <see cref="SearchProfile.Key"/>.</summary>
    SearchProfile Save(SearchProfile profile);

    /// <summary>Returns false when the profile does not exist or is the last remaining one.</summary>
    bool Delete(Guid key);

    /// <summary>Makes this profile the default and clears the flag on every other profile.</summary>
    bool SetDefault(Guid key);

    int Count();

    /// <summary>True when the profile table has been created by the migration.</summary>
    bool TableExists();
}
