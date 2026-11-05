using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Imobisoft.Search.Configuration;
using Imobisoft.Search.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Cache;
using Umbraco.Extensions;

namespace Imobisoft.Search.Services.Ai;

/// <summary>One request from the site: which result, which action, and into which language.</summary>
public sealed class AiTextRequest
{
    public required AiAction Action { get; init; }

    /// <summary>The result's node key. The text itself is read server-side from that key.</summary>
    public required Guid Key { get; init; }

    /// <summary>Target language for <see cref="AiAction.Translate"/>; ignored by the other actions.</summary>
    public string? Language { get; init; }

    /// <summary>
    /// Identifies the caller for rate limiting - the client IP. Never stored, only hashed into a
    /// counter bucket.
    /// </summary>
    public string? Caller { get; init; }

    /// <summary>Which profile's AI rules apply. Null uses the default profile.</summary>
    public string? ProfileAlias { get; init; }
}

/// <summary>What the site gets back.</summary>
public sealed class AiTextResult
{
    public bool Ok { get; init; }

    public string Text { get; init; } = string.Empty;

    public string Error { get; init; } = string.Empty;

    /// <summary>True when this answer came from the cache, so the site can show it instantly.</summary>
    public bool Cached { get; init; }

    /// <summary>HTTP status the endpoint should use.</summary>
    public int Status { get; init; } = 200;

    public static AiTextResult Success(string text, bool cached = false)
        => new() { Ok = true, Text = text, Cached = cached };

    public static AiTextResult Failure(string error, int status = 400)
        => new() { Ok = false, Error = error, Status = status };
}

/// <summary>The live text tools, as the site's endpoint calls them.</summary>
public interface IAiTextService
{
    /// <summary>Whether the tools are configured and switched on for a profile.</summary>
    bool IsAvailable(string? profileAlias);

    /// <summary>The rules the frontend needs: which actions to offer, and which languages.</summary>
    AiRules? RulesFor(string? profileAlias);

