using System.Text.RegularExpressions;
using System.Threading.Channels;
using Imobisoft.Search.Models;
using Imobisoft.Search.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Imobisoft.Search.Services;

/// <summary>
/// Records what visitors searched for and what they opened, and reports on it.
/// <para>
/// Recording never blocks a search: <see cref="Record"/> hands the row to a background writer and
/// returns. If the site is searching faster than the writer can keep up, rows are dropped rather
/// than queued without limit - losing analytics is always preferable to slowing down search.
/// </para>
/// </summary>
public interface ISearchAnalyticsService
{
    /// <summary>Queues a completed search for recording. Returns immediately.</summary>
    void Record(SearchResponse response, string? culture, int durationMilliseconds);

    /// <summary>
    /// Records that a visitor opened a result. <paramref name="queryKey"/> comes from
    /// <see cref="SearchResponse.QueryKey"/> of the search that produced it.
    /// </summary>
    void RecordClick(Guid queryKey, Guid nodeKey, string nodeName, int position);

    AnalyticsReport GetReport(DateTime from, DateTime to, int take = 25);

    /// <summary>Deletes anything past the retention window. Returns rows removed.</summary>
    int Purge();
}

/// <inheritdoc />
public sealed partial class SearchAnalyticsService : ISearchAnalyticsService
{
    /// <summary>
    /// Bounded so a traffic spike cannot grow the queue without limit. Sized well above any
    /// realistic burst between two flushes.
    /// </summary>
    internal const int QueueCapacity = 10_000;

    private readonly Channel<object> _queue = Channel.CreateBounded<object>(
        new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });

    private readonly ISearchAnalyticsRepository _repository;
    private readonly ISearchSettingsService _settingsService;
    private readonly ILogger<SearchAnalyticsService> _logger;

    public SearchAnalyticsService(
        ISearchAnalyticsRepository repository,
        ISearchSettingsService settingsService,
        ILogger<SearchAnalyticsService> logger)
    {
        _repository = repository;
        _settingsService = settingsService;
        _logger = logger;
    }

    /// <summary>The queue the background writer drains.</summary>
    internal ChannelReader<object> Reader => _queue.Reader;

    /// <inheritdoc />
    public void Record(SearchResponse response, string? culture, int durationMilliseconds)
    {
        AnalyticsSettings settings = _settingsService.Get().Analytics;

        if (!settings.Enabled)
        {
            return;
        }

        var term = (response.Term ?? string.Empty).Trim();

        if (term.Length < Math.Max(1, settings.MinimumTermLength))
        {
            return;
        }

        if (settings.RecordZeroResultsOnly && response.TotalResults > 0)
        {
            return;
        }

        var record = new SearchQueryRecord
        {
            Key = response.QueryKey,
            ProfileAlias = response.ProfileAlias,
            Term = term,
            NormalizedTerm = Normalize(term),
            ResultCount = response.TotalResults,
            DurationMilliseconds = durationMilliseconds,
            Culture = culture,
            CreateDate = DateTime.UtcNow,
        };

        if (!_queue.Writer.TryWrite(record))
        {
            _logger.LogDebug("Search analytics queue is full; dropped a recorded search.");
        }
    }

    /// <inheritdoc />
    public void RecordClick(Guid queryKey, Guid nodeKey, string nodeName, int position)
    {
        AnalyticsSettings settings = _settingsService.Get().Analytics;

        if (!settings.Enabled || !settings.TrackClicks || queryKey == Guid.Empty)
        {
            return;
        }

        var record = new SearchClickRecord
        {
            QueryKey = queryKey,
            NodeKey = nodeKey,
            NodeName = nodeName,
            Position = position,
            CreateDate = DateTime.UtcNow,
        };

        if (!_queue.Writer.TryWrite(record))
        {
            _logger.LogDebug("Search analytics queue is full; dropped a recorded click.");
        }
    }

    /// <inheritdoc />
    public AnalyticsReport GetReport(DateTime from, DateTime to, int take = 25)
    {
        if (!_repository.TablesExist())
        {
            return new AnalyticsReport { From = from, To = to };
        }

        return new AnalyticsReport
        {
            From = from,
            To = to,
            Summary = _repository.GetSummary(from, to),
            TopTerms = _repository.GetTopTerms(from, to, take).ToList(),
            ZeroResultTerms = _repository.GetZeroResultTerms(from, to, take).ToList(),
            UnclickedTerms = _repository.GetUnclickedTerms(from, to, take).ToList(),
            Volume = _repository.GetDailyVolume(from, to).ToList(),
        };
    }

    /// <inheritdoc />
    public int Purge()
    {
        var retentionDays = _settingsService.Get().Analytics.RetentionDays;

        if (retentionDays <= 0 || !_repository.TablesExist())
        {
            return 0;
        }

        return _repository.Purge(DateTime.UtcNow.AddDays(-retentionDays));
    }

    /// <summary>
    /// Collapses case and runs of whitespace so that "  Blue   Widget " and "blue widget" report as
    /// the same search.
    /// </summary>
    internal static string Normalize(string term)
        => WhitespaceRuns().Replace(term.Trim().ToLowerInvariant(), " ");

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRuns();
}

