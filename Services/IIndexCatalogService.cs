using Imobisoft.Search.Models;

namespace Imobisoft.Search.Services;

/// <summary>
/// Discovers what there is to search. Everything here is read from the running site rather than
/// configured, which is what lets the dashboard offer a consumer's own custom Examine indexes and
/// document types without the package knowing anything about them in advance.
/// </summary>
public interface IIndexCatalogService
{
    /// <summary>Every registered Examine index, with its fields and health.</summary>
    IReadOnlyList<IndexInfo> GetIndexes(bool includeFields = true);

    /// <summary>A single index, or null when no index of that name is registered.</summary>
    IndexInfo? GetIndex(string name, bool includeFields = true);

    /// <summary>
    /// Names of the indexes a set of source rules resolves to. An empty rule list resolves to every
    /// index, which is how a freshly installed package searches everything.
    /// </summary>
    IReadOnlyList<string> ResolveIndexNames(SourceRules sources);

    /// <summary>
    /// The fields worth searching in an index, used when a profile has not named any explicitly.
    /// </summary>
    IReadOnlyList<IndexFieldInfo> GetSearchableFields(string indexName);

    IReadOnlyList<ContentTypeInfo> GetContentTypes();

    IReadOnlyList<ContentTypeInfo> GetMediaTypes();

    IReadOnlyList<LanguageInfo> GetLanguages();

    /// <summary>Everything the dashboard needs to render its pick-lists, in one call.</summary>
    SearchCatalog GetCatalog();
}
