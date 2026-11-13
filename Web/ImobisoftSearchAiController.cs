using Imobisoft.Search.Configuration;
using Imobisoft.Search.Models;
using Imobisoft.Search.Services;
using Imobisoft.Search.Services.Ai;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Imobisoft.Search.Web;

/// <summary>What the site's JavaScript posts: which result, which action, and into which language.</summary>
public sealed class AiTransformRequest
{
    /// <summary>One of <c>summarise</c>, <c>shorten</c>, <c>keyPoints</c>, <c>translate</c>, <c>ask</c>.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// The visitor's question, for the <c>ask</c> action. Length-capped by the profile and framed
    /// as data rather than instructions; ignored by every other action.
    /// </summary>
    public string? Question { get; set; }

    /// <summary>
    /// What the visitor searched for. The server searches the site with this and reads the matching
    /// pages itself, so no page text is ever sent by the browser.
    /// </summary>
    public string? Term { get; set; }

    public string? Language { get; set; }

    /// <summary>Which profile's AI rules apply. Empty uses the default profile.</summary>
    public string? Profile { get; set; }
}

/// <summary>
/// The endpoint behind the summarise / shorten / key points / translate buttons on a search result.
/// <para>
/// This is reachable by the site's visitors, so it is also a way to spend the site's money at an AI
/// provider. Four things keep that contained, and all four matter: the action comes from a closed
/// set and the instructions are built server-side, so there is no prompt to inject; the body carries
/// a node key rather than text, so the only thing that can be processed is a page this site has
/// published; a per-visitor rate limit caps how often one caller can get through; and answers are
/// cached, so the same page is paid for once however many visitors ask.
/// </para>
/// </summary>
[AllowAnonymous]
[Route("~/" + ImobisoftSearchConstants.Web.SearchPagePath + "/ai")]
public sealed class ImobisoftSearchAiController : Controller
{
    private readonly IAiTextService _ai;
    private readonly ISearchProfileService _profiles;
    private readonly IImobisoftSearchService _search;
    private readonly IAiQueryPlanner _queryPlanner;
    private readonly IAiCredentialStore _credentials;
    private readonly ImobisoftSearchOptions _options;

    public ImobisoftSearchAiController(
        IAiTextService ai,
        ISearchProfileService profiles,
        IImobisoftSearchService search,
        IAiQueryPlanner queryPlanner,
        IAiCredentialStore credentials,
        IOptions<ImobisoftSearchOptions> options)
    {
        _ai = ai;
        _profiles = profiles;
        _search = search;
        _queryPlanner = queryPlanner;
        _credentials = credentials;
        _options = options.Value;
    }

    [HttpPost("")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Transform(
        [FromBody] AiTransformRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Nothing to do." });
        }

        if (!Enum.TryParse(request.Action, ignoreCase: true, out AiAction action))
        {
            return BadRequest(new { error = "Unknown action." });
        }

        AiTextResult result = await _ai.TransformAsync(
            new AiTextRequest
            {
                Action = action,
                Term = request.Term,
                Language = request.Language,
                Question = request.Question,
                Caller = CallerId(),
                ProfileAlias = string.IsNullOrWhiteSpace(request.Profile) ? null : request.Profile,
            },
            cancellationToken);

        if (!result.Ok)
        {
            return StatusCode(result.Status, new { error = result.Error });
        }

        return Ok(new
        {
            text = result.Text,
            cached = result.Cached,

            // Which pages the answer was drawn from, so the site can link them. This is the list the
            // server sent to the model, not one parsed back out of the answer.
            sources = result.Sources.Select(s => new { title = s.Title, url = s.Url }),
        });
    }

