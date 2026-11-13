using System.Text.Json.Serialization;

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

    /// <summary>
    /// An answer to the visitor's own question about the page.
    /// <para>
    /// The one action that carries text a visitor typed. It is answered strictly from the page, and
    /// the question is framed as data rather than instructions - but it is still the widest surface
    /// here, and the costliest, because a distinct question cannot be served from the cache.
    /// </para>
    /// </summary>
    Ask,

    /// <summary>
    /// A direct answer to the search itself, shown above the results rather than offered on a
    /// button.
    /// <para>
    /// Not part of <see cref="AiRules.Actions"/>: that list is the buttons a visitor can press, and
    /// this one is not pressed. It is governed by <see cref="AiSearchRules.AnswerCard"/> instead.
    /// </para>
    /// </summary>
    Overview,

    /// <summary>
    /// Searches worth trying next, drawn from what the site actually holds.
    /// <para>
    /// Also not a button. Governed by <see cref="AiSearchRules.RelatedSearches"/>, and returned as
    /// one search per line so the site can render them as links.
    /// </para>
    /// </summary>
    RelatedSearches,
}

/// <summary>
/// AI inside the search itself, as opposed to the text tools a visitor presses a button for.
/// <para>
/// Three distinct things, each switched on separately because each costs money on a different
/// schedule. Query understanding and re-ranking run <em>during</em> a search, so they are the two
/// that can make a visitor wait - both are bounded by <see cref="TimeoutMilliseconds"/> and a search
/// that outruns it is served exactly as it would have been without any of this. The answer card is
/// fetched by the browser after the results are already on screen, so it never delays them.
/// </para>
/// <para>
/// All off by default. Installing the package must not start spending money at a provider, and these
/// spend it per search rather than per button press.
/// </para>
/// </summary>
public sealed class AiSearchRules
{
    /// <summary>
    /// Whether these stages may run at all, independently of the text tools.
    /// <para>
    /// There used to be one switch for both halves of AI, and it was the one on the text tools box
    /// - so a site that wanted its results re-ordered had to put summarise buttons on the page to
    /// get it, and a site that turned the buttons off silently lost query understanding and the
    /// answer card with them. Two features that cost money on different schedules need two
    /// switches.
    /// </para>
    /// <para>
    /// Null means a profile saved before this existed, and defers to <see cref="AiRules.Enabled"/>
    /// - so an editor who had switched AI off keeps it off, rather than finding that re-ranking
    /// resumed spending at their provider because it gained a switch of its own. The backoffice
    /// always writes a real value, so a profile saved once is decided from then on.
    /// </para>
    /// </summary>
    public bool? Enabled { get; set; }

    /// <summary>
    /// Reads the query before it runs: corrects the spelling, adds the words the site itself uses
    /// for the same thing, and lifts filters and a sort order out of a sentence - so "3 bed houses
    /// in Leeds added this month, cheapest first" becomes a term, two filters and a sort.
    /// <para>
    /// This is the one that changes what a search finds rather than how it is ordered. Everything
    /// it produces is checked against the profile first: a filter it invents that the profile does
    /// not define is dropped, and so is a sort option that is not enabled.
    /// </para>
    /// </summary>
    public bool UnderstandQuery { get; set; }

    /// <summary>
    /// Lets query understanding correct the spelling of the term. Separate from the "did you mean"
    /// suggestion below the box: this one rewrites the query that actually runs.
    /// </summary>
    public bool CorrectSpelling { get; set; } = true;

    /// <summary>
    /// Lets query understanding add words the site uses for the same thing, which are searched
    /// alongside what the visitor typed rather than instead of it.
    /// </summary>
    public bool ExpandSynonyms { get; set; } = true;

    /// <summary>
    /// Lets query understanding select the profile's own filters and sort order from the query.
    /// Nothing is selected that the profile does not already offer a visitor.
    /// </summary>
    public bool ExtractFilters { get; set; } = true;

    /// <summary>
    /// Re-orders the best matches by how well they actually answer the query, rather than by how
    /// many times its words appear. This is the fix for a page that is plainly the right answer and
    /// ranks eighth because it says the word once.
    /// <para>
    /// Applies to the first page only, and only when the visitor has not chosen a sort order of
    /// their own - a "Sort by: newest" dropdown is an instruction, and reordering underneath it
    /// would be ignoring it. Best bets stay pinned where the profile pinned them.
    /// </para>
    /// </summary>
    public bool Rerank { get; set; }

    /// <summary>
    /// How many of the top matches are re-ordered. The cost of one re-rank is roughly this many
    /// titles and extracts, so it is the cost lever: 20 is deep enough to pull up a result keyword
    /// scoring buried and cheap enough to leave on.
    /// </summary>
    public int RerankDepth { get; set; } = 20;

