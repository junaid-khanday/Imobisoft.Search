using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Imobisoft.Search.Models;
using Microsoft.Extensions.Logging;

namespace Imobisoft.Search.Services.Ai.Providers;

/// <summary>
/// Anything that speaks OpenAI's <c>/v1/chat/completions</c>: OpenAI, Azure OpenAI, Google Gemini's
/// compatibility endpoint, Groq, Mistral, DeepSeek, Together, OpenRouter, and a local Ollama or LM
/// Studio.
/// <para>
/// Written against the wire format rather than any vendor's SDK, because that is what makes it one
/// implementation instead of nine - and what lets a site point the package at an endpoint that did
/// not exist when this was written.
/// </para>
/// </summary>
internal sealed partial class OpenAiCompatibleChatClient : IAiChatClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;

    public OpenAiCompatibleChatClient(IHttpClientFactory httpClientFactory, ILogger logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string?> CompleteAsync(
        AiSettings settings,
        string system,
        string user,
        int maxTokens,
        AiEffort effort,
        string schemaJson,
        CancellationToken cancellationToken)
    {
        try
        {
            // Providers disagree about structured output: OpenAI and Groq honour a json_schema,
            // others accept only json_object, and a local Ollama may ignore both. Asking for the
            // shape in the prompt as well means the reply is usable whichever of those is true.
            var instructed = system
                             + "\n\nReply with a single JSON object and nothing else - no prose, no code fences. "
                             + "It must match this JSON Schema:\n" + schemaJson;

            var payload = new Dictionary<string, object?>
            {
                ["model"] = settings.Model,
                ["max_tokens"] = maxTokens,
                ["messages"] = new object[]
                {
                    new { role = "system", content = instructed },
                    new { role = "user", content = user },
                },
                ["response_format"] = new { type = "json_object" },
            };

            using HttpResponseMessage response = await SendAsync(settings, payload, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "The AI provider returned {Status} for a search request; serving results without it. {Body}",
                    (int)response.StatusCode,
                    await SafeBodyAsync(response, cancellationToken));

                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            return ExtractJsonObject(ReadContent(json));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Imobisoft.Search could not complete an AI call; serving results without it.");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<AiConnectionResult> PingAsync(AiSettings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            return new AiConnectionResult { Success = false, Message = "No API base URL has been set." };
        }

        if (string.IsNullOrWhiteSpace(settings.Model))
        {
            return new AiConnectionResult { Success = false, Message = "No model name has been set." };
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var payload = new Dictionary<string, object?>
            {
                ["model"] = settings.Model,
                ["max_tokens"] = 32,
                ["messages"] = new object[] { new { role = "user", content = "Reply with the single word: ready" } },
            };

            using HttpResponseMessage response = await SendAsync(settings, payload, cancellationToken);
            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                // Reported verbatim: an editor with a wrong key or a misspelled model needs the
                // provider's own words, which name the actual problem.
                return new AiConnectionResult
                {
                    Success = false,
                    Message = $"{(int)response.StatusCode} {response.ReasonPhrase}: "
                              + await SafeBodyAsync(response, cancellationToken),
                    Model = settings.Model,
                    ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
                };
            }

            return new AiConnectionResult
            {
                Success = true,
                Message = $"Connected. {settings.Model} answered in {stopwatch.ElapsedMilliseconds}ms.",
                Model = settings.Model,
                ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex, "Imobisoft.Search could not reach the AI provider during a connection test.");

            return new AiConnectionResult
            {
                Success = false,
                Message = ex.Message,
                Model = settings.Model,
                ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
            };
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        AiSettings settings,
        Dictionary<string, object?> payload,
        CancellationToken cancellationToken)
    {
        HttpClient http = _httpClientFactory.CreateClient(nameof(OpenAiCompatibleChatClient));
        http.Timeout = AiTimeouts.For(settings);

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint(settings.BaseUrl))
        {
            Content = JsonContent.Create(payload),
        };

        // A local Ollama or LM Studio needs no credential, so an empty key is a valid configuration
        // rather than something to reject.
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        }

        return await http.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// Builds the chat-completions URL, tolerating however the base was pasted - with or without a
    /// trailing slash, with or without <c>/v1</c>, or already pointing at the endpoint itself.
    /// </summary>
    private static string Endpoint(string baseUrl)
    {
        var trimmed = baseUrl.Trim().TrimEnd('/');

        return trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : trimmed + "/chat/completions";
    }

    /// <summary>Pulls the assistant message out of a chat-completions response.</summary>
    private static string? ReadContent(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("choices", out JsonElement choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            return null;
        }

        return choices[0].TryGetProperty("message", out JsonElement message)
               && message.TryGetProperty("content", out JsonElement content)
            ? content.GetString()
            : null;
    }

    /// <summary>
    /// Finds the JSON object in a reply. Models that ignore <c>response_format</c> wrap it in a code
    /// fence or a sentence of preamble, so the object is located rather than assumed.
    /// </summary>
    private static string? ExtractJsonObject(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var text = FencePattern().Replace(content, string.Empty).Trim();
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');

        return start >= 0 && end > start ? text[start..(end + 1)] : null;
    }

    /// <summary>
    /// The provider's error body, capped. Whatever it says is more useful than a status code alone,
    /// but a provider that returns an HTML error page should not put all of it in the log.
    /// </summary>
    private static async Task<string> SafeBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            return body.Length <= 400 ? body : body[..400] + "…";
        }
        catch
        {
            return string.Empty;
        }
    }

    [GeneratedRegex(@"^```[a-zA-Z]*|```$", RegexOptions.Multiline)]
    private static partial Regex FencePattern();
}
