using Imobisoft.Search.Models;
using Imobisoft.Search.Services.Ai;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Imobisoft.Search.Api;

/// <summary>The AI key as the backoffice is allowed to see it: that one exists, and its last four characters.</summary>
public sealed class AiSettingsStatus
{
    public bool HasKey { get; set; }

    public string Hint { get; set; } = string.Empty;

    /// <summary>Provider read from the key's shape, so the dashboard can show what it recognised.</summary>
    public string DetectedProvider { get; set; } = string.Empty;

    public string DetectedProviderName { get; set; } = string.Empty;

    /// <summary>Every provider the package can call, for the dropdown.</summary>
    public IList<AiProviderOption> Providers { get; set; } = new List<AiProviderOption>();
}

/// <summary>One provider, as the dashboard lists it.</summary>
public sealed class AiProviderOption
{
    public string Value { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public string DefaultModel { get; set; } = string.Empty;

    public bool RequiresBaseUrl { get; set; }

    public string ModelsDocumentation { get; set; } = string.Empty;
}

/// <summary>Body for saving a key.</summary>
public sealed class AiKeyRequest
{
    public string ApiKey { get; set; } = string.Empty;
}

/// <summary>One model the provider says this key can run.</summary>
public sealed class AiModelOption
{
    public string Id { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public bool IsFree { get; set; }
}

/// <summary>The provider's own model list, plus which one to use.</summary>
public sealed class AiModelsResult
{
    public bool Ok { get; set; }

    public string Error { get; set; } = string.Empty;

    /// <summary>Model the package would pick, chosen from the list below.</summary>
    public string Suggested { get; set; } = string.Empty;

    public string SuggestedLabel { get; set; } = string.Empty;

    public IList<AiModelOption> Models { get; set; } = new List<AiModelOption>();
}

/// <summary>Outcome of the Test button.</summary>
public sealed class AiTestResult
{
    public bool Ok { get; set; }

    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Adds and tests the AI key from the backoffice.
/// <para>
/// The key is write-only across this API: it can be set, replaced and cleared, and the only thing
/// ever read back is whether one exists plus its last four characters. That is deliberate - the
/// profile's rules travel to the browser in full, so a key kept alongside them would travel too.
/// </para>
/// </summary>
public sealed class SearchAiController : ImobisoftSearchControllerBase
{
    private readonly IAiCredentialStore _credentials;
    private readonly IAiTextService _ai;

    public SearchAiController(IAiCredentialStore credentials, IAiTextService ai)
    {
        _credentials = credentials;
        _ai = ai;
    }

    /// <summary>Whether a key is in place, and the providers the package can call.</summary>
    [HttpGet("ai")]
    [ProducesResponseType(typeof(AiSettingsStatus), StatusCodes.Status200OK)]
    public IActionResult Get() => Ok(Describe());

    /// <summary>Saves or replaces the key.</summary>
    [HttpPut("ai/key")]
    [ProducesResponseType(typeof(AiSettingsStatus), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult SaveKey([FromBody] AiKeyRequest request)
    {
        var key = (request?.ApiKey ?? string.Empty).Trim();

        if (string.IsNullOrEmpty(key))
        {
            return BadRequest(new { title = "No key", detail = "Paste an API key, or use Remove to clear the current one." });
        }

        // A key pasted with surrounding quotes or a "Bearer " prefix is a common slip, and it fails
        // later in a way that looks like a rejected key rather than a typo.
        key = key.Trim('"', '\'');

        if (key.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            key = key["Bearer ".Length..].Trim();
        }

        if (key.Length < 16 || key.Any(char.IsWhiteSpace))
        {
            return BadRequest(new
            {
                title = "That does not look like an API key",
                detail = "Keys are at least 16 characters and contain no spaces. Paste the whole key.",
            });
        }

        _credentials.Save(key);

        return Ok(Describe());
    }

    /// <summary>Removes the key, which also stops the text tools answering.</summary>
    [HttpDelete("ai/key")]
    [ProducesResponseType(typeof(AiSettingsStatus), StatusCodes.Status200OK)]
    public IActionResult DeleteKey()
    {
        _credentials.Clear();

        return Ok(Describe());
    }

    /// <summary>
    /// Runs the smallest real request against the configured provider. Worth doing before trusting
    /// the tools, because a wrong model name or endpoint only shows up on a real call.
    /// </summary>
    [HttpPost("ai/test")]
    [ProducesResponseType(typeof(AiTestResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Test(
        [FromQuery] string? profileAlias,
        CancellationToken cancellationToken = default)
    {
        try
        {
            AiCompletionResult result = await _ai.TestAsync(profileAlias, cancellationToken);

            return Ok(new AiTestResult
            {
                Ok = result.Ok,
                Message = result.Ok ? result.Text : result.Error,
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // This endpoint exists to diagnose a misconfiguration, so it answers with the problem
            // rather than a 500 - a stack trace in the browser console is not a usable answer here.
            return Ok(new AiTestResult
            {
                Ok = false,
                Message = "The test could not run: " + ex.Message,
            });
        }
    }

    /// <summary>
    /// The models the stored key can actually run, with the one to use. This is what lets adding a
    /// key fill the model in by itself instead of leaving an editor to find a slug the provider
    /// accepts - the ids come from the provider, so they are never out of date or invented.
    /// </summary>
    [HttpGet("ai/models")]
    [ProducesResponseType(typeof(AiModelsResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Models(
        [FromQuery] string? profileAlias,
        CancellationToken cancellationToken = default)
    {
        try
        {
            AiModelListResult result = await _ai.ListModelsAsync(profileAlias, cancellationToken);

            if (!result.Ok)
            {
                return Ok(new AiModelsResult { Ok = false, Error = result.Error });
            }

            AiProvider provider = AiProviderCatalog.Resolve(
                _ai.RulesFor(profileAlias) ?? new AiRules(),
                _credentials.Read()).Provider;

            AiModelInfo? suggested = AiProviderCatalog.SuggestModel(provider, result.Models);

            return Ok(new AiModelsResult
            {
                Ok = true,
                Suggested = suggested?.Id ?? string.Empty,
                SuggestedLabel = suggested?.Label ?? string.Empty,
                Models = result.Models
                    .OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
                    .Select(m => new AiModelOption { Id = m.Id, Label = m.Label, IsFree = m.IsFree })
                    .ToList(),
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Ok(new AiModelsResult { Ok = false, Error = "Could not list models: " + ex.Message });
        }
    }

    private AiSettingsStatus Describe()
    {
        AiCredentialStatus status = _credentials.Describe();

        return new AiSettingsStatus
        {
            HasKey = status.HasKey,
            Hint = status.Hint,
            DetectedProvider = status.DetectedProvider?.ToString() ?? string.Empty,
            DetectedProviderName = status.DetectedProviderName,
            Providers = AiProviderCatalog.All
                .Select(p => new AiProviderOption
                {
                    Value = p.Provider.ToString(),
                    Name = p.DisplayName,
                    BaseUrl = p.BaseUrl,
                    DefaultModel = p.DefaultModel,
                    RequiresBaseUrl = p.RequiresBaseUrl,
                    ModelsDocumentation = p.ModelsDocumentation,
                })
                .ToList(),
        };
    }
}
