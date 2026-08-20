using System.Text.Json.Serialization;

namespace Imobisoft.Search.Models;

/// <summary>How a search term is matched against a single field.</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<FieldMatchMode>))]
public enum FieldMatchMode
{
    /// <summary>The term must appear as-is.</summary>
    Exact,

    /// <summary>The term matches the start of a word (<c>term*</c>). Good for type-ahead.</summary>
    Prefix,

    /// <summary>Tolerates spelling mistakes (<c>term~</c>) using <see cref="MatchingRules.Fuzziness"/>.</summary>
    Fuzzy,

    /// <summary>Matches anywhere in a word (<c>*term*</c>). Slowest, widest net.</summary>
    Wildcard,
}

/// <summary>How multiple field clauses are combined.</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<SearchOperator>))]
public enum SearchOperator
{
    /// <summary>Every clause must match.</summary>
    And,

    /// <summary>Any clause may match.</summary>
    Or,
}

/// <summary>A single searchable field and how much it counts towards relevance.</summary>
public sealed class SearchFieldRule
{
    /// <summary>The indexed field name, e.g. <c>nodeName</c> or <c>bodyText</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Relevance multiplier. 1 is neutral; a title field at 10 will outrank a body match.
    /// </summary>
    public float Boost { get; set; } = 1f;

    public FieldMatchMode MatchMode { get; set; } = FieldMatchMode.Prefix;

    /// <summary>Lets the dashboard keep a configured field around while temporarily turning it off.</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Defines <em>how</em> a term matches: which fields carry the search, how forgiving matching is,
/// and how the term itself is pre-processed.
/// </summary>
public sealed class MatchingRules
{
    /// <summary>
    /// Fields to search, with per-field weighting. Empty means the package picks the searchable
    /// text fields from the index itself, which is the shipped default.
    /// </summary>
    public IList<SearchFieldRule> Fields { get; set; } = new List<SearchFieldRule>();

    /// <summary>How the per-field clauses combine. OR casts the widest net.</summary>
    public SearchOperator DefaultOperator { get; set; } = SearchOperator.Or;

    /// <summary>Minimum similarity for <see cref="FieldMatchMode.Fuzzy"/>, between 0 and 1.</summary>
    public float Fuzziness { get; set; } = 0.8f;

    /// <summary>Queries shorter than this return nothing, which stops one-letter queries scanning the index.</summary>
    public int MinimumQueryLength { get; set; } = 2;

    /// <summary>Results scoring below this are dropped. 0 disables the cut-off.</summary>
    public float MinimumScore { get; set; }

    /// <summary>Words stripped from the query before it is built.</summary>
    public IList<string> StopWords { get; set; } = new List<string>();

    /// <summary>
    /// Query-time term expansion: a search for the key also searches for each of its values.
    /// </summary>
    public IDictionary<string, IList<string>> Synonyms { get; set; } =
        new Dictionary<string, IList<string>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// When a query has several words, require every word to match somewhere in the document
    /// rather than just one of them.
    /// </summary>
    public bool AllTermsMustMatch { get; set; }
}
