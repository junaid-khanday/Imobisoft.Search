namespace Imobisoft.Search.Models;

/// <summary>
/// The complete set of rules the search engine obeys. This is what gets serialised into the
/// <c>configJson</c> column of a profile row, and what the dashboard edits.
/// </summary>
public sealed class SearchRuleSet
{
    /// <summary>
    /// How a visitor starts a search: the box the package puts on the site's own search link.
    /// First in the set because it is first in the journey - and first on the profile editor's
    /// tabs, ahead of Source.
    /// </summary>
    public SearchBoxRules SearchBox { get; set; } = new();

    /// <summary>What is searchable.</summary>
    public SourceRules Sources { get; set; } = new();

    /// <summary>How a term matches.</summary>
    public MatchingRules Matching { get; set; } = new();

    /// <summary>What order results come back in.</summary>
    public RankingRules Ranking { get; set; } = new();

    /// <summary>The shape of the response.</summary>
    public ResultRules Results { get; set; } = new();

    /// <summary>
    /// The live text tools offered on a result - summarise, shorten, key points, translate. Off
    /// until a key is added in the backoffice.
    /// </summary>
    public AiRules Ai { get; set; } = new();
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
    /// The profile the package seeds on install: both published content and media indexes are in
    /// scope and nothing is narrowed down further, so a fresh site searches everything the moment
    /// the package lands - editors then narrow it from the dashboard at their own pace.
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
        Rules = new SearchRuleSet
        {
            Sources = new SourceRules
            {
                // The two index types every Umbraco site ships with, so the Sources tab opens on
                // something rather than on nothing.
                //
                // This profile still returns no results until document types are included: that is
                // what including means, and it is the one place the package asks for a decision
                // before it will do anything. The alternative - searching every document type on
                // the site by default - puts settings nodes and internal pages in front of
                // visitors on day one, which is not a default anyone should inherit silently.
                IndexTypes =
                {
                    ImobisoftSearchConstants.IndexTypes.Content,
                    ImobisoftSearchConstants.IndexTypes.Media,
                },
            },
            Results = new ResultRules { Highlight = new HighlightRules { Enabled = true } },
        },
    };
}
