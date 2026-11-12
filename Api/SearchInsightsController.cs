using Imobisoft.Search.Configuration;
using Imobisoft.Search.Models;
using Imobisoft.Search.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

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
    private readonly IOptions<ImobisoftSearchOptions> _options;

    public SearchSettingsController(
        ISearchSettingsService settingsService,
        IOptions<ImobisoftSearchOptions> options)
    {
        _settingsService = settingsService;
        _options = options;
    }

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

    /// <summary>
    /// The deployment switches, as they are actually in effect.
    /// <para>
    /// Read-only on purpose, and worth having anyway. These live in <c>appsettings.json</c> so that
    /// a developer decides them and an editor cannot switch the package off for the whole site or
    /// open a paid AI endpoint by accident. But being unchangeable in the backoffice had turned
    /// into being <em>invisible</em> there: with the AI features not appearing on a site, nothing
    /// on any screen said whether the feature was off or the deployment switch was. A value you
    /// cannot edit is still a value you need to be able to read.
    /// </para>
    /// </summary>
    [HttpGet("settings/deployment")]
    [ProducesResponseType(typeof(DeploymentConfigurationResult), StatusCodes.Status200OK)]
    public IActionResult Deployment()
    {
        ImobisoftSearchOptions o = _options.Value;

        return Ok(new DeploymentConfigurationResult
        {
            Section = ImobisoftSearchOptions.SectionName,
            Values = new List<DeploymentSetting>
            {
                new("Enabled", o.Enabled, "The whole package. Off returns no results anywhere."),
                new("EnableSearchPage", o.EnableSearchPage, "Serves the built-in search page."),
                new("EnableHeaderSearchBox", o.EnableHeaderSearchBox, "Injects the header search box."),
                new("TakeOverSiteSearch", o.TakeOverSiteSearch, "Answers the site's own search service."),
                new("TakeOverSiteSearchPage", o.TakeOverSiteSearchPage, "Answers the site's own search page."),
                new("SiteSearchInterfaceName", o.SiteSearchInterfaceName, "Interface to take over, when not a usual name."),
                new("EnableAiTextTools", o.EnableAiTextTools, "Allows the AI buttons on results."),
                new("EnableAiSearch", o.EnableAiSearch, "Allows AI inside the search itself."),
                new("EnableContentExtraction", o.EnableContentExtraction, "Writes the clean extractedContent field."),
                new("ContentExtractionMinimumWords", o.ContentExtractionMinimumWords, "Words before text counts as body copy. 0 = default."),
                new("ContentExtractionMaxCharacters", o.ContentExtractionMaxCharacters, "Extracted text per page. 0 = default."),
                new("ReadFiltersFromQueryString", o.ReadFiltersFromQueryString, "Reads filter selections off the URL."),
                new("FilterQueryPrefix", o.FilterQueryPrefix, "Prefix marking a filter in the URL."),
                new("AddResponseHeader", o.AddResponseHeader, "Adds X-Imobisoft-Search to responses."),
            },
        });
    }
}

/// <summary>One deployment switch and its effective value.</summary>
public sealed class DeploymentSetting
{
    public DeploymentSetting(string name, object? value, string summary)
    {
        Name = name;
        Value = value?.ToString() ?? string.Empty;
        IsOn = value is not bool flag || flag;
        IsFlag = value is bool;
        Summary = summary;
    }

    public string Name { get; }

    public string Value { get; }

    /// <summary>For a switch, whether it is on. Always true for a value that is not a switch.</summary>
    public bool IsOn { get; }

    public bool IsFlag { get; }

    public string Summary { get; }
}

/// <summary>The deployment switches plus where to change them.</summary>
public sealed class DeploymentConfigurationResult
{
    /// <summary>The appsettings section these are bound from.</summary>
    public string Section { get; set; } = string.Empty;

    public IList<DeploymentSetting> Values { get; set; } = new List<DeploymentSetting>();
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
    {
        try
        {
            return Ok(await _search.AutocompleteAsync(term, profileAlias, take, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Type-ahead drops its request as soon as the next letter is typed, so a cancelled
            // search is routine here and must not be reported as a fault. Nothing is waiting for
            // this response.
            return Ok(Array.Empty<SearchSuggestion>());
        }
    }
}
