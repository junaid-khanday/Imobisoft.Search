using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Sync;
using Umbraco.Cms.Infrastructure.BackgroundJobs;

namespace Imobisoft.Search.Services;

/// <summary>
/// Deletes recorded searches past the retention window.
/// <para>
/// Restricted to the scheduling server, so a load-balanced site runs the purge once rather than
/// once per front-end.
/// </para>
/// </summary>
public sealed class SearchAnalyticsRetentionJob : IRecurringBackgroundJob
{
    private readonly ISearchAnalyticsService _analytics;
    private readonly ILogger<SearchAnalyticsRetentionJob> _logger;

    public SearchAnalyticsRetentionJob(
        ISearchAnalyticsService analytics,
        ILogger<SearchAnalyticsRetentionJob> logger)
    {
        _analytics = analytics;
        _logger = logger;
    }

    public TimeSpan Period => TimeSpan.FromHours(24);

    /// <summary>Deliberately late, so a purge never competes with a site's start-up work.</summary>
    public TimeSpan Delay => TimeSpan.FromMinutes(10);

    public ServerRole[] ServerRoles => new[] { ServerRole.Single, ServerRole.SchedulingPublisher };

    /// <summary>Required by the interface; the period never changes for this job.</summary>
    public event EventHandler? PeriodChanged
    {
        add { }
        remove { }
    }

    public Task RunJobAsync()
    {
        try
        {
            var removed = _analytics.Purge();

            if (removed > 0)
            {
                _logger.LogInformation("Purged {Count} recorded searches past the retention window.", removed);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Imobisoft.Search could not purge old analytics.");
        }

        return Task.CompletedTask;
    }
}
