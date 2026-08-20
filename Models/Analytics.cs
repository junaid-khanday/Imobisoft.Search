namespace Imobisoft.Search.Models;

/// <summary>One recorded search.</summary>
public sealed class SearchQueryRecord
{
    /// <summary>
    /// Identifies this search to the site, so a click can be attributed back to the query that
    /// produced it.
    /// </summary>
    public Guid Key { get; set; }

    public string ProfileAlias { get; set; } = string.Empty;

    /// <summary>What the visitor typed, as typed.</summary>
    public string Term { get; set; } = string.Empty;

    /// <summary>Lower-cased and whitespace-collapsed, so "  Blue  Widget" and "blue widget" group together.</summary>
    public string NormalizedTerm { get; set; } = string.Empty;

    public int ResultCount { get; set; }

    public int DurationMilliseconds { get; set; }

    public string? Culture { get; set; }

    public DateTime CreateDate { get; set; }
}

/// <summary>A result a visitor opened from a search.</summary>
public sealed class SearchClickRecord
{
    public Guid QueryKey { get; set; }

    public Guid NodeKey { get; set; }

    public string NodeName { get; set; } = string.Empty;

    /// <summary>1-based position in the result list, so the reports can show whether ranking is working.</summary>
    public int Position { get; set; }

    public DateTime CreateDate { get; set; }
}

/// <summary>One row in a "what are people searching for" report.</summary>
public sealed class TermReportRow
{
    public string Term { get; set; } = string.Empty;

    public int SearchCount { get; set; }

    /// <summary>Average number of results, which is what makes a poorly performing term obvious.</summary>
    public double AverageResultCount { get; set; }

    public int ZeroResultCount { get; set; }

    public int ClickCount { get; set; }

    /// <summary>Share of searches for this term where the visitor opened something.</summary>
    public double ClickThroughRate => SearchCount == 0 ? 0 : (double)ClickCount / SearchCount;

    /// <summary>Average position of what was clicked. A high number means the right answer ranks too low.</summary>
    public double AverageClickPosition { get; set; }

    public DateTime LastSearched { get; set; }
}

/// <summary>Headline numbers for a period.</summary>
public sealed class AnalyticsSummary
{
    public int TotalSearches { get; set; }

    public int UniqueTerms { get; set; }

    public int ZeroResultSearches { get; set; }

    public double ZeroResultRate => TotalSearches == 0 ? 0 : (double)ZeroResultSearches / TotalSearches;

    public int TotalClicks { get; set; }

    public double ClickThroughRate => TotalSearches == 0 ? 0 : (double)TotalClicks / TotalSearches;

    public double AverageDurationMilliseconds { get; set; }

    public double AverageResultCount { get; set; }
}

/// <summary>Searches per day, for the trend chart.</summary>
public sealed class DailyVolume
{
    public DateOnly Date { get; set; }

    public int SearchCount { get; set; }

    public int ZeroResultCount { get; set; }
}

/// <summary>Everything the insights dashboard renders, in one call.</summary>
public sealed class AnalyticsReport
{
    public DateTime From { get; set; }

    public DateTime To { get; set; }

    public AnalyticsSummary Summary { get; set; } = new();

    /// <summary>Most searched terms.</summary>
    public IList<TermReportRow> TopTerms { get; set; } = new List<TermReportRow>();

    /// <summary>Terms that found nothing — the most actionable list in the package.</summary>
    public IList<TermReportRow> ZeroResultTerms { get; set; } = new List<TermReportRow>();

    /// <summary>Terms people search but never click a result for.</summary>
    public IList<TermReportRow> UnclickedTerms { get; set; } = new List<TermReportRow>();

    public IList<DailyVolume> Volume { get; set; } = new List<DailyVolume>();
}

/// <summary>A type-ahead or "did you mean" suggestion.</summary>
public sealed class SearchSuggestion
{
    /// <summary>The text to show and to search for.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Set when the suggestion points at one specific page.</summary>
    public Guid? NodeKey { get; set; }

    public string? Url { get; set; }

    public string? ContentTypeAlias { get; set; }

    public float Score { get; set; }
}
