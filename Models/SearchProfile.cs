namespace Imobisoft.Search.Models;

/// <summary>
/// The complete set of rules the search engine obeys. This is what gets serialised into the
/// <c>configJson</c> column of a profile row, and what the dashboard edits.
/// </summary>
public sealed class SearchRuleSet
{
    /// <summary>What is searchable.</summary>
    public SourceRules Sources { get; set; } = new();

    /// <summary>How a term matches.</summary>
    public MatchingRules Matching { get; set; } = new();

    /// <summary>What order results come back in.</summary>
    public RankingRules Ranking { get; set; } = new();

    /// <summary>The shape of the response.</summary>
    public ResultRules Results { get; set; } = new();
}

/// <summary>
/// A named, saveable search configuration. A site can hold several - for example a wide "site
/// search" profile and a narrow "knowledge base" profile - and exactly one is the default used
/// when no profile is named.
/// </summary>
public sealed class SearchProfile
{
    public Guid Key { get; set; }

    /// <summary>Stable, code-friendly identifier used when calling the search service.</summary>
    public string Alias { get; set; } = string.Empty;

    /// <summary>Human readable name shown in the backoffice.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The profile used when a search does not name one. Exactly one profile holds this.</summary>
    public bool IsDefault { get; set; }

    /// <summary>A disabled profile returns no results rather than throwing.</summary>
    public bool Enabled { get; set; } = true;

    public DateTime CreateDate { get; set; }

    public DateTime UpdateDate { get; set; }

    public SearchRuleSet Rules { get; set; } = new();

    /// <summary>
    /// The profile the package seeds on install: no restrictions anywhere, so every index and every
    /// document type is searched until an editor narrows it down.
    /// </summary>
    public static SearchProfile CreateDefault() => new()
    {
        Key = Guid.NewGuid(),
        Alias = ImobisoftSearchConstants.Profiles.DefaultAlias,
        Name = ImobisoftSearchConstants.Profiles.DefaultName,
        IsDefault = true,
        Enabled = true,
        CreateDate = DateTime.UtcNow,
        UpdateDate = DateTime.UtcNow,
        Rules = new SearchRuleSet(),
    };
}
