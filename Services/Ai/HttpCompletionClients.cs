using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Imobisoft.Search.Models;
using Microsoft.Extensions.Logging;

namespace Imobisoft.Search.Services.Ai;

/// <summary>Shared plumbing for the providers the package talks to over plain HTTP.</summary>
public abstract class HttpCompletionClientBase
{
    /// <summary>Named client, so a site can configure the timeout and handler in one place.</summary>
    public const string HttpClientName = "Imobisoft.Search.Ai";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;

    protected HttpCompletionClientBase(IHttpClientFactory httpClientFactory, ILogger logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    protected HttpClient CreateClient() => _httpClientFactory.CreateClient(HttpClientName);

    protected async Task<AiCompletionResult> SendAsync(
        HttpClient http,
        HttpRequestMessage message,
        Func<JsonElement, string?> readText,
        string providerName,
        CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await http.SendAsync(message, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return AiCompletionResult.Failure(Describe(providerName, response.StatusCode, body));
            }

            using JsonDocument document = JsonDocument.Parse(body);
            var text = readText(document.RootElement);

            return string.IsNullOrWhiteSpace(text)
                ? AiCompletionResult.Failure(
                    $"{providerName} returned no text. The output token limit may be too low for the model to answer.")
                : AiCompletionResult.Success(text);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient reports its own timeout as a TaskCanceledException, which is an
            // OperationCanceledException - so "cancelled" has to be told apart from "timed out" by
            // asking whether the caller's token is the one that fired. Without this check a slow or
            // unreachable provider escapes as an unhandled exception instead of a message.
            _logger.LogWarning(
                ex,
                "Imobisoft.Search timed out calling {Provider} at {Host}.",
                providerName,
                Host(message));

            return AiCompletionResult.Failure(
                $"{providerName} did not respond in time ({Host(message)}). Check that this server can "
                + "reach it, and that the API base URL is right.");
        }
        catch (OperationCanceledException)
        {
            // The caller really did give up - the visitor navigated away, or the request was aborted.
            throw;
        }
        catch (HttpRequestException ex)
        {
            // No connection at all: DNS, a refused port, a TLS failure, or an egress rule. The host
            // is named because that is the one thing that makes this diagnosable.
            _logger.LogWarning(
                ex,
                "Imobisoft.Search could not connect to {Provider} at {Host}.",
                providerName,
                Host(message));

            return AiCompletionResult.Failure(
                $"Could not connect to {providerName} at {Host(message)}: {Shorten(ex.Message)}");
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Imobisoft.Search could not read the {Provider} response.", providerName);
            return AiCompletionResult.Failure($"{providerName} returned a response the package could not read.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Imobisoft.Search could not reach {Provider}.", providerName);
            return AiCompletionResult.Failure($"Could not reach {providerName}: {Shorten(ex.Message)}");
        }
    }

    /// <summary>Host the request was aimed at, for an error an editor can act on.</summary>
    private static string Host(HttpRequestMessage message)
        => message.RequestUri?.Host ?? "an unknown host";

    /// <summary>
    /// Turns a provider's own error into one sentence. The provider's message is included because
    /// it usually names the real problem - an unknown model, a disabled key - but it is trimmed, and
    /// nothing from the request is echoed back.
    /// </summary>
    protected static string Describe(string providerName, System.Net.HttpStatusCode status, string body)
    {
        var detail = ReadErrorMessage(body);

        var prefix = (int)status switch
        {
            401 => $"{providerName} rejected the API key.",
            403 => $"{providerName} refused the request - the key may not have access to that model.",
            404 => $"{providerName} does not recognise that model or endpoint.",
            422 => $"{providerName} rejected the request.",
            429 => $"{providerName} is rate limiting this key. Try again shortly.",
            >= 500 => $"{providerName} is unavailable at the moment. Try again shortly.",
            _ => $"{providerName} returned {(int)status}.",
        };

        return string.IsNullOrWhiteSpace(detail) ? prefix : $"{prefix} {Shorten(detail)}";
    }

