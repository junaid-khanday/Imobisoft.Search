using Examine;
using Examine.Search;
using Imobisoft.Search.Models;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Services;
using Umbraco.Extensions;

namespace Imobisoft.Search.Services;

/// <inheritdoc />
public sealed class IndexCatalogService : IIndexCatalogService
{
    /// <summary>
    /// How many documents to sample when working out which fields an index actually holds. Umbraco
    /// content indexes add a field per document type property at index time, so the declared field
    /// definitions alone do not describe the index - only the documents do.
    /// </summary>
    private const int FieldSampleSize = 30;

    private const string FieldCacheKeyPrefix = "Imobisoft.Search.IndexFields.";

    /// <summary>
    /// Field discovery reads documents off disk, so it is cached. Short enough that a newly added
    /// document type property shows up in the dashboard without a restart.
    /// </summary>
    private static readonly TimeSpan FieldCacheDuration = TimeSpan.FromMinutes(2);

    private readonly IExamineManager _examineManager;
    private readonly IContentTypeService _contentTypeService;
    private readonly IMediaTypeService _mediaTypeService;
    private readonly ILanguageService _languageService;
    private readonly AppCaches _appCaches;
    private readonly ILogger<IndexCatalogService> _logger;

    public IndexCatalogService(
        IExamineManager examineManager,
        IContentTypeService contentTypeService,
        IMediaTypeService mediaTypeService,
        ILanguageService languageService,
        AppCaches appCaches,
        ILogger<IndexCatalogService> logger)
    {
        _examineManager = examineManager;
        _contentTypeService = contentTypeService;
        _mediaTypeService = mediaTypeService;
        _languageService = languageService;
        _appCaches = appCaches;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<IndexInfo> GetIndexes(bool includeFields = true)
        => _examineManager.Indexes
            .Select(index => BuildIndexInfo(index, includeFields))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <inheritdoc />
    public IndexInfo? GetIndex(string name, bool includeFields = true)
        => _examineManager.TryGetIndex(name, out IIndex? index) ? BuildIndexInfo(index, includeFields) : null;

    /// <inheritdoc />
    public IReadOnlyList<string> ResolveIndexNames(SourceRules sources)
    {
        var available = _examineManager.Indexes.Select(x => x.Name).ToList();

        if (sources.Indexes.Count == 0)
        {
            // With no explicit order, put the published content index first. A node can appear in
            // several indexes and the first one to return it wins, so this makes "search everything"
            // attribute results to the index a visitor-facing search should be reading.
            return available
                .OrderByDescending(name => name.Equals(
                    Umbraco.Cms.Core.Constants.UmbracoIndexes.ExternalIndexName,
                    StringComparison.OrdinalIgnoreCase))
                .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // Keep the profile's ordering but drop anything that is no longer registered, so removing a
        // custom index from the consumer project degrades rather than throws.
        var resolved = sources.Indexes
            .Where(requested => available.Contains(requested, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return resolved;
    }

    /// <inheritdoc />
    public IReadOnlyList<IndexFieldInfo> GetSearchableFields(string indexName)
        => GetFields(indexName).Where(x => x.IsSearchable).ToList();

    /// <inheritdoc />
    public IReadOnlyList<ContentTypeInfo> GetContentTypes()
        => _contentTypeService.GetAll()
            .Select(x => new ContentTypeInfo
            {
                Key = x.Key,
                Alias = x.Alias,
                Name = x.Name ?? x.Alias,
                Icon = x.Icon,
                IsElement = x.IsElement,
            })
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <inheritdoc />
    public IReadOnlyList<ContentTypeInfo> GetMediaTypes()
        => _mediaTypeService.GetAll()
            .Select(x => new ContentTypeInfo
            {
                Key = x.Key,
                Alias = x.Alias,
                Name = x.Name ?? x.Alias,
                Icon = x.Icon,
                IsElement = false,
            })
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <inheritdoc />
    public IReadOnlyList<LanguageInfo> GetLanguages()
        => _languageService.GetAllAsync().GetAwaiter().GetResult()
            .Select(x => new LanguageInfo
            {
                IsoCode = x.IsoCode,
                Name = x.CultureName,
                IsDefault = x.IsDefault,
            })
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <inheritdoc />
    public SearchCatalog GetCatalog() => new()
    {
        Indexes = GetIndexes().ToList(),
        ContentTypes = GetContentTypes().ToList(),
        MediaTypes = GetMediaTypes().ToList(),
        Languages = GetLanguages().ToList(),
    };

    private IndexInfo BuildIndexInfo(IIndex index, bool includeFields)
    {
        var info = new IndexInfo
        {
            Name = index.Name,
            IsUmbracoIndex = IsUmbracoIndex(index.Name),
        };

        try
        {
            ISearchResults probe = index.Searcher.CreateQuery().All().Execute(QueryOptions.SkipTake(0, 1));
            info.DocumentCount = probe.TotalItemCount;
            info.IsHealthy = true;
        }
        catch (Exception ex)
        {
            // A missing or corrupt Lucene directory throws here. Report it rather than hiding the
            // index, so the dashboard can tell the editor why nothing is being found.
            info.IsHealthy = false;
            info.HealthMessage = ex.Message;
            _logger.LogWarning(ex, "Examine index {IndexName} could not be queried.", index.Name);
        }

        if (includeFields && info.IsHealthy)
        {
            info.Fields = GetFields(index.Name).ToList();
            info.FieldCount = info.Fields.Count;
        }

        return info;
    }

    /// <summary>
    /// Works out an index's fields by combining what it declares with what its documents actually
    /// contain, then marks each one searchable and/or sortable.
    /// </summary>
    private IReadOnlyList<IndexFieldInfo> GetFields(string indexName)
        => _appCaches.RuntimeCache.GetCacheItem(
               FieldCacheKeyPrefix + indexName,
               () => DiscoverFields(indexName),
               FieldCacheDuration)
           ?? new List<IndexFieldInfo>();

    private List<IndexFieldInfo> DiscoverFields(string indexName)
    {
        if (!_examineManager.TryGetIndex(indexName, out IIndex? index))
        {
            return new List<IndexFieldInfo>();
        }

        var declaredTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (FieldDefinition definition in index.FieldDefinitions)
            {
                declaredTypes[definition.Name] = definition.Type;
                names.Add(definition.Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read field definitions from {IndexName}.", indexName);
        }

        try
        {
            ISearchResults sample = index.Searcher.CreateQuery()
                .All()
                .Execute(QueryOptions.SkipTake(0, FieldSampleSize));

            foreach (ISearchResult result in sample)
            {
                foreach (var key in result.Values.Keys)
                {
                    names.Add(key);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not sample documents from {IndexName} for field discovery.", indexName);
        }

        // A field is sortable when Umbraco has written the matching sort-optimised twin.
        var sortableFields = new HashSet<string>(
            names.Where(n => n.StartsWith(ImobisoftSearchConstants.IndexFields.SortPrefix, StringComparison.Ordinal))
                 .Select(n => n[ImobisoftSearchConstants.IndexFields.SortPrefix.Length..]),
            StringComparer.OrdinalIgnoreCase);

        return names
            .Where(name => !name.StartsWith(ImobisoftSearchConstants.IndexFields.Raw, StringComparison.Ordinal))
            .Where(name => !name.StartsWith(ImobisoftSearchConstants.IndexFields.SortPrefix, StringComparison.Ordinal))
            .Select(name =>
            {
                declaredTypes.TryGetValue(name, out var type);
                type ??= FieldDefinitionTypes.FullText;

                return new IndexFieldInfo
                {
                    Name = name,
                    Type = type,
                    IsSystemField = name.StartsWith("__", StringComparison.Ordinal),
                    IsSearchable = IsSearchableType(name, type),
                    IsSortable = sortableFields.Contains(name) || IsSortableType(type),
                };
            })
            .OrderBy(x => x.IsSystemField)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Free-text fields are worth searching; numbers, dates and Umbraco's internal plumbing are not.
    /// Document type properties are indexed as full text and have no declared definition, so an
    /// unknown type counts as searchable.
    /// </summary>
    private static bool IsSearchableType(string name, string type)
    {
        if (name.StartsWith("__", StringComparison.Ordinal))
        {
            // __NodeName is the one system field that genuinely holds the title.
            return name.Equals(ImobisoftSearchConstants.IndexFields.SystemNodeName, StringComparison.OrdinalIgnoreCase);
        }

        // Node ids, template ids and icon names are indexed as text but are not content. Searching
        // them adds noise and lets a visitor match a document by typing its internal id.
        if (ImobisoftSearchConstants.IndexFields.Metadata.Contains(name))
        {
            return false;
        }

        return type.Equals(FieldDefinitionTypes.FullText, StringComparison.OrdinalIgnoreCase)
               || type.Equals(FieldDefinitionTypes.FullTextSortable, StringComparison.OrdinalIgnoreCase)
               || type.Equals(FieldDefinitionTypes.InvariantCultureIgnoreCase, StringComparison.OrdinalIgnoreCase)
               || type.Equals(FieldDefinitionTypes.EmailAddress, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSortableType(string type)
        => type.Equals(FieldDefinitionTypes.FullTextSortable, StringComparison.OrdinalIgnoreCase)
           || type.Equals(FieldDefinitionTypes.Integer, StringComparison.OrdinalIgnoreCase)
           || type.Equals(FieldDefinitionTypes.Long, StringComparison.OrdinalIgnoreCase)
           || type.Equals(FieldDefinitionTypes.Float, StringComparison.OrdinalIgnoreCase)
           || type.Equals(FieldDefinitionTypes.Double, StringComparison.OrdinalIgnoreCase)
           || type.Equals(FieldDefinitionTypes.DateTime, StringComparison.OrdinalIgnoreCase);

    private static bool IsUmbracoIndex(string name)
        => name is Umbraco.Cms.Core.Constants.UmbracoIndexes.InternalIndexName
                or Umbraco.Cms.Core.Constants.UmbracoIndexes.ExternalIndexName
                or Umbraco.Cms.Core.Constants.UmbracoIndexes.MembersIndexName
           || name.StartsWith("Delivery", StringComparison.OrdinalIgnoreCase);
}
