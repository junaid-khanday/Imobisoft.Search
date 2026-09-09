using System.Text.Json.Serialization;

namespace Imobisoft.Search.Models;

/// <summary>
/// How hard the model works on a request. Lower is faster and cheaper; a search page is a
/// latency-sensitive surface, so the package defaults low and lets a site trade up.
/// </summary>
[JsonConverter(typeof(CamelCaseEnumConverter<AiEffort>))]
public enum AiEffort
{
    Low,
    Medium,
    High,
}

/// <summary>Which engine writes the answer.</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<AiProvider>))]
public enum AiProvider
{
    /// <summary>
    /// The package's own answer engine. No key, no account, no network call and no cost: it reads
    /// the results the search already found and stitches the sentences that actually answer the
    /// question into a short summary.
    /// <para>
    /// This is the default, which is what lets the answer box work the moment the package is
    /// installed. It summarises rather than reasons - it can only surface an answer the pages
    /// state fairly directly - so a site that wants a written answer moves to a model provider.
    /// </para>
    /// </summary>
    Builtin,

    /// <summary>
    /// Anthropic's Claude, through the official SDK. Needs an API key and bills per search, and in
    /// exchange writes a real answer rather than quoting one, and unlocks query understanding and
    /// re-ranking.
    /// </summary>
    Anthropic,

    /// <summary>
    /// Any service that speaks OpenAI's <c>/v1/chat/completions</c> - which is nearly all of them.
    /// You supply the base URL, the key and the model name, so this one option covers OpenAI, Azure
    /// OpenAI, Google Gemini's compatibility endpoint, Groq, Mistral, DeepSeek, Together,
    /// OpenRouter, and a local Ollama or LM Studio.
    /// <para>
    /// Chosen over adding a named provider per vendor because the vendors already agreed on a wire
    /// format: one implementation, and a site can point it at something that did not exist when this
    /// package was built.
    /// </para>
    /// </summary>
    OpenAiCompatible,
}

/// <summary>
/// The AI answer shown above the result list: a short, grounded summary of what the search found.
/// <para>
/// It is written only from the results the search returned, never from the model's own knowledge,
/// which is what keeps it answering for <em>this</em> site rather than the internet at large.
/// </para>
/// </summary>
public sealed class AiAnswerSettings
{
    /// <summary>Whether an answer is generated at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How many of the top results are given to the model as source material. More sources cost
    /// more tokens and add latency; five is enough to answer most site-search questions.
    /// </summary>
    public int MaxSources { get; set; } = 5;

    /// <summary>Rough ceiling on the answer's length, in words. Kept short - this sits above the results, it does not replace them.</summary>
    public int MaxWords { get; set; } = 90;

    /// <summary>
    /// Adds a numbered citation per sentence, tying each claim back to the result it came from.
    /// This is what lets a visitor check the answer rather than take it on trust.
    /// </summary>
    public bool ShowSources { get; set; } = true;

    public AiEffort Effort { get; set; } = AiEffort.Low;
}

/// <summary>
/// Turns what a visitor typed into what the index can answer.
/// <para>
/// Someone types "how do I cancel my booking" and the index holds a page called "Cancellations and
/// refunds" - no word in common but "cancel". This rewrites the question into the terms the content
/// actually uses before the query runs.
/// </para>
/// </summary>
public sealed class AiQueryUnderstandingSettings
{
    /// <summary>
    /// Off by default: it puts a model call in front of every search, so it costs latency on the
    /// path a visitor is waiting on. Worth turning on for sites whose visitors ask questions rather
    /// than type keywords.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Extra terms the rewrite may add. The visitor's own words are always kept, so this widens the
    /// query rather than replacing it - a rewrite that misreads the question still finds what a
    /// plain keyword search would have.
    /// </summary>
    public int MaxAddedTerms { get; set; } = 6;

    /// <summary>
    /// Shortest term worth rewriting. One or two words are already keywords; there is nothing to
    /// understand and the call would only add latency.
    /// </summary>
    public int MinimumWords { get; set; } = 3;

    public AiEffort Effort { get; set; } = AiEffort.Low;
}

/// <summary>
/// Reorders the page of results by how well each one actually answers the question, rather than by
/// how many times the words matched.
/// </summary>
public sealed class AiRerankSettings
{
    /// <summary>
    /// Off by default: it adds a model call after the search, on the path the visitor is waiting on.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// How many of the top results are reordered. Everything below this keeps its original position,
    /// which bounds both the cost and how far a bad reorder can move things.
    /// </summary>
    public int TopN { get; set; } = 10;

