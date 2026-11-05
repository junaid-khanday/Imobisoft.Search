using Imobisoft.Search.Models;

namespace Imobisoft.Search.Services.Ai;

/// <summary>Which wire format a provider speaks. Three shapes cover every provider below.</summary>
public enum AiProtocol
{
    /// <summary>Anthropic's own Messages API, called through the official SDK.</summary>
    Anthropic,

    /// <summary>The OpenAI chat-completions shape, which most vendors have adopted.</summary>
    OpenAiChat,

    /// <summary>Google's Generative Language API.</summary>
    Gemini,
}

/// <summary>Everything the package needs to know to call one provider.</summary>
public sealed class AiProviderProfile
{
    public required AiProvider Provider { get; init; }

    public required AiProtocol Protocol { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>Published API base URL. Empty where the host is per-account and must be supplied.</summary>
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>
    /// Model used when none is configured. Empty where the package has no dependable default - a
    /// guessed model id fails at request time with a confusing error, so an empty default and a
    /// "model is required" message is the better failure.
    /// </summary>
    public string DefaultModel { get; init; } = string.Empty;

    /// <summary>A base URL must be supplied; there is no published one to fall back on.</summary>
    public bool RequiresBaseUrl { get; init; }

    /// <summary>Where to read the model names this provider accepts.</summary>
    public string ModelsDocumentation { get; init; } = string.Empty;

    /// <summary>
    /// Substrings preferred when picking a model automatically, best first. These are matched
    /// against the list the provider actually returns, never used to invent an id - so a stale hint
    /// simply does not match and the choice falls through to price or to the first model offered.
    /// </summary>
    public string[] PreferredModelHints { get; init; } = Array.Empty<string>();
}

/// <summary>
/// What the package knows about each provider, and how it works out which one a key belongs to.
/// <para>
/// This is what makes "paste a key and it works" true rather than aspirational: the key's own
/// prefix identifies the vendor, which supplies the endpoint, the authentication header and the
/// wire format, so an editor does not have to know any of them.
/// </para>
/// </summary>
public static class AiProviderCatalog
{
    private static readonly AiProviderProfile[] Profiles =
    {
        new()
        {
            Provider = AiProvider.Anthropic,
            Protocol = AiProtocol.Anthropic,
            DisplayName = "Anthropic (Claude)",
            BaseUrl = "https://api.anthropic.com",
            // The one default the package states outright, because the model line is documented and
            // stable. Everything else is left to the editor.
            DefaultModel = "claude-opus-5",
            ModelsDocumentation = "https://platform.claude.com/docs/en/about-claude/models/overview",
        },
        new()
        {
            Provider = AiProvider.OpenAi,
            Protocol = AiProtocol.OpenAiChat,
            DisplayName = "OpenAI",
            BaseUrl = "https://api.openai.com/v1",
            ModelsDocumentation = "https://platform.openai.com/docs/models",
            PreferredModelHints = new[] { "gpt-4o-mini", "mini", "gpt-4o", "gpt" },
        },
        new()
        {
            Provider = AiProvider.Gemini,
            Protocol = AiProtocol.Gemini,
            DisplayName = "Google Gemini",
            BaseUrl = "https://generativelanguage.googleapis.com/v1beta",
            ModelsDocumentation = "https://ai.google.dev/gemini-api/docs/models",
            PreferredModelHints = new[] { "flash-lite", "flash", "pro" },
        },
        new()
        {
            Provider = AiProvider.Groq,
            Protocol = AiProtocol.OpenAiChat,
            DisplayName = "Groq",
            BaseUrl = "https://api.groq.com/openai/v1",
            ModelsDocumentation = "https://console.groq.com/docs/models",
            PreferredModelHints = new[] { "8b", "llama" },
        },
        new()
        {
            Provider = AiProvider.Mistral,
            Protocol = AiProtocol.OpenAiChat,
            DisplayName = "Mistral",
            BaseUrl = "https://api.mistral.ai/v1",
            ModelsDocumentation = "https://docs.mistral.ai/getting-started/models/",
            PreferredModelHints = new[] { "small", "mistral" },
        },
        new()
        {
            Provider = AiProvider.DeepSeek,
            Protocol = AiProtocol.OpenAiChat,
            DisplayName = "DeepSeek",
            BaseUrl = "https://api.deepseek.com/v1",
            ModelsDocumentation = "https://api-docs.deepseek.com/quick_start/pricing",
            PreferredModelHints = new[] { "chat" },
        },
        new()
        {
            Provider = AiProvider.OpenRouter,
            Protocol = AiProtocol.OpenAiChat,
            DisplayName = "OpenRouter",
            BaseUrl = "https://openrouter.ai/api/v1",
            ModelsDocumentation = "https://openrouter.ai/models",
            PreferredModelHints = new[] { "llama-3.3-70b-instruct", "gpt-4o-mini", "instruct" },
        },
        new()
        {
            Provider = AiProvider.AzureOpenAi,
            Protocol = AiProtocol.OpenAiChat,
            DisplayName = "Azure OpenAI",
            RequiresBaseUrl = true,
            ModelsDocumentation = "https://learn.microsoft.com/azure/ai-services/openai/concepts/models",
        },
        new()
        {
            Provider = AiProvider.Custom,
            Protocol = AiProtocol.OpenAiChat,
            DisplayName = "Other (OpenAI-compatible)",
            RequiresBaseUrl = true,
        },
    };

