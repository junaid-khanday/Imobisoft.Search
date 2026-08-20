using System.Text.Json.Serialization;

namespace Imobisoft.Search.Models;

[JsonConverter(typeof(CamelCaseEnumConverter<SortDirection>))]
public enum SortDirection
{
    Ascending,
    Descending,
}

/// <summary>One level of sorting. Several can be stacked in <see cref="RankingRules.SortBy"/>.</summary>
public sealed class SortRule
{
    /// <summary>
    /// The field to sort on. The reserved value <c>score</c> means relevance, which is the default
    /// and needs no sortable field in the index.
    /// </summary>
    public string Field { get; set; } = SortRule.ScoreField;

    public SortDirection Direction { get; set; } = SortDirection.Descending;

    public const string ScoreField = "score";
}

/// <summary>
/// A curated result: when someone searches for any of <see cref="Terms"/>, these nodes are pinned
/// to the top regardless of how they scored.
/// </summary>
public sealed class BestBet
{
    public IList<string> Terms { get; set; } = new List<string>();

    public IList<Guid> NodeKeys { get; set; } = new List<Guid>();
}

/// <summary>Pushes recently edited content up the results.</summary>
public sealed class RecencyBoost
{
    public bool Enabled { get; set; }

    /// <summary>Date field the boost reads, normally <c>updateDate</c> or <c>createDate</c>.</summary>
    public string Field { get; set; } = ImobisoftSearchConstants.IndexFields.UpdateDate;

    /// <summary>Age in days at which the boost has decayed to half its strength.</summary>
    public int HalfLifeDays { get; set; } = 90;

    /// <summary>How much the boost can lift a score, as a multiplier on top of 1.</summary>
    public float Weight { get; set; } = 0.5f;
}

/// <summary>
/// Defines the <em>order</em> results come back in, plus the editorial overrides that let a content
/// team force or suppress specific outcomes.
/// </summary>
public sealed class RankingRules
{
    /// <summary>
    /// Ordered sort levels. Empty means sort by relevance, which is the shipped default.
    /// </summary>
    public IList<SortRule> SortBy { get; set; } = new List<SortRule>();

    /// <summary>
    /// Per-document-type relevance multipliers, so e.g. news articles can outrank generic pages.
    /// </summary>
    public IDictionary<string, float> ContentTypeBoosts { get; set; } =
        new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

    public IList<BestBet> BestBets { get; set; } = new List<BestBet>();

    /// <summary>Terms that return an empty result set instead of running a query.</summary>
    public IList<string> BlockedTerms { get; set; } = new List<string>();

    public RecencyBoost Recency { get; set; } = new();
}
