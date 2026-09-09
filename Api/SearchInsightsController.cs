using Imobisoft.Search.Models;
using Imobisoft.Search.Services;
using Imobisoft.Search.Services.Ai;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Imobisoft.Search.Api;

/// <summary>Reports on what visitors have actually been searching for.</summary>
public sealed class SearchInsightsController : ImobisoftSearchControllerBase
{
    /// <summary>Caps the reporting window, so a stray request cannot ask for a decade of rows.</summary>
    private const int MaximumWindowDays = 730;

    private readonly ISearchAnalyticsService _analytics;

    public SearchInsightsController(ISearchAnalyticsService analytics) => _analytics = analytics;

    /// <summary>
    /// Headline numbers, the most searched terms, the terms that found nothing, the terms nobody
    /// clicks, and daily volume - everything the insights dashboard renders.
    /// </summary>
    /// <param name="days">Size of the reporting window, counting back from now.</param>
    /// <param name="take">How many rows in each of the term tables.</param>
    [HttpGet("insights")]
    [ProducesResponseType(typeof(AnalyticsReport), StatusCodes.Status200OK)]
    public IActionResult GetReport([FromQuery] int days = 30, [FromQuery] int take = 25)
    {
        var window = Math.Clamp(days, 1, MaximumWindowDays);
        DateTime to = DateTime.UtcNow.AddDays(1).Date;
        DateTime from = to.AddDays(-window);

        return Ok(_analytics.GetReport(from, to, Math.Clamp(take, 1, 200)));
    }

    /// <summary>Deletes recorded searches past the retention window, without waiting for the nightly job.</summary>
    [HttpPost("insights/purge")]
    [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
    public IActionResult Purge() => Ok(_analytics.Purge());
}

/// <summary>What the dashboard posts to check an AI credential before relying on it.</summary>
public sealed class AiConnectionTestRequest
{
    /// <summary>
    /// Key to test. Left empty, the saved one is tested - which is the only way to verify a
    /// credential the dashboard deliberately cannot read back.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Model to test. Empty uses the saved one.</summary>
    public string? Model { get; set; }
}

/// <summary>Site-wide package settings, as opposed to the per-profile search rules.</summary>
public sealed class SearchSettingsController : ImobisoftSearchControllerBase
{
    /// <summary>
    /// Stands in for a stored API key on the way out, and means "leave it alone" on the way back in.
    /// A real key is never sent to the browser, so this is what the dashboard round-trips instead.
    /// </summary>
    internal const string ApiKeyMask = "••••••••••••";

    private readonly ISearchSettingsService _settingsService;
    private readonly IAiSearchService _ai;

    public SearchSettingsController(ISearchSettingsService settingsService, IAiSearchService ai)
    {
        _settingsService = settingsService;
        _ai = ai;
    }

    [HttpGet("settings")]
    [ProducesResponseType(typeof(SearchSettings), StatusCodes.Status200OK)]
    public IActionResult Get() => Ok(Redacted(_settingsService.Get()));

    [HttpPut("settings")]
    [ProducesResponseType(typeof(SearchSettings), StatusCodes.Status200OK)]
    public IActionResult Update([FromBody] SearchSettings settings)
    {
        SearchSettings stored = _settingsService.Get();

        // The dashboard never held the real key, so an unchanged field arrives as the mask. Writing
        // that through would overwrite a working credential with a row of dots on every settings
        // save - including saves that had nothing to do with AI.
        settings.Ai.ApiKey = settings.Ai.ApiKey == ApiKeyMask
            ? stored.Ai.ApiKey
            : (settings.Ai.ApiKey ?? string.Empty).Trim();

        _settingsService.Save(settings);

        return Ok(Redacted(_settingsService.Get()));
    }

    /// <summary>
    /// Sends one trivial request to the provider so an editor finds out here that a key is wrong,
    /// rather than from a search page that quietly stopped using AI.
    /// </summary>
    [HttpPost("settings/ai/test")]
    [ProducesResponseType(typeof(AiConnectionResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> TestAi(
        [FromBody] AiConnectionTestRequest request,
        CancellationToken cancellationToken)
    {
        var key = request.ApiKey == ApiKeyMask ? null : request.ApiKey;

        return Ok(await _ai.TestConnectionAsync(key, request.Model, cancellationToken));
    }

    /// <summary>
    /// Replaces a stored key with the mask. Applied to a copy of the settings object rather than the
    /// stored one, because the settings service hands out a cached instance - redacting in place
    /// would wipe the key for the whole application until the cache expired.
    /// </summary>
    private static SearchSettings Redacted(SearchSettings settings) => new()
    {
        Analytics = settings.Analytics,
        Suggestions = settings.Suggestions,
        Ai = new AiSettings
        {
            Enabled = settings.Ai.Enabled,
            Provider = settings.Ai.Provider,
            ApiKey = string.IsNullOrWhiteSpace(settings.Ai.ApiKey) ? string.Empty : ApiKeyMask,
            Model = settings.Ai.Model,
            BaseUrl = settings.Ai.BaseUrl,
            TimeoutSeconds = settings.Ai.TimeoutSeconds,
            CacheMinutes = settings.Ai.CacheMinutes,
            MaxRequestsPerMinute = settings.Ai.MaxRequestsPerMinute,
            Answer = settings.Ai.Answer,
            QueryUnderstanding = settings.Ai.QueryUnderstanding,
            Rerank = settings.Ai.Rerank,
        },
    };
}

/// <summary>Type-ahead, so the dashboard's test panel can exercise it the way a site would.</summary>
public sealed class SearchAutocompleteController : ImobisoftSearchControllerBase
{
    private readonly IImobisoftSearchService _search;

    public SearchAutocompleteController(IImobisoftSearchService search) => _search = search;

    [HttpGet("autocomplete")]
    [ProducesResponseType(typeof(IEnumerable<SearchSuggestion>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Autocomplete(
        [FromQuery] string term,
        [FromQuery] string? profileAlias = null,
        [FromQuery] int take = 0,
        CancellationToken cancellationToken = default)
        => Ok(await _search.AutocompleteAsync(term, profileAlias, take, cancellationToken));
}
