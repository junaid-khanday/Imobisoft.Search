using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Logging;

namespace Imobisoft.Search.Services.Ai;

/// <summary>
/// Calls Claude through Anthropic's own .NET SDK rather than over hand-rolled HTTP, so retries,
/// typed errors and the current request shape come from the vendor rather than from this package.
/// </summary>
public sealed class AnthropicCompletionClient : IAiCompletionClient
{
    private readonly ILogger<AnthropicCompletionClient> _logger;

    public AnthropicCompletionClient(ILogger<AnthropicCompletionClient> logger) => _logger = logger;

    public AiProtocol Protocol => AiProtocol.Anthropic;

    public async Task<AiCompletionResult> CompleteAsync(
        AiCompletionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            AnthropicClient client = new() { ApiKey = request.ApiKey };

            var parameters = new MessageCreateParams
            {
                Model = request.Target.Model,
                MaxTokens = request.MaxOutputTokens,
                System = request.SystemPrompt,

                // Thinking left on at the lowest effort. These are short transforms, so the cost is
                // small - and turning thinking off on the current Claude models risks internal tags
                // appearing in the answer, which a visitor would see.
                Thinking = new ThinkingConfigAdaptive(),
                OutputConfig = new OutputConfig { Effort = Effort.Low },

                Messages = new List<MessageParam>
                {
                    new(new UserMessageParam { Content = request.UserText }),
                },
            };

            Message message = await client.Messages.Create(parameters, cancellationToken);

            // A safety classifier can decline a request: that arrives as a normal success with no
            // content, so it has to be checked before the content is read.
            if (message.StopReason == StopReason.Refusal)
            {
                return AiCompletionResult.Failure("The model declined to process this text.");
            }

            var text = string.Join(
                "\n\n",
                message.Content
                    .Select(block => block.Value)
                    .OfType<TextBlock>()
                    .Select(block => block.Text)
                    .Where(value => !string.IsNullOrWhiteSpace(value)));

            return string.IsNullOrWhiteSpace(text)
                ? AiCompletionResult.Failure("The model returned nothing. Try raising the output token limit.")
                : AiCompletionResult.Success(text);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // The SDK's own timeout arrives as a TaskCanceledException, which is an
            // OperationCanceledException - so it has to be told apart from the caller cancelling by
            // checking whose token fired, or a slow network escapes as an unhandled exception.
            _logger.LogWarning(ex, "Imobisoft.Search timed out calling Anthropic.");

            return AiCompletionResult.Failure(
                "Anthropic did not respond in time. Check that this server can reach "
                + "api.anthropic.com.");
        }
        catch (OperationCanceledException)
        {
            // The caller gave up, not the provider.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Imobisoft.Search could not reach Anthropic for an AI text transform.");

            return AiCompletionResult.Failure(Describe(ex));
        }
    }

    public async Task<AiModelListResult> ListModelsAsync(
        AiResolvedTarget target,
        string apiKey,
        CancellationToken cancellationToken)
    {
        try
        {
            AnthropicClient client = new() { ApiKey = apiKey };

            var page = await client.Models.List(cancellationToken: cancellationToken);

            return AiModelListResult.Success(
                page.Items
                    .Select(model => new AiModelInfo { Id = model.ID, Label = model.DisplayName ?? model.ID })
                    .ToList());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return AiModelListResult.Failure("Anthropic did not answer in time.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Imobisoft.Search could not list Anthropic models.");
            return AiModelListResult.Failure(Describe(ex));
        }
    }

    /// <summary>
    /// Turns the SDK's exception into something an editor can act on. The message is kept short and
    /// free of anything echoed back from the request.
    /// </summary>
    private static string Describe(Exception ex) => ex switch
    {
        AnthropicUnauthorizedException => "Anthropic rejected the API key.",
        AnthropicForbiddenException => "This Anthropic key does not have access to that model.",
        AnthropicNotFoundException => "Anthropic does not recognise that model name.",
        AnthropicRateLimitException => "Anthropic is rate limiting this key. Try again shortly.",
        Anthropic5xxException => "Anthropic is unavailable at the moment. Try again shortly.",
        AnthropicBadRequestException bad => "Anthropic rejected the request: " + Trim(bad.Message),
        _ => "Could not reach Anthropic: " + Trim(ex.Message),
    };

    private static string Trim(string message)
        => message.Length <= 300 ? message : message[..300] + "…";
}
