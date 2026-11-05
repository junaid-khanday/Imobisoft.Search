using Imobisoft.Search.Models;
using Imobisoft.Search.Services.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Imobisoft.Search.Web;

/// <summary>What the site's JavaScript posts: which result, which action, and into which language.</summary>
public sealed class AiTransformRequest
{
    /// <summary>One of <c>summarise</c>, <c>shorten</c>, <c>keyPoints</c>, <c>translate</c>.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>The result's node key. The text is read server-side from this, never sent by the browser.</summary>
    public Guid Key { get; set; }

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

    public ImobisoftSearchAiController(IAiTextService ai) => _ai = ai;

    [HttpPost("")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Transform(
        [FromBody] AiTransformRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || request.Key == Guid.Empty)
        {
            return BadRequest(new { error = "A result key is required." });
        }

        if (!Enum.TryParse(request.Action, ignoreCase: true, out AiAction action))
        {
            return BadRequest(new { error = "Unknown action." });
        }

        AiTextResult result = await _ai.TransformAsync(
            new AiTextRequest
            {
                Action = action,
                Key = request.Key,
                Language = request.Language,
                Caller = CallerId(),
                ProfileAlias = string.IsNullOrWhiteSpace(request.Profile) ? null : request.Profile,
            },
            cancellationToken);

        if (!result.Ok)
        {
            return StatusCode(result.Status, new { error = result.Error });
        }

        return Ok(new { text = result.Text, cached = result.Cached });
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
