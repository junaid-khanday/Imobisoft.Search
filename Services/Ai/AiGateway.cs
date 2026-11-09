using Imobisoft.Search.Configuration;
using Imobisoft.Search.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Imobisoft.Search.Services.Ai;

/// <summary>One call to the configured provider, as the search pipeline makes it.</summary>
public sealed class AiGatewayRequest
{
    /// <summary>The profile's AI rules, which carry the provider, the model and the endpoint.</summary>
    public required AiRules Rules { get; init; }

    /// <summary>Instructions. Built by the package; never supplied by a visitor.</summary>
    public required string SystemPrompt { get; init; }

    /// <summary>The content to work on, already fenced and capped by the caller.</summary>
    public required string UserText { get; init; }

    public int MaxOutputTokens { get; init; } = 1024;

    /// <summary>
    /// How long to wait. The pipeline stages pass the profile's own figure, because a stage that
    /// runs inside a search is spending a visitor's patience rather than only money.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// The one place the package turns a profile's AI rules into an actual provider call.
/// <para>
/// It exists because everything in front of a provider is the same every time - read the key,
/// resolve which service and model the key and rules point at, pick the client that speaks that
/// wire format, hold it to a deadline, and turn a failure into something a caller can act on. The
/// search pipeline has three stages that need all of that and nothing else.
/// </para>
/// <para>
/// Deliberately knows nothing about searching. The services above it do the retrieval and build the
/// prompt; this only makes the call.
/// </para>
/// </summary>
public interface IAiGateway
{
    /// <summary>
    /// Whether a provider call can be made at all: the deployment allows it, and a key has been
    /// added. Checked before any retrieval work, so a site with no key spends nothing - not even
    /// the search a stage would have run to build its prompt.
    /// </summary>
    bool IsConfigured(AiRules? rules);

    /// <summary>
    /// Identifies the provider and model for a cache key, so changing model serves fresh answers
    /// rather than the previous model's.
    /// </summary>
    string Fingerprint(AiRules rules);

    Task<AiCompletionResult> RunAsync(AiGatewayRequest request, CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class AiGateway : IAiGateway
{
    private readonly IAiCredentialStore _credentials;
    private readonly IEnumerable<IAiCompletionClient> _clients;
    private readonly ImobisoftSearchOptions _options;
    private readonly ILogger<AiGateway> _logger;

    public AiGateway(
        IAiCredentialStore credentials,
        IEnumerable<IAiCompletionClient> clients,
        IOptions<ImobisoftSearchOptions> options,
        ILogger<AiGateway> logger)
    {
        _credentials = credentials;
        _clients = clients;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsConfigured(AiRules? rules)
    {
        // The profile's own master switch counts here too. An editor who has turned AI off for a
        // profile has turned it off - finding that the results were still being re-ordered by a
        // model, because that happens to have a toggle of its own, would be indefensible.
        if (rules is not { Enabled: true } || !_options.Enabled || !_options.EnableAiSearch)
        {
            return false;
        }

        // Describe rather than Read: this is asked on every search, and whether a key exists is
        // cheaper to answer than decrypting it.
        return _credentials.Describe().HasKey;
    }

    /// <inheritdoc />
    public string Fingerprint(AiRules rules)
    {
        // The key is read rather than skipped, because on Auto it is the key that decides the
        // provider - and a site swapping an Anthropic key for an OpenAI one has changed what
        // answers. Without it here, the cache would keep serving the old provider's replies.
        AiResolvedTarget target = AiProviderCatalog.Resolve(rules, _credentials.Read());

        return target.Provider + "|" + target.Model;
    }

    /// <inheritdoc />
    public async Task<AiCompletionResult> RunAsync(
        AiGatewayRequest request,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled || !_options.EnableAiSearch)
        {
            return AiCompletionResult.Failure("AI in search is switched off in configuration.");
        }

        var apiKey = _credentials.Read();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return AiCompletionResult.Failure("No AI key has been added yet.");
        }

        AiResolvedTarget target = AiProviderCatalog.Resolve(request.Rules, apiKey);

        if (target.Problem is { } problem)
        {
            _logger.LogWarning("Imobisoft.Search AI is not configured correctly: {Problem}", problem);

            return AiCompletionResult.Failure("The AI tools are not configured correctly.");
        }

        IAiCompletionClient? client = _clients.FirstOrDefault(c => c.Protocol == target.Protocol);

        if (client is null)
        {
            return AiCompletionResult.Failure($"The package cannot call {target.DisplayName}.");
        }

        // A stage that runs inside a search holds the whole search up, so the deadline is the
        // caller's and not the provider's. Linked, so the visitor leaving still cancels everything.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(request.Timeout);

        try
        {
            return await client.CompleteAsync(
                new AiCompletionRequest
                {
                    SystemPrompt = request.SystemPrompt,
                    UserText = request.UserText,
                    Target = target,
                    ApiKey = apiKey,
                    MaxOutputTokens = Math.Clamp(request.MaxOutputTokens, 64, 8192),
                },
                deadline.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The visitor left. Nothing is waiting for this.
            throw;
        }
        catch (OperationCanceledException)
        {
            // Our own deadline. Reported rather than thrown, because every caller of this treats a
            // slow provider as "carry on without the AI" and none of them should have to catch.
            return AiCompletionResult.Failure(
                $"{target.DisplayName} did not answer within {request.Timeout.TotalMilliseconds:0}ms.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Imobisoft.Search could not reach {Provider}.", target.DisplayName);

            return AiCompletionResult.Failure($"{target.DisplayName} could not be reached.");
        }
    }
}