    public AiEffort Effort { get; set; } = AiEffort.Low;
}

/// <summary>
/// Everything the package needs to put a model behind the search: the credential, which model, and
/// which of the three AI features are switched on.
/// <para>
/// All of it is off until a key is saved, and every feature degrades to the ordinary keyword search
/// when the model is unreachable, slow, or declines - an AI failure must never cost a visitor their
/// results.
/// </para>
/// </summary>
public sealed class AiSettings
{
    /// <summary>
    /// Master switch. On by default, because the default provider is the built-in one: it needs no
    /// key and costs nothing, so the answer box works on a fresh install rather than waiting for
    /// somebody to find a credit card. Off means search behaves exactly as it does without the
    /// add-on.
    /// </summary>
    public bool Enabled { get; set; } = true;

    private AiProvider? _provider;

    /// <summary>
    /// Which engine answers. Defaults to the built-in one - see <see cref="AiProvider.Builtin"/>.
    /// </summary>
    public AiProvider Provider
    {
        get => _provider ?? AiProvider.Builtin;
        set => _provider = value;
    }

    /// <summary>
    /// Whether the stored settings actually named a provider.
    /// <para>
    /// Settings written before the add-on had a free tier carry no provider and were saved with AI
    /// switched off, because at the time there was nothing to switch on without a paid key. Left
    /// alone, those sites would upgrade into a free feature that silently stays off. This is how the
    /// settings service tells "never chose" apart from "chose no" - a site that turns AI off today
    /// writes a provider alongside it, so its choice is preserved.
    /// </para>
    /// </summary>
    [JsonIgnore]
    public bool ProviderWasStored => _provider.HasValue;

    /// <summary>
    /// The Anthropic API key. Write-only across the management API: it is stored here but replaced
    /// with a mask whenever settings are read back, so it cannot be recovered through the backoffice
    /// once saved. See <see cref="Api.SearchSettingsController"/>.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Model id, as the chosen provider names it - <c>claude-opus-5</c>, <c>gpt-4o-mini</c>,
    /// <c>llama-3.3-70b-versatile</c>, whatever the endpoint expects. Free text rather than a fixed
    /// list, so a model released after this package cannot be locked out.
    /// </summary>
    public string Model { get; set; } = "claude-opus-5";

    /// <summary>
    /// Base URL for <see cref="AiProvider.OpenAiCompatible"/>, e.g.
    /// <c>https://api.openai.com/v1</c> or <c>http://localhost:11434/v1</c>. Ignored by the other
    /// providers. A trailing <c>/chat/completions</c> is appended by the client, so give the root.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// How long the model has to answer before the package gives up and serves the results without
    /// it. Deliberately short: a visitor waiting on a search page is the wrong place to be patient.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 12;

    /// <summary>
    /// How long an AI result is reused for the same search. The same question asked twice in an hour
    /// costs one model call, not two - this is the single biggest lever on what the add-on costs.
    /// 0 disables caching.
    /// </summary>
    public int CacheMinutes { get; set; } = 60;

    /// <summary>
    /// Ceiling on model calls per minute across the whole site. A traffic spike - or a crawler -
    /// stops costing money at this point and search quietly falls back to keyword results.
    /// 0 means no ceiling.
    /// </summary>
    public int MaxRequestsPerMinute { get; set; } = 60;

    public AiAnswerSettings Answer { get; set; } = new();

    public AiQueryUnderstandingSettings QueryUnderstanding { get; set; } = new();

    public AiRerankSettings Rerank { get; set; } = new();

    /// <summary>
    /// True when the add-on can actually produce something. The built-in provider needs nothing
    /// beyond being switched on; a model provider additionally needs a key and a model.
    /// </summary>
    [JsonIgnore]
    public bool IsUsable => Enabled
                            && Provider switch
                            {
                                AiProvider.Builtin => true,

                                // A local endpoint often needs no key at all, so the base URL is the
                                // requirement here rather than the credential.
                                AiProvider.OpenAiCompatible =>
                                    !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(Model),

                                _ => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(Model),
                            };

    /// <summary>
    /// True when answers come from a model rather than from the built-in extractor. Query
    /// understanding and re-ranking exist only on this side - there is no keyless equivalent of
    /// reasoning about a question.
    /// </summary>
    [JsonIgnore]
    public bool UsesModel => Provider != AiProvider.Builtin;

    /// <summary>
    /// Whether a key is stored, without disclosing it. Read-only, so it serialises out to the
    /// dashboard and is ignored on the way back in - the dashboard can show "a key is saved" while
    /// still having no way to read what it is.
    /// </summary>
    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);
}