    Task<AiTextResult> TransformAsync(AiTextRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Runs the smallest possible request to prove the key, model and endpoint work together. Used
    /// by the backoffice's Test button, so a misconfiguration is found while an editor is still
    /// looking at the screen rather than by a visitor.
    /// </summary>
    Task<AiCompletionResult> TestAsync(string? profileAlias, CancellationToken cancellationToken);

    /// <summary>
    /// Asks the provider which models the stored key can run, and which of them to use. Called when
    /// a key is added, so the model field fills itself in from the provider rather than from a
    /// guess an editor has to correct.
    /// </summary>
    Task<AiModelListResult> ListModelsAsync(string? profileAlias, CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class AiTextService : IAiTextService
{
    /// <summary>
    /// Per-caller request counters. In-process on purpose: it needs no schema and no dependency, and
    /// on a load-balanced site each server simply enforces its own share of the limit.
    /// </summary>
    private static readonly ConcurrentDictionary<string, CallerWindow> Windows = new();

    private static DateTime _lastSweep = DateTime.MinValue;

    private readonly ISearchProfileService _profiles;
    private readonly IAiCredentialStore _credentials;
    private readonly IAiContentReader _contentReader;
    private readonly IEnumerable<IAiCompletionClient> _clients;
    private readonly AppCaches _appCaches;
    private readonly ImobisoftSearchOptions _options;
    private readonly ILogger<AiTextService> _logger;

    public AiTextService(
        ISearchProfileService profiles,
        IAiCredentialStore credentials,
        IAiContentReader contentReader,
        IEnumerable<IAiCompletionClient> clients,
        AppCaches appCaches,
        IOptions<ImobisoftSearchOptions> options,
        ILogger<AiTextService> logger)
    {
        _profiles = profiles;
        _credentials = credentials;
        _contentReader = contentReader;
        _clients = clients;
        _appCaches = appCaches;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsAvailable(string? profileAlias)
    {
        AiRules? rules = RulesFor(profileAlias);

        return rules is { Enabled: true }
               && rules.Actions.Count > 0
               && _credentials.Describe().HasKey;
    }

    /// <inheritdoc />
    public AiRules? RulesFor(string? profileAlias)
    {
        if (!_options.Enabled || !_options.EnableAiTextTools)
        {
            return null;
        }

        return _profiles.ResolveForSearch(profileAlias)?.Rules.Ai;
    }

    /// <inheritdoc />
    public async Task<AiTextResult> TransformAsync(AiTextRequest request, CancellationToken cancellationToken)
    {
        AiRules? rules = RulesFor(request.ProfileAlias);

        if (rules is null || !rules.Enabled)
        {
            return AiTextResult.Failure("The AI text tools are switched off.", 404);
        }

        if (!rules.Actions.Contains(request.Action))
        {
            return AiTextResult.Failure("That action is not enabled.", 400);
        }

        // Checked against the profile's own list, so the language reaching the prompt is one an
        // editor chose rather than one a caller invented.
        var language = ResolveLanguage(rules, request.Language);

        if (request.Action == AiAction.Translate && language is null)
        {
            return AiTextResult.Failure("That language is not enabled.", 400);
        }

        var apiKey = _credentials.Read();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return AiTextResult.Failure("No AI key has been added yet.", 503);
        }

        AiResolvedTarget target = AiProviderCatalog.Resolve(rules, apiKey);

        if (target.Problem is { } problem)
        {
            _logger.LogWarning("Imobisoft.Search AI is not configured correctly: {Problem}", problem);
            return AiTextResult.Failure("The AI tools are not configured correctly.", 503);
        }

        // Cache first: the same page summarised for the next visitor costs nothing, and a cache hit
        // is also exempt from the rate limit because it never reaches the provider.
        var cacheKey = CacheKey(target, request.Action, request.Key, language);

        if (rules.CacheMinutes > 0
            && _appCaches.RuntimeCache.GetCacheItem<string>(cacheKey) is { } cached
            && !string.IsNullOrWhiteSpace(cached))
        {
            return AiTextResult.Success(cached, cached: true);
        }

        if (!WithinRateLimit(request.Caller, rules.RequestsPerMinutePerVisitor))
        {
            return AiTextResult.Failure("Too many requests. Try again in a minute.", 429);
        }

        AiContentText? content = _contentReader.Read(request.Key, rules.MaxInputCharacters);

        if (content is null)
        {
            return AiTextResult.Failure("That page could not be found in the search index.", 404);
        }

        if (!content.HasBody)
        {
            return AiTextResult.Failure("That page has no text to work with.", 422);
        }

        IAiCompletionClient? client = _clients.FirstOrDefault(c => c.Protocol == target.Protocol);

        if (client is null)
        {
            return AiTextResult.Failure("The AI tools are not configured correctly.", 503);
        }

        AiCompletionResult completion = await client.CompleteAsync(
            new AiCompletionRequest
            {
                SystemPrompt = AiPrompts.For(request.Action, language),
                UserText = Compose(content, rules.MaxInputCharacters),
                Target = target,
                ApiKey = apiKey,
                MaxOutputTokens = Math.Clamp(rules.MaxOutputTokens, 256, 8192),
            },
            cancellationToken);

        if (!completion.Ok)
        {
            // The provider's own wording goes to the log for whoever configured it; a visitor gets
            // something plain, because the detail can name models and account states.
            _logger.LogWarning("Imobisoft.Search AI request failed: {Error}", completion.Error);

            return AiTextResult.Failure("The AI service could not answer that just now.", 502);
        }

        if (completion.Text.Equals("UNAVAILABLE", StringComparison.OrdinalIgnoreCase))
        {
            return AiTextResult.Failure("There is not enough text on that page to work with.", 422);
        }

        if (rules.CacheMinutes > 0)
        {
            _appCaches.RuntimeCache.InsertCacheItem(
                cacheKey,
                () => completion.Text,
                TimeSpan.FromMinutes(rules.CacheMinutes));
        }

        return AiTextResult.Success(completion.Text);
    }

    /// <inheritdoc />
    public async Task<AiCompletionResult> TestAsync(string? profileAlias, CancellationToken cancellationToken)
    {
        AiRules? rules = RulesFor(profileAlias);

        if (rules is null)
        {
            return AiCompletionResult.Failure("The AI text tools are switched off in configuration.");
        }

        var apiKey = _credentials.Read();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return AiCompletionResult.Failure("Add an API key first.");
        }

        AiResolvedTarget target = AiProviderCatalog.Resolve(rules, apiKey);

        if (target.Problem is { } problem)
        {
            return AiCompletionResult.Failure(problem);
        }

        IAiCompletionClient? client = _clients.FirstOrDefault(c => c.Protocol == target.Protocol);

        if (client is null)
        {
            return AiCompletionResult.Failure($"The package cannot call {target.DisplayName}.");
        }

        // An editor is watching this one, so it gives up sooner than a visitor-facing request would.
        // The linked token means the client still sees a cancellation it can distinguish from the
        // caller's, and reports it as a timeout rather than letting it escape.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));

        AiCompletionResult result;

        try
        {
            result = await client.CompleteAsync(
                new AiCompletionRequest
                {
                    SystemPrompt = "Reply with the single word OK. Nothing else.",
                    UserText = "Connection test.",
                    Target = target,
                    ApiKey = apiKey,
                    // Room for a model that thinks before answering: the budget covers both, so a
                    // tight ceiling here would read as "returned nothing" rather than a working key.
                    MaxOutputTokens = Math.Clamp(rules.MaxOutputTokens, 256, 2048),
                },
                deadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Our own 20-second deadline rather than the editor navigating away. The client sees
            // this token as the caller's and rethrows, so it is reported here instead.
            return AiCompletionResult.Failure(
                $"{target.DisplayName} did not respond within 20 seconds. Check that this server can "
                + "reach the internet, and that any base URL is correct.");
        }

        return result.Ok
            ? AiCompletionResult.Success($"{target.DisplayName} answered using {target.Model}.")
            : result;
    }

    /// <inheritdoc />
    public async Task<AiModelListResult> ListModelsAsync(string? profileAlias, CancellationToken cancellationToken)
    {
        AiRules? rules = RulesFor(profileAlias);

        if (rules is null)
        {
            return AiModelListResult.Failure("The AI text tools are switched off in configuration.");
        }

        var apiKey = _credentials.Read();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return AiModelListResult.Failure("Add an API key first.");
        }

        AiResolvedTarget target = AiProviderCatalog.Resolve(rules, apiKey);

        // A missing model is the very thing this call exists to fix, so only the other problems - a
        // key pointed at the wrong provider, a missing base URL - stop it.
        if (string.IsNullOrWhiteSpace(target.BaseUrl))
        {
            return AiModelListResult.Failure($"{target.DisplayName} needs an API base URL first.");
        }

        if (target.KeyLooksLike is { } detected
            && detected != target.Provider
            && target.Provider is not (AiProvider.Custom or AiProvider.AzureOpenAi))
        {
            return AiModelListResult.Failure(
                $"The key looks like it belongs to {AiProviderCatalog.For(detected).DisplayName}, but "
                + $"the provider is set to {target.DisplayName}.");
        }

        IAiCompletionClient? client = _clients.FirstOrDefault(c => c.Protocol == target.Protocol);

        if (client is null)
        {
            return AiModelListResult.Failure($"The package cannot call {target.DisplayName}.");
        }

        // An editor is waiting, so this gives up well before the request timeout would.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));

        try
        {
            return await client.ListModelsAsync(target, apiKey, deadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return AiModelListResult.Failure($"{target.DisplayName} did not answer within 20 seconds.");
        }
    }

    /// <summary>Title first, then the body: the title is the strongest hint about what the page is.</summary>
    private static string Compose(AiContentText content, int maximumCharacters)
    {
        var text = string.IsNullOrWhiteSpace(content.Name)
            ? content.Body
            : content.Name + "\n\n" + content.Body;

        var ceiling = Math.Max(200, maximumCharacters);

        return text.Length <= ceiling ? text : text[..ceiling];
    }

    private static string? ResolveLanguage(AiRules rules, string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            return rules.Languages.FirstOrDefault();
        }

        return rules.Languages.FirstOrDefault(l => l.InvariantEquals(requested.Trim()));
    }

    private static string CacheKey(AiResolvedTarget target, AiAction action, Guid key, string? language)
    {
        // The model is part of the key: changing model should produce fresh answers rather than
        // serving the previous model's.
        var raw = $"{target.Provider}|{target.Model}|{action}|{key:N}|{language}";

        return ImobisoftSearchConstants.Ai.CacheKeyPrefix + "|" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..24];
    }

    /// <summary>
    /// A fixed window per caller per minute. Rough by design - it exists to stop one caller running
    /// up a bill, not to meter fairly.
    /// </summary>
    private static bool WithinRateLimit(string? caller, int perMinute)
    {
        if (perMinute <= 0)
        {
            return false;
        }

        var bucket = string.IsNullOrWhiteSpace(caller) ? "unknown" : caller;
        DateTime now = DateTime.UtcNow;

        Sweep(now);

        CallerWindow window = Windows.GetOrAdd(bucket, _ => new CallerWindow());

        lock (window)
        {
            if (now - window.StartedUtc >= TimeSpan.FromMinutes(1))
            {
                window.StartedUtc = now;
                window.Count = 0;
            }

            if (window.Count >= perMinute)
            {
                return false;
            }

            window.Count++;
            return true;
        }
    }

    /// <summary>Drops windows nobody has used for a while, so the dictionary cannot grow unbounded.</summary>
    private static void Sweep(DateTime now)
    {
        if (now - _lastSweep < TimeSpan.FromMinutes(5))
        {
            return;
        }

        _lastSweep = now;

        foreach (KeyValuePair<string, CallerWindow> entry in Windows)
        {
            if (now - entry.Value.StartedUtc > TimeSpan.FromMinutes(10))
            {
                Windows.TryRemove(entry.Key, out _);
            }
        }
    }

    private sealed class CallerWindow
    {
        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

        public int Count { get; set; }
    }
}
