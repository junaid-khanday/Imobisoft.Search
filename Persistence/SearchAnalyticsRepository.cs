using Imobisoft.Search.Models;
using Microsoft.Extensions.Logging;
using NPoco;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Extensions;

namespace Imobisoft.Search.Persistence;

/// <summary>Reads and writes the analytics tables.</summary>
public interface ISearchAnalyticsRepository
{
    /// <summary>Inserts a batch of recorded searches in one transaction.</summary>
    void InsertQueries(IReadOnlyCollection<SearchQueryRecord> records);

    void InsertClick(SearchClickRecord record);

    AnalyticsSummary GetSummary(DateTime from, DateTime to);

    /// <summary>Terms ordered by how often they were searched.</summary>
    IReadOnlyList<TermReportRow> GetTopTerms(DateTime from, DateTime to, int take);

    /// <summary>Terms whose searches found nothing, ordered by how much traffic they lost.</summary>
    IReadOnlyList<TermReportRow> GetZeroResultTerms(DateTime from, DateTime to, int take);

    /// <summary>Terms that returned results but which nobody clicked, ordered by volume.</summary>
    IReadOnlyList<TermReportRow> GetUnclickedTerms(DateTime from, DateTime to, int take);

    IReadOnlyList<DailyVolume> GetDailyVolume(DateTime from, DateTime to);

    /// <summary>Deletes recorded searches older than the cut-off, and their clicks. Returns rows removed.</summary>
    int Purge(DateTime olderThan);

    bool TablesExist();
}

/// <inheritdoc />
public sealed class SearchAnalyticsRepository : ISearchAnalyticsRepository
{
    private readonly IScopeProvider _scopeProvider;
    private readonly ILogger<SearchAnalyticsRepository> _logger;

