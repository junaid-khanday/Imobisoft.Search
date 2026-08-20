using Imobisoft.Search.Models;

namespace Imobisoft.Search.Services.Querying;

/// <summary>The query to run against one index, plus the fields it ended up searching.</summary>
internal sealed class IndexQueryPlan
{
    public required string IndexName { get; init; }

    /// <summary>The Lucene query string, ready to hand to Examine.</summary>
    public required string Query { get; init; }

    /// <summary>Fields this index actually searched, after culture expansion and availability checks.</summary>
    public required IReadOnlyList<string> Fields { get; init; }
}

/// <summary>
/// The fully resolved outcome of applying a rule set to a term: which indexes to hit with which
/// query, and which checks still have to happen once documents come back.
/// <para>
/// Filters split into two groups. Anything the index can answer reliably becomes part of the Lucene
/// query so the result window stays relevant. Anything whose indexed representation varies between
/// Umbraco versions and field types - hidden nodes, protected pages, subtree membership - is applied
/// in memory against the returned document, where the answer is unambiguous.
/// </para>
/// </summary>
internal sealed class SearchPlan
{
    /// <summary>
    /// True when no query should run at all: the term was blocked, too short, emptied by stop words,
    /// or the profile is disabled. Callers return an empty response rather than an error.
    /// </summary>
    public bool ShortCircuit { get; init; }

    /// <summary>The term after trimming, stop word removal and synonym expansion.</summary>
    public IReadOnlyList<IReadOnlyList<string>> TermGroups { get; init; } = Array.Empty<IReadOnlyList<string>>();

    public IReadOnlyList<IndexQueryPlan> Indexes { get; init; } = Array.Empty<IndexQueryPlan>();

    /// <summary>Umbraco node ids the results must sit under. Empty means no subtree restriction.</summary>
    public IReadOnlySet<int> RootNodeIds { get; init; } = new HashSet<int>();

    /// <summary>Umbraco node ids to drop from results.</summary>
    public IReadOnlySet<int> ExcludedNodeIds { get; init; } = new HashSet<int>();

    /// <summary>Whether an excluded node also removes everything beneath it.</summary>
    public bool ExcludeDescendants { get; init; }

    /// <summary>How many documents to pull before post-processing.</summary>
    public int FetchSize { get; init; }

    /// <summary>Human readable record of which rules fired, surfaced in the dashboard's test panel.</summary>
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();

    /// <summary>The rules this plan came from, carried through so post-processing can read them.</summary>
    public required SearchRuleSet Rules { get; init; }

    public static SearchPlan Blocked(SearchRuleSet rules, string reason) => new()
    {
        ShortCircuit = true,
        Rules = rules,
        Notes = new[] { reason },
    };
}