    /// <summary>
    /// What AI is switched on for this search, as the script asks for it.
    /// <para>
    /// This exists because the search page is a view a site is invited to take over - Search →
    /// Templates writes an editable copy into the project - and a site's own copy carries whichever
    /// data attributes it was copied with. Wiring a new feature through a new attribute therefore
    /// turns it off for every site that has ever customised the page: the script arrives, finds
    /// nothing it recognises, and does nothing. Asking the server instead means a feature switched
    /// on in the backoffice works on the page as it stands.
    /// </para>
    /// <para>
    /// Safe to leave open. It reports which buttons a profile offers and whether the answer card is
    /// on - the same things the markup it replaces said in plain sight - and reaches no paid
    /// service: the interpretation it reports is read from the cache the search itself filled, never
    /// worked out here.
    /// </para>
    /// </summary>
    [HttpGet("~/" + ImobisoftSearchConstants.Web.SearchPagePath + "/ai/state")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult State([FromQuery] string? q, [FromQuery] string? profile)
    {
        var alias = string.IsNullOrWhiteSpace(profile) ? null : profile;

        AiRules? rules = _ai.RulesFor(alias);

        // "One half of AI is on for this profile and there is a key", which is the least that could
        // put anything on the page. Either half on its own is enough: a profile running only the
        // answer card has no buttons, and one running only buttons has no card, and asking for both
        // here is what used to make the search half depend on the text tools being switched on.
        // Deliberately not IAiTextService.IsAvailable: that asks whether some button or card is
        // switched on, and a profile running only query understanding has neither while still
        // having something to report - the interpretation it applied to the visitor's query.
        if (rules is null || (!rules.Enabled && !rules.SearchEnabled) || !_credentials.Describe().HasKey)
        {
            // Not a 404: the script asks on every search page, and "nothing is on" is a perfectly
            // ordinary answer that should not read as a broken endpoint in a browser console.
            return Ok(new { enabled = false });
        }

        return Ok(new
        {
            enabled = true,

            // Each feature gated by its own deployment switch AND its own profile switch, the same
            // way TransformAsync gates the request that follows - so the page is never offered
            // something the endpoint would then refuse.
            actions = _options.EnableAiTextTools && rules.Enabled
                ? rules.Actions.Select(Camel).ToList()
                : new List<string>(),
            languages = rules.Languages,
            questionLimit = rules.MaxQuestionCharacters,
            answer = _options.EnableAiSearch && rules.SearchEnabled && rules.Search.AnswerCard,
            related = _options.EnableAiSearch && rules.SearchEnabled && rules.Search.RelatedSearches,
            notice = Notice(q, alias),
        });
    }

    /// <summary>
    /// What was done to the visitor's query, for the page to state - or null when it was run as
    /// typed.
    /// <para>
    /// Read from the plan the search already cached. A query nobody has searched yet has no plan
    /// here and reports nothing, which is correct: nothing has been done to it.
    /// </para>
    /// </summary>
    private object? Notice(string? term, string? profileAlias)
    {
        var typed = (term ?? string.Empty).Trim();

        if (typed.Length == 0)
        {
            return null;
        }

        SearchRuleSet? rules = _profiles.ResolveForSearch(profileAlias)?.Rules;

        if (rules is null)
        {
            return null;
        }

        AiQueryPlan? plan = _queryPlanner.Cached(typed, rules);

        if (plan is null)
        {
            return null;
        }

        List<string> filters = plan.DescribeFilters(rules);

        // An expansion only ever widens what matches, so on its own there is nothing to tell
        // anyone: the words they typed were all searched for.
        if (plan.Term.Length == 0 && filters.Count == 0)
        {
            return null;
        }

        return new
        {
            term = plan.Term.Length > 0 ? plan.Term : typed,
            original = typed,
            filters,
        };
    }

    /// <summary>
    /// An action as the script names it. The enum is PascalCase in C# and camelCase everywhere the
    /// browser sees it, and the markup this replaces did the same conversion inline.
    /// </summary>
    private static string Camel(AiAction action)
    {
        var name = action.ToString();

        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    /// <summary>
    /// The header search box, rendered from <c>Views/Partials/Search/HeaderSearchBox.cshtml</c>.
    /// <para>
    /// The injected script fetches this and drops the markup into the site's header, which is what
    /// makes that one file the thing that designs the header box. Before this existed the script
    /// built the box in JavaScript, so editing the view changed nothing unless a site happened to
    /// place the view component by hand.
    /// </para>
    /// </summary>
    [HttpGet("~/" + ImobisoftSearchConstants.Web.SearchPagePath + "/box")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Box([FromQuery] string? resultsUrl, [FromQuery] string? term)
    {
        SearchBoxRules rules = _profiles.ResolveForSearch(null)?.Rules.SearchBox ?? new SearchBoxRules();

        if (!_options.Enabled || !_options.EnableHeaderSearchBox || !rules.Enabled)
        {
            return NotFound();
        }

        var parameter = string.IsNullOrWhiteSpace(rules.QueryName) ? "q" : rules.QueryName;

        // Whether the term stays in the box after a search is a profile rule, so a site can have
        // every search start from an empty box if it prefers.
        var keepTerm = _profiles.ResolveForSearch(null)?.Rules.Matching.KeepSearchTerm ?? true;

        return PartialView("~/Views/Partials/Search/HeaderSearchBox.cshtml", new SearchBoxModel
        {
            // The link the script found decides where a search lands; the package's own page is the
            // fallback when it could not be read.
            Action = string.IsNullOrWhiteSpace(resultsUrl)
                ? "/" + ImobisoftSearchConstants.Web.SearchPagePath
                : resultsUrl.Trim(),
            QueryName = parameter,
            Term = keepTerm ? (term ?? string.Empty).Trim() : string.Empty,
            StartOpen = false,
            Placeholder = rules.Placeholder,
            ButtonLabel = rules.ButtonLabel,
            IconUrl = rules.IconUrl,
            IconSize = rules.IconSize > 0 ? rules.IconSize : 18,
            OpenWidth = "100%",
        });
    }

    /// <summary>
    /// Type-ahead suggestions for a partly typed term - type "ap" and get the words the site
    /// actually has.
    /// <para>
    /// The engine and its settings already existed for the backoffice test panel; this is the same
    /// thing reachable by the site's visitors. It is cheap and safe to leave open: suggestions come
    /// from what the profile can already see in the index, so it reveals nothing a search would not,
    /// and it reaches no paid service. Switched off with "Enable Spelling Suggestions &amp;
    /// Type-Ahead" in Search → Settings.
    /// </para>
    /// </summary>
    [HttpGet("~/" + ImobisoftSearchConstants.Web.SearchPagePath + "/suggest")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Suggest(
        [FromQuery] string? term,
        [FromQuery] int take = 0,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return NotFound();
        }

        var typed = (term ?? string.Empty).Trim();

        // A single character matches most of the index, so it is not worth a round trip.
        if (typed.Length < 2)
        {
            return Ok(Array.Empty<object>());
        }

        if (typed.Length > 100)
        {
            typed = typed[..100];
        }

        try
        {
            IReadOnlyList<SearchSuggestion> suggestions = await _search.AutocompleteAsync(
                typed,
                profileAlias: null,
                take: Math.Clamp(take, 0, 20),
                cancellationToken);

            return Ok(suggestions.Select(s => new { term = s.Text, url = s.Url }));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The visitor typed another letter before this answer came back, so the browser dropped
            // the request. Type-ahead does that on every keystroke, which makes a cancelled search
            // the normal course of events here rather than a fault - and one that must not be logged
            // as an error or surface as a 500. The token is still passed in, because abandoning the
            // Lucene work is the whole point of it being cancelled.
            //
            // Nothing is listening for this response.
            return Ok(Array.Empty<object>());
        }
    }

    /// <summary>
    /// Who to count against the rate limit. The client IP, taking the first hop of a forwarding
    /// header when one is present so that a site behind a proxy or CDN does not look like a single
    /// caller - without it, one visitor hitting the limit would lock out everyone.
    /// </summary>
    private string CallerId()
    {
        var forwarded = Request.Headers["X-Forwarded-For"].ToString();

        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            var first = forwarded.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(first))
            {
                return first;
            }
        }

        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