    /// <summary>Both the OpenAI and Gemini shapes nest the message under <c>error.message</c>.</summary>
    private static string ReadErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);

            if (document.RootElement.TryGetProperty("error", out JsonElement error))
            {
                if (error.ValueKind == JsonValueKind.String)
                {
                    return error.GetString() ?? string.Empty;
                }

                if (error.ValueKind == JsonValueKind.Object
                    && error.TryGetProperty("message", out JsonElement errorMessage))
                {
                    return errorMessage.GetString() ?? string.Empty;
                }
            }

            if (document.RootElement.TryGetProperty("message", out JsonElement message))
            {
                return message.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            // Not JSON - an HTML error page from a proxy, say. Nothing useful to pull out.
        }

        return string.Empty;
    }

    protected static string Shorten(string value)
        => value.Length <= 300 ? value : value[..300] + "…";

    /// <summary>
    /// Fetches and parses a provider's model list. The shapes differ, so the caller supplies the
    /// reader; everything about getting there and failing cleanly is the same.
    /// </summary>
    protected async Task<AiModelListResult> ListAsync(
        HttpRequestMessage message,
        Func<JsonElement, IReadOnlyList<AiModelInfo>> readModels,
        string providerName,
        CancellationToken cancellationToken)
    {
        HttpClient http = CreateClient();

        try
        {
            using HttpResponseMessage response = await http.SendAsync(message, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return AiModelListResult.Failure(Describe(providerName, response.StatusCode, body));
            }

            using JsonDocument document = JsonDocument.Parse(body);

            return AiModelListResult.Success(readModels(document.RootElement));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return AiModelListResult.Failure($"{providerName} did not answer in time ({Host(message)}).");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Imobisoft.Search could not list {Provider} models.", providerName);
            return AiModelListResult.Failure($"Could not list {providerName} models: {Shorten(ex.Message)}");
        }
    }

    /// <summary>
    /// Price per million tokens from a string the provider gives as price per token. Returns null
    /// rather than guessing when it is missing or unparseable.
    /// </summary>
    protected static decimal? PricePerMillion(JsonElement owner, string property)
    {
        if (!owner.TryGetProperty(property, out JsonElement value))
        {
            return null;
        }

        var raw = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();

        return decimal.TryParse(raw, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed * 1_000_000m
            : null;
    }

    protected static string Label(string id, decimal? pricePerMillion)
    {
        if (pricePerMillion is null)
        {
            return id;
        }

        return pricePerMillion.Value <= 0m
            ? id + " — free"
            : $"{id} — ${pricePerMillion.Value:0.##}/M in";
    }
}

/// <summary>
/// The OpenAI chat-completions shape. One implementation covers OpenAI, Groq, Mistral, DeepSeek,
/// OpenRouter, Azure OpenAI and anything self-hosted that speaks the same API - which is why
/// pasting a key from any of them works without new code.
/// </summary>
public sealed class OpenAiCompatibleCompletionClient : HttpCompletionClientBase, IAiCompletionClient
{
    public OpenAiCompatibleCompletionClient(
        IHttpClientFactory httpClientFactory,
        ILogger<OpenAiCompatibleCompletionClient> logger)
        : base(httpClientFactory, logger)
    {
    }

    public AiProtocol Protocol => AiProtocol.OpenAiChat;

    public async Task<AiCompletionResult> CompleteAsync(
        AiCompletionRequest request,
        CancellationToken cancellationToken)
    {
        // Not disposed: the factory owns the handler's lifetime, and disposing the client would
        // cancel any request still in flight.
        HttpClient http = CreateClient();

        AiCompletionResult result = await PostAsync(http, request, "max_tokens", cancellationToken);

        // Several newer models reject max_tokens and want max_completion_tokens instead. Rather than
        // keeping a list of which ones, the first rejection is taken as the answer and the request
        // is re-sent the other way.
        if (!result.Ok && result.Error.Contains("max_completion_tokens", StringComparison.OrdinalIgnoreCase))
        {
            return await PostAsync(http, request, "max_completion_tokens", cancellationToken);
        }

        return result;
    }