    public SearchAnalyticsRepository(IScopeProvider scopeProvider, ILogger<SearchAnalyticsRepository> logger)
    {
        _scopeProvider = scopeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public void InsertQueries(IReadOnlyCollection<SearchQueryRecord> records)
    {
        if (records.Count == 0)
        {
            return;
        }

        using IScope scope = _scopeProvider.CreateScope();

        foreach (SearchQueryRecord record in records)
        {
            scope.Database.Insert(new SearchQuerySchema
            {
                UniqueId = record.Key,
                ProfileAlias = Truncate(record.ProfileAlias, 255),
                Term = Truncate(record.Term, 500),
                NormalizedTerm = Truncate(record.NormalizedTerm, 500),
                ResultCount = record.ResultCount,
                DurationMs = record.DurationMilliseconds,
                Culture = Truncate(record.Culture, 20),
                CreateDate = record.CreateDate,
            });
        }

        scope.Complete();
    }

    /// <inheritdoc />
    public void InsertClick(SearchClickRecord record)
    {
        using IScope scope = _scopeProvider.CreateScope();

        scope.Database.Insert(new SearchClickSchema
        {
            QueryUniqueId = record.QueryKey,
            NodeKey = record.NodeKey,
            NodeName = Truncate(record.NodeName, 255) ?? string.Empty,
            Position = record.Position,
            CreateDate = record.CreateDate,
        });

        scope.Complete();
    }

    /// <inheritdoc />
    public AnalyticsSummary GetSummary(DateTime from, DateTime to)
    {
        using IScope scope = _scopeProvider.CreateScope(autoComplete: true);

        var q = QuotedQuery(scope);

        SummaryRow? row = scope.Database.FirstOrDefault<SummaryRow>(
            $@"SELECT COUNT(*) AS TotalSearches,
                      COUNT(DISTINCT {q.NormalizedTerm}) AS UniqueTerms,
                      SUM(CASE WHEN {q.ResultCount} = 0 THEN 1 ELSE 0 END) AS ZeroResultSearches,
                      AVG(CAST({q.DurationMs} AS FLOAT)) AS AverageDurationMilliseconds,
                      AVG(CAST({q.ResultCount} AS FLOAT)) AS AverageResultCount
               FROM {q.QueryTable}
               WHERE {q.CreateDate} >= @0 AND {q.CreateDate} < @1",
            from,
            to);

        var clicks = scope.Database.ExecuteScalar<int>(
            $@"SELECT COUNT(*) FROM {q.ClickTable} WHERE {q.ClickCreateDate} >= @0 AND {q.ClickCreateDate} < @1",
            from,
            to);

        return new AnalyticsSummary
        {
            TotalSearches = row?.TotalSearches ?? 0,
            UniqueTerms = row?.UniqueTerms ?? 0,
            ZeroResultSearches = row?.ZeroResultSearches ?? 0,
            AverageDurationMilliseconds = row?.AverageDurationMilliseconds ?? 0,
            AverageResultCount = row?.AverageResultCount ?? 0,
            TotalClicks = clicks,
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<TermReportRow> GetTopTerms(DateTime from, DateTime to, int take)
        => QueryTerms(from, to, take, havingZeroResultsOnly: false, unclickedOnly: false);

    /// <inheritdoc />
    public IReadOnlyList<TermReportRow> GetZeroResultTerms(DateTime from, DateTime to, int take)
        => QueryTerms(from, to, take, havingZeroResultsOnly: true, unclickedOnly: false);

    /// <inheritdoc />
    public IReadOnlyList<TermReportRow> GetUnclickedTerms(DateTime from, DateTime to, int take)
        => QueryTerms(from, to, take, havingZeroResultsOnly: false, unclickedOnly: true);

    /// <summary>
    /// One shape of query drives all three term reports. Clicks are joined through a sub-select
    /// rather than an outer join so that a term with many clicks does not multiply its search count.
    /// </summary>
    private IReadOnlyList<TermReportRow> QueryTerms(
        DateTime from,
        DateTime to,
        int take,
        bool havingZeroResultsOnly,
        bool unclickedOnly)
    {
        using IScope scope = _scopeProvider.CreateScope(autoComplete: true);
        var q = QuotedQuery(scope);

        var having = havingZeroResultsOnly
            ? $"HAVING SUM(CASE WHEN s.{q.ResultCount} = 0 THEN 1 ELSE 0 END) > 0"
            : string.Empty;

        var sql =
            $@"SELECT s.{q.NormalizedTerm} AS Term,
                      COUNT(*) AS SearchCount,
                      AVG(CAST(s.{q.ResultCount} AS FLOAT)) AS AverageResultCount,
                      SUM(CASE WHEN s.{q.ResultCount} = 0 THEN 1 ELSE 0 END) AS ZeroResultCount,
                      MAX(s.{q.CreateDate}) AS LastSearched,
                      COALESCE(SUM(c.ClickCount), 0) AS ClickCount,
                      AVG(c.AveragePosition) AS AverageClickPosition
               FROM {q.QueryTable} s
               LEFT JOIN (
                   SELECT {q.ClickQueryId} AS QueryId,
                          COUNT(*) AS ClickCount,
                          AVG(CAST({q.ClickPosition} AS FLOAT)) AS AveragePosition
                   FROM {q.ClickTable}
                   GROUP BY {q.ClickQueryId}
               ) c ON c.QueryId = s.{q.UniqueId}
               WHERE s.{q.CreateDate} >= @0 AND s.{q.CreateDate} < @1
               GROUP BY s.{q.NormalizedTerm}
               {having}
               ORDER BY COUNT(*) DESC";

        List<TermRow> rows = scope.Database.Fetch<TermRow>(sql, from, to);

        IEnumerable<TermRow> filtered = unclickedOnly
            ? rows.Where(r => r.ClickCount == 0 && r.ZeroResultCount < r.SearchCount)
            : rows;

        return filtered
            .Take(Math.Max(1, take))
            .Select(r => new TermReportRow
            {
                Term = r.Term ?? string.Empty,
                SearchCount = r.SearchCount,
                AverageResultCount = r.AverageResultCount,
                ZeroResultCount = r.ZeroResultCount,
                ClickCount = r.ClickCount,
                AverageClickPosition = r.AverageClickPosition ?? 0,
                LastSearched = r.LastSearched,
            })
            .ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<DailyVolume> GetDailyVolume(DateTime from, DateTime to)
    {
        using IScope scope = _scopeProvider.CreateScope(autoComplete: true);
        var q = QuotedQuery(scope);

        // Grouped in memory rather than with a database-specific date-truncation function, which
        // differs between SQL Server and SQLite. The row count is bounded by the reporting window.
        List<VolumeRow> rows = scope.Database.Fetch<VolumeRow>(
            $@"SELECT {q.CreateDate} AS CreateDate, {q.ResultCount} AS ResultCount
               FROM {q.QueryTable}
               WHERE {q.CreateDate} >= @0 AND {q.CreateDate} < @1",
            from,
            to);

        return rows
            .GroupBy(r => DateOnly.FromDateTime(r.CreateDate))
            .OrderBy(g => g.Key)
            .Select(g => new DailyVolume
            {
                Date = g.Key,
                SearchCount = g.Count(),
                ZeroResultCount = g.Count(r => r.ResultCount == 0),
            })
            .ToList();
    }

    /// <inheritdoc />
    public int Purge(DateTime olderThan)
    {
        using IScope scope = _scopeProvider.CreateScope();
        var q = QuotedQuery(scope);

        // Clicks first, so a click can never be orphaned by a partially applied purge.
        scope.Database.Execute(
            $@"DELETE FROM {q.ClickTable}
               WHERE {q.ClickQueryId} IN (
                   SELECT {q.UniqueId} FROM {q.QueryTable} WHERE {q.CreateDate} < @0
               )",
            olderThan);

        var removed = scope.Database.Execute(
            $"DELETE FROM {q.QueryTable} WHERE {q.CreateDate} < @0",
            olderThan);

        scope.Complete();
        return removed;
    }

    /// <inheritdoc />
    public bool TablesExist()
    {
        try
        {
            using IScope scope = _scopeProvider.CreateScope(autoComplete: true);

            return scope.SqlContext.SqlSyntax.DoesTableExist(scope.Database, ImobisoftSearchConstants.Database.QueryTableName)
                   && scope.SqlContext.SqlSyntax.DoesTableExist(scope.Database, ImobisoftSearchConstants.Database.ClickTableName);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not determine whether the analytics tables exist.");
            return false;
        }
    }

    /// <summary>
    /// Table and column names quoted for whichever database the site runs on, so the same SQL works
    /// on SQL Server and SQLite alike.
    /// </summary>
    private static QuotedNames QuotedQuery(IScope scope)
    {
        var syntax = scope.SqlContext.SqlSyntax;

        return new QuotedNames(
            syntax.GetQuotedTableName(ImobisoftSearchConstants.Database.QueryTableName),
            syntax.GetQuotedTableName(ImobisoftSearchConstants.Database.ClickTableName),
            syntax.GetQuotedColumnName("normalizedTerm"),
            syntax.GetQuotedColumnName("resultCount"),
            syntax.GetQuotedColumnName("durationMs"),
            syntax.GetQuotedColumnName("createDate"),
            syntax.GetQuotedColumnName("uniqueId"),
            syntax.GetQuotedColumnName("queryUniqueId"),
            syntax.GetQuotedColumnName("position"));
    }

    private sealed record QuotedNames(
        string QueryTable,
        string ClickTable,
        string NormalizedTerm,
        string ResultCount,
        string DurationMs,
        string CreateDate,
        string UniqueId,
        string ClickQueryId,
        string ClickPosition)
    {
        /// <summary>Both tables name their timestamp column the same way.</summary>
        public string ClickCreateDate => CreateDate;
    }

    private static string? Truncate(string? value, int max)
        => value is null || value.Length <= max ? value : value[..max];

    private sealed class SummaryRow
    {
        public int TotalSearches { get; set; }

        public int UniqueTerms { get; set; }

        public int ZeroResultSearches { get; set; }

        public double AverageDurationMilliseconds { get; set; }

        public double AverageResultCount { get; set; }
    }

    private sealed class TermRow
    {
        public string? Term { get; set; }

        public int SearchCount { get; set; }

        public double AverageResultCount { get; set; }

        public int ZeroResultCount { get; set; }

        public int ClickCount { get; set; }

        public double? AverageClickPosition { get; set; }

        public DateTime LastSearched { get; set; }
    }

    private sealed class VolumeRow
    {
        public DateTime CreateDate { get; set; }

        public int ResultCount { get; set; }
    }
}
