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
                // Without an explicit entity-type selection the engine treats the profile as
                // "nothing included yet", so the seeded profile names the two indexes every
                // Umbraco site ships with.
                IndexTypes =
                {
                    ImobisoftSearchConstants.IndexTypes.Content,
                    ImobisoftSearchConstants.IndexTypes.Media,
                },
            },
            Results = new ResultRules
            {
                Highlight = new HighlightRules { Enabled = true },

                // A search page with nothing above the box looks unfinished, so the seeded profile
                // ships the three filter dimensions every Umbraco site can answer without any
                // property being configured first. Because the filter bar is driven by these
                // DEFINITIONS rather than by counts, they put dropdowns above the search box from
                // the moment the page opens - before a word is typed. They are ordinary facets:
                // editors retune or delete them under Search > Filters like any other.
                Facets = DefaultFacets(),
                SortOptions = DefaultSortOptions(),
            },
        },
    };

    /// <summary>
    /// The filter dimensions the seeded profile starts with. Every field named here is written by
    /// Umbraco itself into both the content and media indexes, so the dropdowns have real values on
    /// a site that has configured nothing.
    /// <para>
    /// Shared with the migration that backfills them, so a site that installed the package before
    /// these existed ends up with exactly what a fresh install gets.
    /// </para>
    /// </summary>
    internal static IList<FacetDefinition> DefaultFacets() => new List<FacetDefinition>
    {
        new()
        {
            Alias = "contentType",
            Field = ImobisoftSearchConstants.IndexFields.NodeTypeAlias,
            Label = "Content type",
            Kind = FacetKind.Field,
            MaxValues = 20,
        },
        new()
        {
            Alias = "section",
            Field = ImobisoftSearchConstants.IndexFields.IndexType,
            Label = "Section",
            Kind = FacetKind.Field,
            MaxValues = 10,
        },
        new()
        {
            Alias = "updated",
            Field = ImobisoftSearchConstants.IndexFields.UpdateDate,
            Label = "Last updated",
            Kind = FacetKind.DateRange,

            // Relative bounds rather than fixed dates, so "last 7 days" still means the last seven
            // days a year after the site went live.
            Ranges =
            {
                new FacetRange { Alias = "last7", Label = "Last 7 days", From = "now-7d" },
                new FacetRange { Alias = "last30", Label = "Last 30 days", From = "now-30d" },
                new FacetRange { Alias = "last12m", Label = "Last 12 months", From = "now-12m" },
                new FacetRange { Alias = "older", Label = "Over a year ago", To = "now-12m" },
            },
        },
    };

    /// <summary>
    /// The choices the seeded profile offers in "Sort by". The relevance entry carries no field on
    /// purpose: an empty field is what tells the engine to leave the profile's own ranking alone,
    /// and it is the option the built-in views render as the dropdown's blank default.
    /// </summary>
    internal static IList<SortOption> DefaultSortOptions() => new List<SortOption>
    {
        new() { Alias = "relevance", Label = "Relevance", Field = string.Empty },
        new()
        {
            Alias = "nameAsc",
            Label = "Title A - Z",
            Field = ImobisoftSearchConstants.IndexFields.NodeName,
            Direction = SortDirection.Ascending,
        },
        new()
        {
            Alias = "nameDesc",
            Label = "Title Z - A",
            Field = ImobisoftSearchConstants.IndexFields.NodeName,
            Direction = SortDirection.Descending,
        },
        new()
        {
            Alias = "newest",
            Label = "Newest first",
            Field = ImobisoftSearchConstants.IndexFields.UpdateDate,
            Direction = SortDirection.Descending,
        },
        new()
        {
            Alias = "oldest",
            Label = "Oldest first",
            Field = ImobisoftSearchConstants.IndexFields.UpdateDate,
            Direction = SortDirection.Ascending,
        },
    };
}