    /// <summary>Every provider the package can call, for the backoffice to list.</summary>
    public static IReadOnlyList<AiProviderProfile> All => Profiles;

    public static AiProviderProfile For(AiProvider provider)
        => Profiles.FirstOrDefault(p => p.Provider == provider)
           ?? Profiles.First(p => p.Provider == AiProvider.Custom);

    /// <summary>
    /// Works out the provider from the key. Prefixes are checked longest-first, because several are
    /// prefixes of each other - an OpenRouter key also starts with the OpenAI <c>sk-</c>.
    /// <para>
    /// Returns null when the key's shape says nothing useful, in which case the provider has to be
    /// chosen by hand rather than guessed at.
    /// </para>
    /// </summary>
    public static AiProvider? Detect(string? apiKey)
    {
        var key = (apiKey ?? string.Empty).Trim();

        if (key.Length < 8)
        {
            return null;
        }

        if (key.StartsWith("sk-ant-", StringComparison.OrdinalIgnoreCase))
        {
            return AiProvider.Anthropic;
        }

        if (key.StartsWith("sk-or-", StringComparison.OrdinalIgnoreCase))
        {
            return AiProvider.OpenRouter;
        }

        if (key.StartsWith("gsk_", StringComparison.OrdinalIgnoreCase))
        {
            return AiProvider.Groq;
        }

        // Google keys are the one common form with no separator to anchor on.
        if (key.StartsWith("AIza", StringComparison.Ordinal))
        {
            return AiProvider.Gemini;
        }

        if (key.StartsWith("sk-proj-", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("sk-svcacct-", StringComparison.OrdinalIgnoreCase))
        {
            return AiProvider.OpenAi;
        }

        // Plain "sk-" is shared by OpenAI, DeepSeek and several others, so it only narrows things to
        // the OpenAI shape - which is the right guess, since that shape is what they all speak.
        if (key.StartsWith("sk-", StringComparison.OrdinalIgnoreCase))
        {
            return AiProvider.OpenAi;
        }

        return null;
    }

    /// <summary>
    /// Picks a model out of the list a provider actually returned, so adding a key is enough to get
    /// a working setup instead of leaving an editor to guess a slug.
    /// <para>
    /// Order: the package's own documented default if the provider offers it; then the provider's
    /// preferred shapes; then the cheapest model that costs something; then whatever came first.
    /// Nothing is ever invented - every candidate comes from the provider's own list.
    /// </para>
    /// </summary>
    public static AiModelInfo? SuggestModel(AiProvider provider, IReadOnlyList<AiModelInfo> models)
    {
        if (models.Count == 0)
        {
            return null;
        }

        AiProviderProfile profile = For(provider);

        if (!string.IsNullOrWhiteSpace(profile.DefaultModel))
        {
            AiModelInfo? exact = models.FirstOrDefault(m =>
                m.Id.Equals(profile.DefaultModel, StringComparison.OrdinalIgnoreCase));

            if (exact is not null)
            {
                return exact;
            }
        }

        foreach (var hint in profile.PreferredModelHints)
        {
            // Cheapest match for the hint, so "mini" or "flash" lands on the cheap one rather than
            // whichever happens to be listed first.
            AiModelInfo? match = models
                .Where(m => m.Id.Contains(hint, StringComparison.OrdinalIgnoreCase) && !m.IsFree)
                .OrderBy(m => m.PromptPricePerMillion ?? decimal.MaxValue)
                .FirstOrDefault();

            if (match is not null)
            {
                return match;
            }
        }

        // Free tiers are skipped deliberately: on aggregators they are heavily rate limited and
        // routinely answer "unavailable for free", which looks like a broken setup.
        AiModelInfo? cheapest = models
            .Where(m => m.PromptPricePerMillion is > 0m)
            .OrderBy(m => m.PromptPricePerMillion)
            .FirstOrDefault();

        return cheapest ?? models.FirstOrDefault(m => !m.IsFree) ?? models[0];
    }

    /// <summary>
    /// Settles provider, model and base URL from the rules and the key together, so callers work
    /// with resolved values rather than repeating the fallback logic.
    /// </summary>
    public static AiResolvedTarget Resolve(AiRules rules, string? apiKey)
    {
        AiProvider provider = rules.Provider == AiProvider.Auto
            ? Detect(apiKey) ?? AiProvider.Custom
            : rules.Provider;

        AiProviderProfile profile = For(provider);

        var baseUrl = string.IsNullOrWhiteSpace(rules.BaseUrl)
            ? profile.BaseUrl
            : rules.BaseUrl.Trim().TrimEnd('/');

        var model = string.IsNullOrWhiteSpace(rules.Model)
            ? profile.DefaultModel
            : rules.Model.Trim();

        return new AiResolvedTarget
        {
            Provider = provider,
            Protocol = profile.Protocol,
            DisplayName = profile.DisplayName,
            BaseUrl = baseUrl,
            Model = model,
            ModelsDocumentation = profile.ModelsDocumentation,
            KeyLooksLike = Detect(apiKey),
        };
    }
}

/// <summary>Provider, model and endpoint after the rules and the key have both had their say.</summary>
public sealed class AiResolvedTarget
{
    public required AiProvider Provider { get; init; }