    private Task<AiCompletionResult> PostAsync(
        HttpClient http,
        AiCompletionRequest request,
        string tokenLimitField,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = request.Target.Model,
            ["messages"] = new object[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = request.UserText },
            },
            [tokenLimitField] = request.MaxOutputTokens,
        };

        var message = new HttpRequestMessage(HttpMethod.Post, Endpoint(request.Target))
        {
            Content = JsonContent.Create(payload),
        };

        if (request.Target.Provider == AiProvider.AzureOpenAi)
        {
            // Azure authenticates with its own header rather than a bearer token.
            message.Headers.TryAddWithoutValidation("api-key", request.ApiKey);
        }
        else
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.ApiKey);
        }

        return SendAsync(http, message, ReadText, request.Target.DisplayName, cancellationToken);
    }

    public Task<AiModelListResult> ListModelsAsync(
        AiResolvedTarget target,
        string apiKey,
        CancellationToken cancellationToken)
    {
        if (target.Provider == AiProvider.AzureOpenAi)
        {
            // Azure serves deployments you have created, not a catalogue, and the endpoint already
            // names one - so there is nothing to list.
            return Task.FromResult(AiModelListResult.Failure(
                "Azure OpenAI has no model list - the deployment name is part of its endpoint URL."));
        }

        var message = new HttpRequestMessage(
            HttpMethod.Get,
            target.BaseUrl.TrimEnd('/') + "/models");

        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        return ListAsync(message, ReadModels, target.DisplayName, cancellationToken);
    }

    private static IReadOnlyList<AiModelInfo> ReadModels(JsonElement root)
    {
        if (!root.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<AiModelInfo>();
        }

        var models = new List<AiModelInfo>();

        foreach (JsonElement entry in data.EnumerateArray())
        {
            if (!entry.TryGetProperty("id", out JsonElement id) || id.GetString() is not { } value)
            {
                continue;
            }

            // OpenRouter reports pricing here; OpenAI and most others do not, so it stays null and
            // the label is just the id.
            decimal? price = entry.TryGetProperty("pricing", out JsonElement pricing)
                ? PricePerMillion(pricing, "prompt")
                : null;

            models.Add(new AiModelInfo
            {
                Id = value,
                Label = Label(value, price),
                PromptPricePerMillion = price,
                IsFree = price is <= 0m,
            });
        }

        return models;
    }

    /// <summary>
    /// Azure's endpoint carries the deployment name and an api-version, and differs per resource, so
    /// its base URL is used exactly as entered. Everyone else gets the standard path appended.
    /// </summary>
    private static string Endpoint(AiResolvedTarget target)
        => target.Provider == AiProvider.AzureOpenAi
            ? target.BaseUrl
            : target.BaseUrl.TrimEnd('/') + "/chat/completions";

    private static string? ReadText(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out JsonElement choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            return null;
        }

        JsonElement first = choices[0];

        if (first.TryGetProperty("message", out JsonElement message)
            && message.TryGetProperty("content", out JsonElement content))
        {
            // Most providers return a string; a few return the content as an array of parts.
            if (content.ValueKind == JsonValueKind.String)
            {
                return content.GetString();
            }

            if (content.ValueKind == JsonValueKind.Array)
            {
                return string.Join(
                    string.Empty,
                    content.EnumerateArray()
                        .Select(part => part.TryGetProperty("text", out JsonElement text) ? text.GetString() : null)
                        .Where(value => !string.IsNullOrEmpty(value)));
            }
        }

        return null;
    }
}

