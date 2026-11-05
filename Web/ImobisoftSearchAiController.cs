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
    private readonly ImobisoftSearchOptions _options;

    public ImobisoftSearchAiController(
        IAiTextService ai,
        ISearchProfileService profiles,
        IOptions<ImobisoftSearchOptions> options)
    {
        _ai = ai;
        _profiles = profiles;
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

        return PartialView("~/Views/Partials/Search/HeaderSearchBox.cshtml", new SearchBoxModel
        {
            // The link the script found decides where a search lands; the package's own page is the
            // fallback when it could not be read.
            Action = string.IsNullOrWhiteSpace(resultsUrl)
                ? "/" + ImobisoftSearchConstants.Web.SearchPagePath
                : resultsUrl.Trim(),
            QueryName = parameter,
            Term = (term ?? string.Empty).Trim(),
            StartOpen = false,
            Placeholder = rules.Placeholder,
            ButtonLabel = rules.ButtonLabel,
            IconUrl = rules.IconUrl,
            IconSize = rules.IconSize > 0 ? rules.IconSize : 18,
            OpenWidth = "100%",
        });
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
