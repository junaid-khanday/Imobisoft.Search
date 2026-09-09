using Imobisoft.Search.Models;

namespace Imobisoft.Search.Services.Ai.Providers;

/// <summary>
/// One model call, in the only shape this package needs: a system prompt, a user prompt, and a JSON
/// schema the reply must fit.
/// <para>
/// Every provider gets reduced to this. It is what lets the answer, query-understanding and
/// re-ranking passes be written once against "a model" rather than three times per vendor, and what
/// lets a site point the package at an endpoint that did not exist when it was built.
/// </para>
/// </summary>
internal interface IAiChatClient
{
    /// <summary>
    /// Returns the model's reply as a JSON string matching <paramref name="schemaJson"/>, or null
    /// when it could not be obtained. Implementations must not throw: a provider being down is a
    /// reason to serve keyword results, not to fail a search.
    /// </summary>
    Task<string?> CompleteAsync(
        AiSettings settings,
        string system,
        string user,
        int maxTokens,
        AiEffort effort,
        string schemaJson,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sends one trivial request to prove the credential and endpoint work, reporting whatever the
    /// provider said so a wrong key reads as the provider's own error rather than "it failed".
    /// </summary>
    Task<AiConnectionResult> PingAsync(AiSettings settings, CancellationToken cancellationToken);
}