/// <summary>
/// Drains the analytics queue and writes it in batches.
/// <para>
/// Batching matters: a busy site can produce hundreds of searches between flushes, and one
/// transaction per batch keeps analytics off the critical path and off the database's back.
/// </para>
/// </summary>
public sealed class SearchAnalyticsWriter : BackgroundService
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(5);
    private const int MaximumBatchSize = 200;

    private readonly SearchAnalyticsService _analytics;
    private readonly ISearchAnalyticsRepository _repository;
    private readonly ILogger<SearchAnalyticsWriter> _logger;

    public SearchAnalyticsWriter(
        ISearchAnalyticsService analytics,
        ISearchAnalyticsRepository repository,
        ILogger<SearchAnalyticsWriter> logger)
    {
        _analytics = (SearchAnalyticsService)analytics;
        _repository = repository;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var queries = new List<SearchQueryRecord>(MaximumBatchSize);
        var clicks = new List<SearchClickRecord>(MaximumBatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(FlushInterval, stoppingToken);

                while (queries.Count + clicks.Count < MaximumBatchSize
                       && _analytics.Reader.TryRead(out var item))
                {
                    switch (item)
                    {
                        case SearchQueryRecord query:
                            queries.Add(query);
                            break;
                        case SearchClickRecord click:
                            clicks.Add(click);
                            break;
                    }
                }

                Flush(queries, clicks);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // The writer must survive anything - a database blip should cost analytics, not the
                // background service.
                _logger.LogWarning(ex, "Imobisoft.Search could not write analytics; the batch was dropped.");
                queries.Clear();
                clicks.Clear();
            }
        }

        // Best effort on shutdown, so a restart does not lose the last few seconds.
        try
        {
            while (_analytics.Reader.TryRead(out var item))
            {
                switch (item)
                {
                    case SearchQueryRecord query:
                        queries.Add(query);
                        break;
                    case SearchClickRecord click:
                        clicks.Add(click);
                        break;
                }
            }

            Flush(queries, clicks);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not flush the final analytics batch on shutdown.");
        }
    }

    private void Flush(List<SearchQueryRecord> queries, List<SearchClickRecord> clicks)
    {
        if (queries.Count == 0 && clicks.Count == 0)
        {
            return;
        }

        // Nothing to write into until the migration has run - on a very early first boot the queue
        // simply waits.
        if (!_repository.TablesExist())
        {
            return;
        }

        if (queries.Count > 0)
        {
            _repository.InsertQueries(queries);
            queries.Clear();
        }

        foreach (SearchClickRecord click in clicks)
        {
            _repository.InsertClick(click);
        }

        clicks.Clear();
    }
}
