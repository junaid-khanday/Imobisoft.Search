namespace Imobisoft.Search.Services.Ai;

/// <summary>One text transform to run.</summary>
public sealed class AiCompletionRequest
{
    /// <summary>Instructions. Built by the package from the action; never supplied by a visitor.</summary>
    public required string SystemPrompt { get; init; }

    /// <summary>The text to work on, already capped to the profile's input limit.</summary>
    public required string UserText { get; init; }

    public required AiResolvedTarget Target { get; init; }

    public required string ApiKey { get; init; }

    public int MaxOutputTokens { get; init; } = 2048;
}

/// <summary>
/// What came back. A failure is returned rather than thrown: the caller is an endpoint a visitor is
/// waiting on, and a provider being down should read as a message, not a 500.
/// </summary>
public sealed class AiCompletionResult
{
    public bool Ok { get; init; }

    public string Text { get; init; } = string.Empty;

    /// <summary>Message safe to show a visitor. Never carries the key or the raw provider payload.</summary>
    public string Error { get; init; } = string.Empty;

    public static AiCompletionResult Success(string text) => new() { Ok = true, Text = text.Trim() };

    public static AiCompletionResult Failure(string error) => new() { Ok = false, Error = error };
}

/// <summary>One model a provider says it can run.</summary>
public sealed class AiModelInfo
{
    /// <summary>The id to send as the model, exactly as the provider spells it.</summary>
    public required string Id { get; init; }

    /// <summary>Name to show, with price where the provider publishes one.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Input price per million tokens, when the provider reports it.</summary>
    public decimal? PromptPricePerMillion { get; init; }

    /// <summary>True when the provider prices this model at nothing.</summary>
    public bool IsFree { get; init; }
}

/// <summary>Models a provider offers, or why the list could not be fetched.</summary>
public sealed class AiModelListResult
{
    public bool Ok { get; init; }

    public IReadOnlyList<AiModelInfo> Models { get; init; } = Array.Empty<AiModelInfo>();

    public string Error { get; init; } = string.Empty;

    public static AiModelListResult Success(IReadOnlyList<AiModelInfo> models)
        => new() { Ok = true, Models = models };

    public static AiModelListResult Failure(string error) => new() { Ok = false, Error = error };
}

/// <summary>
/// Calls one provider. One implementation per wire format rather than per vendor, because most
/// vendors share the OpenAI chat shape.
/// </summary>
public interface IAiCompletionClient
{
    AiProtocol Protocol { get; }

    Task<AiCompletionResult> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Asks the provider which models this key can actually run. Used so adding a key is enough to
    /// get a working model, instead of leaving an editor to find a slug the provider accepts.
    /// </summary>
    Task<AiModelListResult> ListModelsAsync(
        AiResolvedTarget target,
        string apiKey,
        CancellationToken cancellationToken);
}
