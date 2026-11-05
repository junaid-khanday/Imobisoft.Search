namespace Imobisoft.Search.Models;

/// <summary>
/// What a visitor can ask the AI to do to a result's text. The list is closed on purpose: the
/// browser sends an action from this set and never a prompt of its own, so a visitor cannot turn
/// the site's endpoint into a general-purpose chatbot on the site's own API bill.
/// </summary>
public enum AiAction
{
    /// <summary>A short prose summary of the page.</summary>
    Summarise,

    /// <summary>The same meaning in fewer words.</summary>
    Shorten,

    /// <summary>The main points as a short list.</summary>
    KeyPoints,

    /// <summary>The text in another language.</summary>
    Translate,
}

/// <summary>
/// Which service answers. <see cref="Auto"/> works out the provider from the shape of the key that
/// was pasted in, which is what lets an editor add a key and have it work without also knowing
/// which endpoint and header the vendor expects.
/// </summary>
public enum AiProvider
{
    /// <summary>Read the provider from the key itself.</summary>
    Auto,

    Anthropic,

    OpenAi,

    /// <summary>Google Gemini (the Generative Language API).</summary>
    Gemini,

    Groq,

    Mistral,

    DeepSeek,

    OpenRouter,

    /// <summary>
    /// Azure OpenAI. Needs the resource's own URL, because the host is per-subscription and the
    /// model is the name of a deployment rather than a published model id.
    /// </summary>
    AzureOpenAi,

    /// <summary>
    /// Anything else that speaks the OpenAI chat API - Together, Fireworks, a local Ollama or
    /// LM Studio. Needs a base URL.
    /// </summary>
    Custom,
}

/// <summary>
/// The live text tools the package offers on search results: summarise, shorten, pull out the key
/// points, or translate - run on the page the visitor is already on.
/// <para>
/// The model and its key are configured in the backoffice, on the profile editor's AI tab. The key
/// itself is never part of these rules: rules travel to the browser with the rest of the profile,
/// so the key is held separately and encrypted (see <c>IAiCredentialStore</c>).
/// </para>
/// </summary>
public sealed class AiRules
{
    /// <summary>
    /// Turns the text tools on. Off, nothing is injected into the site and the public endpoint
    /// refuses every request - which is also what happens when no key has been added yet.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Which service answers. Left on <see cref="AiProvider.Auto"/>, the key decides.</summary>
    public AiProvider Provider { get; set; } = AiProvider.Auto;

    /// <summary>
    /// Model to run. Empty uses the provider's default where the package knows one; providers whose
    /// model names change often need this filled in.
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// API base URL. Empty uses the provider's published endpoint. Required for
    /// <see cref="AiProvider.AzureOpenAi"/> and <see cref="AiProvider.Custom"/>.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Which actions appear on a result. An empty list offers none of them.</summary>
    public IList<AiAction> Actions { get; set; } = new List<AiAction>
    {
        AiAction.Summarise,
        AiAction.Shorten,
        AiAction.KeyPoints,
        AiAction.Translate,
    };

    /// <summary>Languages offered for <see cref="AiAction.Translate"/>.</summary>
    public IList<string> Languages { get; set; } = new List<string> { "English", "French", "German", "Spanish" };

    /// <summary>
    /// How much of a page's text is sent. A ceiling here is a ceiling on the cost of one request,
    /// so it is a cost control as much as a quality one.
    /// </summary>
    public int MaxInputCharacters { get; set; } = 6000;

    /// <summary>
    /// Ceiling on what the model may generate in reply. On a model that thinks before answering
    /// this covers the thinking too, so it needs room above the length of the answer itself.
    /// </summary>
    public int MaxOutputTokens { get; set; } = 2048;

    /// <summary>
    /// How long an answer is reused. Summarising the same page for the next visitor costs nothing,
    /// so this is the single most effective cost control here. 0 disables caching.
    /// </summary>
    public int CacheMinutes { get; set; } = 1440;

    /// <summary>
    /// Requests a single visitor may make per minute. The endpoint is public - it has to be, the
    /// site's visitors call it - so this is what stops one caller running up the bill.
    /// </summary>
    public int RequestsPerMinutePerVisitor { get; set; } = 8;
}