    public required AiProtocol Protocol { get; init; }

    public required string DisplayName { get; init; }

    public required string BaseUrl { get; init; }

    public required string Model { get; init; }

    public string ModelsDocumentation { get; init; } = string.Empty;

    /// <summary>
    /// Provider the stored key's own shape points at, or null when its shape says nothing. Used
    /// only to catch a key being sent to the wrong provider.
    /// </summary>
    public AiProvider? KeyLooksLike { get; init; }

    /// <summary>
    /// Why this target cannot be called yet, or null when it can. Checked before a request is
    /// built, so a misconfiguration reads as a sentence rather than a provider's own 404.
    /// </summary>
    public string? Problem
    {
        get
        {
            // A key that plainly belongs elsewhere is caught here rather than being sent off and
            // rejected: the provider's own "invalid key" reads as a bad key, when the real problem
            // is a stale provider setting. Only flagged when the key's shape is recognised and the
            // target is a named provider - Azure and self-hosted endpoints take any key shape.
            if (KeyLooksLike is { } detected
                && detected != Provider
                && Provider is not (AiProvider.Custom or AiProvider.AzureOpenAi))
            {
                var owner = AiProviderCatalog.For(detected).DisplayName;

                return $"The key looks like it belongs to {owner}, but the provider is set to "
                       + $"{DisplayName}. Switch the provider to {owner}, or paste a key for {DisplayName}.";
            }

            if (string.IsNullOrWhiteSpace(BaseUrl))
            {
                return $"{DisplayName} needs an API base URL - its host is specific to your account.";
            }

            if (string.IsNullOrWhiteSpace(Model))
            {
                return $"{DisplayName} needs a model name. Model names change often, so the package "
                       + "does not guess one"
                       + (string.IsNullOrWhiteSpace(ModelsDocumentation)
                           ? "."
                           : $"; the current list is at {ModelsDocumentation}.");
            }

            return null;
        }
    }
}
