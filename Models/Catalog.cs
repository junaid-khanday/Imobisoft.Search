namespace Imobisoft.Search.Models;

/// <summary>
/// A field the dashboard can offer as searchable, sortable or returnable.
/// </summary>
public sealed class IndexFieldInfo
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Examine field type, e.g. <c>fulltext</c>, <c>string</c>, <c>datetime</c>, <c>number</c>.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>True when the field holds free text and is worth searching against.</summary>
    public bool IsSearchable { get; set; }

    /// <summary>True when a matching <c>__Sort_</c> field exists, so results can be ordered by it.</summary>
    public bool IsSortable { get; set; }

    /// <summary>True for Umbraco's internal <c>__</c> plumbing fields, which the UI hides by default.</summary>
    public bool IsSystemField { get; set; }
}

/// <summary>
/// An Examine index discovered at runtime. Populates the index picker in the dashboard, so a
/// consumer's own custom indexes show up alongside Umbraco's built-in ones without any registration.
/// </summary>
public sealed class IndexInfo
{
    public string Name { get; set; } = string.Empty;

    /// <summary>False when the index reports a problem; the UI surfaces this so a broken index is obvious.</summary>
    public bool IsHealthy { get; set; }

    /// <summary>Why the index is unhealthy, when it is.</summary>
    public string? HealthMessage { get; set; }

    public long DocumentCount { get; set; }

    public long FieldCount { get; set; }

    /// <summary>True for the indexes Umbraco itself registers, as opposed to consumer-defined ones.</summary>
    public bool IsUmbracoIndex { get; set; }

    public IList<IndexFieldInfo> Fields { get; set; } = new List<IndexFieldInfo>();
}

/// <summary>A document or media type offered as an include/exclude option.</summary>
public sealed class ContentTypeInfo
{
    public Guid Key { get; set; }

    public string Alias { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Icon { get; set; }

    /// <summary>Element types cannot be searched on their own, so the UI can grey them out.</summary>
    public bool IsElement { get; set; }
}

/// <summary>A language offered as a culture filter.</summary>
public sealed class LanguageInfo
{
    public string IsoCode { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IsDefault { get; set; }
}

/// <summary>Everything the dashboard needs to render its pick-lists, in one round trip.</summary>
public sealed class SearchCatalog
{
    public IList<IndexInfo> Indexes { get; set; } = new List<IndexInfo>();

    public IList<ContentTypeInfo> ContentTypes { get; set; } = new List<ContentTypeInfo>();

    public IList<ContentTypeInfo> MediaTypes { get; set; } = new List<ContentTypeInfo>();

    public IList<LanguageInfo> Languages { get; set; } = new List<LanguageInfo>();
}
