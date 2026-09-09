using System.Diagnostics;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Imobisoft.Search.Models;
using Microsoft.Extensions.Logging;

namespace Imobisoft.Search.Services.Ai.Providers;

/// <summary>Claude, through the official Anthropic SDK.</summary>
internal sealed class AnthropicChatClient : IAiChatClient
{
    private readonly ILogger _logger;

    /// <summary>
    /// One client per credential. Constructing an SDK client sets up an HttpClient, so rebuilding it
    /// per search would leak sockets under load; keying on the credential means rotating the key in
    /// the backoffice takes effect on the next search rather than needing a restart.
    /// </summary>
    private readonly object _lock = new();
    private string? _fingerprint;
    private AnthropicClient? _client;

    public AnthropicChatClient(ILogger logger) => _logger = logger;

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
            AnthropicClient client = Resolve(settings.ApiKey);

            // Thinking is deliberately left at the model's default rather than disabled. Disabling
            // it on this model family can leak internal tags into the visible answer, and the effort
            // level is the cheaper, safer lever for keeping a search page fast.
            Message response = await client.Messages.Create(new MessageCreateParams
            {
                Model = settings.Model,
                MaxTokens = maxTokens,
                System = new List<TextBlockParam> { new() { Text = system } },
                OutputConfig = new OutputConfig
                {
                    Effort = MapEffort(effort),
                    Format = new JsonOutputFormat { Schema = Schema(schemaJson) },
                },
                Messages = [new() { Role = Role.User, Content = user }],
            }).WaitAsync(AiTimeouts.For(settings), cancellationToken);

            // A refusal is a successful HTTP call carrying no usable content, so it has to be
            // checked before the content is read rather than caught as an error.
            if (response.StopReason == "refusal")
            {
                _logger.LogDebug("The AI provider declined a search request; serving results without it.");
                return null;
            }

            // Truncated output is not valid JSON against the schema, and half an answer is worse
            // than none - drop it rather than show a sentence that stops mid-word.
            if (response.StopReason == "max_tokens")
            {
                _logger.LogDebug("An AI response hit the token ceiling and was discarded.");
                return null;
            }

            var text = string.Concat(response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));

            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Imobisoft.Search could not complete a Claude call; serving results without it.");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<AiConnectionResult> PingAsync(AiSettings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return new AiConnectionResult { Success = false, Message = "No API key has been saved yet." };
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            Message response = await Resolve(settings.ApiKey).Messages.Create(new MessageCreateParams
            {
                Model = settings.Model,
                MaxTokens = 4000,
                OutputConfig = new OutputConfig { Effort = Effort.Low },
                Messages = [new() { Role = Role.User, Content = "Reply with the single word: ready" }],
            }).WaitAsync(AiTimeouts.For(settings), cancellationToken);

            stopwatch.Stop();

            return response.StopReason == "refusal"
                ? new AiConnectionResult
                {
                    Success = false,
                    Message = "The model declined the test request. The key and model are reachable.",
                    Model = settings.Model,
                    ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
                }
                : new AiConnectionResult
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
            _logger.LogWarning(ex, "Imobisoft.Search could not reach Claude during a connection test.");

            // Shown verbatim in the dashboard: an editor pasting a bad key needs to see
            // "authentication_error", not "something went wrong".
            return new AiConnectionResult
            {
                Success = false,
                Message = ex.Message,
                Model = settings.Model,
                ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
            };
        }
    }

    private AnthropicClient Resolve(string apiKey)
    {
        var fingerprint = AiHash.Fingerprint(apiKey);

        lock (_lock)
        {
            if (_client is not null && _fingerprint == fingerprint)
            {
                return _client;
            }

            _client = new AnthropicClient { ApiKey = apiKey };
            _fingerprint = fingerprint;

            return _client;
        }
    }

    private static Effort MapEffort(AiEffort effort) => effort switch
    {
        AiEffort.High => Effort.High,
        AiEffort.Medium => Effort.Medium,
        _ => Effort.Low,
    };

    /// <summary>Turns a JSON schema literal into the shape the SDK's structured-output format wants.</summary>
    private static Dictionary<string, JsonElement> Schema(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);

        return document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
    }
}
