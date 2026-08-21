using System.Text.Json.Serialization;

namespace Imobisoft.Search.Models;

/// <summary>How a facet groups the results it counts.</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<FacetKind>))]
public enum FacetKind
{
    /// <summary>One bucket per distinct value of the field, e.g. one per document type.</summary>
    Field,

    /// <summary>Buckets defined by explicit date ranges, e.g. "last 7 days", "this year".</summary>
    DateRange,

    /// <summary>Buckets defined by explicit numeric ranges, e.g. a price band.</summary>
    Numeric,
}

/// <summary>One bucket of a <see cref="FacetKind.DateRange"/> or <see cref="FacetKind.Numeric"/> facet.</summary>
public sealed class FacetRange
{
    /// <summary>Stable identifier the site posts back to filter by this bucket.</summary>
    public string Alias { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Inclusive lower bound. For dates this accepts either an ISO date or a relative expression
    /// such as <c>now-7d</c>, so a "last week" facet does not go stale. Empty means unbounded.
    /// </summary>
    public string From { get; set; } = string.Empty;

    /// <summary>Exclusive upper bound, same format as <see cref="From"/>. Empty means unbounded.</summary>
    public string To { get; set; } = string.Empty;
}

/// <summary>
/// A filter dimension returned alongside results so the site can render a filter sidebar with live
/// counts. Counts are computed over the matched result window.
/// </summary>
public sealed class FacetDefinition
{
    /// <summary>Key this facet appears under in the response, and the key the site filters by.</summary>
    public string Alias { get; set; } = string.Empty;

    /// <summary>Index field the facet reads.</summary>
    public string Field { get; set; } = string.Empty;

    /// <summary>Label for the site to render above the filter group.</summary>
    public string Label { get; set; } = string.Empty;

    public FacetKind Kind { get; set; } = FacetKind.Field;

    /// <summary>Caps how many buckets come back, highest count first.</summary>
    public int MaxValues { get; set; } = 20;

    /// <summary>Buckets for range facets. Ignored for <see cref="FacetKind.Field"/>.</summary>
    public IList<FacetRange> Ranges { get; set; } = new List<FacetRange>();

    /// <summary>Hides buckets with no matches instead of returning them at zero.</summary>
    public bool HideEmpty { get; set; } = true;

    /// <summary>Whether this filter is enabled and active in search results.</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>One bucket in a computed facet.</summary>
public sealed class FacetValue
{
    public string Value { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public int Count { get; set; }

    /// <summary>True when the incoming request filtered by this bucket.</summary>
    public bool IsSelected { get; set; }
}

/// <summary>A computed facet, ready to render.</summary>
public sealed class FacetResult
{
    public string Alias { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public FacetKind Kind { get; set; }

    public IList<FacetValue> Values { get; set; } = new List<FacetValue>();
}