    /// <summary>
    /// Answers the search above the results, with links to the pages the answer came from.
    /// <para>
    /// Fetched by the browser once the results are rendered, so the page is never held up waiting
    /// for it. Cached per term like every other answer, so a popular search is paid for once.
    /// </para>
    /// </summary>
    public bool AnswerCard { get; set; }

    /// <summary>
    /// Offers searches to try next, drawn from the pages this search found rather than from a list
    /// someone maintains. Fetched alongside the answer card and cached the same way.
    /// </summary>
    public bool RelatedSearches { get; set; }

    /// <summary>
    /// How long a search will wait for the stages that run inside it. Past this the search carries
    /// on without them: a slow provider must cost a visitor nothing but the AI, and a search that
    /// returns late is worse than one that returns unaided.
    /// </summary>
    public int TimeoutMilliseconds { get; set; } = 2500;

    /// <summary>
    /// Calls each stage may make per minute, counted across the whole site.
    /// <para>
    /// This is the one that matters most on a public site, and it is not the same thing as the
    /// per-visitor limit on the text tools. A visitor presses a button; a search just happens. A
    /// crawler walking the search page with a different term every time is not one caller hitting a
    /// limit, and a term nobody has searched before cannot come from the cache - so without a
    /// ceiling here, how much a site spends is decided by how often it is crawled.
    /// </para>
    /// <para>
    /// Past the ceiling, searches are served exactly as they would be with this switched off.
    /// </para>
    /// </summary>
    public int MaxCallsPerMinute { get; set; } = 60;
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
    /// Turns the text tools on - the buttons a visitor presses on a result. Off, no buttons are
    /// injected into the site and the public endpoint refuses every press, which is also what
    /// happens when no key has been added yet.
    /// <para>
    /// The text tools only. AI inside the search has its own switch,
    /// <see cref="AiSearchRules.Enabled"/>, and neither half waits on the other.
    /// </para>
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Whether AI inside the search may run for this profile: the search half's own switch, or the
    /// text tools switch for a profile saved before it had one.
    /// </summary>
    [JsonIgnore]
    public bool SearchEnabled => Search.Enabled ?? Enabled;

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

    /// <summary>
    /// Which actions appear on a result. An empty list offers none of them.
    /// <para>
    /// The buttons only. <see cref="AiAction.Overview"/> and <see cref="AiAction.RelatedSearches"/>
    /// are not pressed by anyone and are governed by <see cref="Search"/>.
    /// </para>
    /// </summary>
    public IList<AiAction> Actions { get; set; } = new List<AiAction>
    {
        AiAction.Summarise,
        AiAction.Shorten,
        AiAction.KeyPoints,
        AiAction.Translate,
        AiAction.Ask,
    };

    /// <summary>
    /// AI inside the search itself: reading the query, re-ordering the results, and answering above
    /// them. All off until switched on - see <see cref="AiSearchRules"/>.
    /// </summary>
    public AiSearchRules Search { get; set; } = new();

    /// <summary>
    /// Longest question a visitor may ask. Short on purpose: a question about one page needs a
    /// sentence, and a generous limit here is an invitation to paste something else entirely.
    /// </summary>
    public int MaxQuestionCharacters { get; set; } = 300;

    /// <summary>
    /// How many pages a question may be answered from. A question is answered from the whole site,
    /// so the package searches first and reads the best matches - this is how many it reads.
    /// <para>
    /// It is the main cost lever for questions: every page consulted is more text sent. Three or
    /// four is usually enough to answer and cheap enough to leave switched on.
    /// </para>
    /// </summary>
    public int MaxPagesConsulted { get; set; } = 4;

    /// <summary>
    /// Which generation of the <see cref="Actions"/> list this profile was last saved with.
    /// <para>
    /// Without this there is no way to tell a profile saved before an action existed from one where
    /// an editor deliberately switched that action off - the stored list looks identical. So a new
    /// action is added once, to profiles that predate it, and the version is stamped; after that an
    /// editor's choice stands and nothing is re-added behind their back.
    /// </para>
    /// </summary>
    public int ActionsVersion { get; set; }

    /// <summary>Generation of the action set this build of the package ships.</summary>
    public const int CurrentActionsVersion = 2;

    /// <summary>
    /// Offers actions added since this profile was saved. Called when a profile is read, so a new
    /// action appears on the site without an editor having to find and tick it.
    /// </summary>
    public void ApplyActionsAddedSinceSave()
    {
        if (ActionsVersion >= CurrentActionsVersion)
        {
            return;
        }

        // Version 2 added Ask. Earlier profiles never had the chance to turn it off, so they get it.
        if (!Actions.Contains(AiAction.Ask))
        {
            Actions.Add(AiAction.Ask);
        }

        ActionsVersion = CurrentActionsVersion;
    }

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