/// <summary>Google's Generative Language API, which has a shape of its own.</summary>
public sealed class GeminiCompletionClient : HttpCompletionClientBase, IAiCompletionClient
{
    public GeminiCompletionClient(
        IHttpClientFactory httpClientFactory,
        ILogger<GeminiCompletionClient> logger)
        : base(httpClientFactory, logger)
    {
    }

    public AiProtocol Protocol => AiProtocol.Gemini;

    public async Task<AiCompletionResult> CompleteAsync(
        AiCompletionRequest request,
        CancellationToken cancellationToken)
    {
        // Not disposed, and awaited rather than returned: the factory owns the handler's lifetime,
        // and disposing this client cancels whatever request is still in flight. Returning the task
        // out of a `using` did exactly that - the request died the moment this method returned, and
        // surfaced as a timeout.
        HttpClient http = CreateClient();

        var payload = new
        {
            system_instruction = new { parts = new[] { new { text = request.SystemPrompt } } },
            contents = new[]
            {
                new { role = "user", parts = new[] { new { text = request.UserText } } },
            },
            generationConfig = new { maxOutputTokens = request.MaxOutputTokens },
        };

        var endpoint = $"{request.Target.BaseUrl.TrimEnd('/')}/models/{request.Target.Model}:generateContent";

        var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(payload),
        };

        // In the header rather than the query string, so the key cannot end up in an access log.
        message.Headers.TryAddWithoutValidation("x-goog-api-key", request.ApiKey);

        return await SendAsync(http, message, ReadText, request.Target.DisplayName, cancellationToken);
    }

    public Task<AiModelListResult> ListModelsAsync(
        AiResolvedTarget target,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var message = new HttpRequestMessage(
            HttpMethod.Get,
            target.BaseUrl.TrimEnd('/') + "/models?pageSize=200");

        message.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);

        return ListAsync(message, ReadModels, target.DisplayName, cancellationToken);
    }

    private static IReadOnlyList<AiModelInfo> ReadModels(JsonElement root)
    {
        if (!root.TryGetProperty("models", out JsonElement models) || models.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<AiModelInfo>();
        }

        var results = new List<AiModelInfo>();

        foreach (JsonElement entry in models.EnumerateArray())
        {
            if (!entry.TryGetProperty("name", out JsonElement name) || name.GetString() is not { } value)
            {
                continue;
            }

            // Google lists embedding and other models alongside the chat ones; only those that can
            // answer generateContent are any use here.
            if (entry.TryGetProperty("supportedGenerationMethods", out JsonElement methods)
                && methods.ValueKind == JsonValueKind.Array
                && !methods.EnumerateArray().Any(m => m.GetString() == "generateContent"))
            {
                continue;
            }

            // Ids come back as "models/gemini-…" but are sent without the prefix.
            var id = value.StartsWith("models/", StringComparison.OrdinalIgnoreCase) ? value[7..] : value;

            results.Add(new AiModelInfo { Id = id, Label = id });
        }

        return results;
    }

    private static string? ReadText(JsonElement root)
    {
        // A blocked prompt comes back as a success with no candidates.
        if (root.TryGetProperty("promptFeedback", out JsonElement feedback)
            && feedback.TryGetProperty("blockReason", out JsonElement blockReason))
        {
            return $"[blocked: {blockReason.GetString()}]";
        }

        if (!root.TryGetProperty("candidates", out JsonElement candidates)
            || candidates.ValueKind != JsonValueKind.Array
            || candidates.GetArrayLength() == 0)
        {
            return null;
        }

        if (!candidates[0].TryGetProperty("content", out JsonElement content)
            || !content.TryGetProperty("parts", out JsonElement parts)
            || parts.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return string.Join(
            string.Empty,
            parts.EnumerateArray()
                .Select(part => part.TryGetProperty("text", out JsonElement text) ? text.GetString() : null)
                .Where(value => !string.IsNullOrEmpty(value)));
    }
}
