using Imobisoft.Search.Models;
using Imobisoft.Search.Services;
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

/// <summary>Site-wide package settings, as opposed to the per-profile search rules.</summary>
public sealed class SearchSettingsController : ImobisoftSearchControllerBase
{
    private readonly ISearchSettingsService _settingsService;

    public SearchSettingsController(ISearchSettingsService settingsService) => _settingsService = settingsService;

    [HttpGet("settings")]
    [ProducesResponseType(typeof(SearchSettings), StatusCodes.Status200OK)]
    public IActionResult Get() => Ok(_settingsService.Get());

    [HttpPut("settings")]
    [ProducesResponseType(typeof(SearchSettings), StatusCodes.Status200OK)]
    public IActionResult Update([FromBody] SearchSettings settings)
    {
        _settingsService.Save(settings);
        return Ok(_settingsService.Get());
    }
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
