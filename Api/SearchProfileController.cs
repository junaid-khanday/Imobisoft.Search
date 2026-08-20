using Imobisoft.Search.Models;
using Imobisoft.Search.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Imobisoft.Search.Api;

/// <summary>
/// CRUD for search profiles. This is what the dashboard's profile editor talks to.
/// </summary>
public sealed class SearchProfileController : ImobisoftSearchControllerBase
{
    private readonly ISearchProfileService _profileService;

    public SearchProfileController(ISearchProfileService profileService) => _profileService = profileService;

    /// <summary>Every saved profile, default first.</summary>
    [HttpGet("profile")]
    [ProducesResponseType(typeof(IEnumerable<SearchProfile>), StatusCodes.Status200OK)]
    public IActionResult GetAll() => Ok(_profileService.GetAll());

    /// <summary>A single profile.</summary>
    [HttpGet("profile/{key:guid}")]
    [ProducesResponseType(typeof(SearchProfile), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult Get(Guid key)
    {
        SearchProfile? profile = _profileService.Get(key);
        return profile is null ? NotFound() : Ok(profile);
    }

    /// <summary>
    /// Creates a profile. A brand new site's first profile automatically becomes the default.
    /// </summary>
    [HttpPost("profile")]
    [ProducesResponseType(typeof(SearchProfile), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public IActionResult Create([FromBody] SearchProfile profile)
    {
        SearchProfileResult result = _profileService.Create(profile);

        return result.Success
            ? CreatedAtAction(nameof(Get), new { key = result.Profile!.Key }, result.Profile)
            : Problem(result.Status);
    }

    /// <summary>Updates a profile's rules. The default flag is moved with <see cref="SetDefault"/> instead.</summary>
    [HttpPut("profile/{key:guid}")]
    [ProducesResponseType(typeof(SearchProfile), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult Update(Guid key, [FromBody] SearchProfile profile)
    {
        SearchProfileResult result = _profileService.Update(key, profile);
        return result.Success ? Ok(result.Profile) : Problem(result.Status);
    }

    /// <summary>Makes this the profile used when a search does not name one.</summary>
    [HttpPost("profile/{key:guid}/default")]
    [ProducesResponseType(typeof(SearchProfile), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult SetDefault(Guid key)
    {
        SearchProfileResult result = _profileService.SetDefault(key);
        return result.Success ? Ok(result.Profile) : Problem(result.Status);
    }

    /// <summary>Deletes a profile. The last remaining profile cannot be deleted.</summary>
    [HttpDelete("profile/{key:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult Delete(Guid key)
    {
        SearchProfileOperationStatus status = _profileService.Delete(key);
        return status == SearchProfileOperationStatus.Success ? NoContent() : Problem(status);
    }

    /// <summary>Turns a failed operation into a response the dashboard can show verbatim.</summary>
    private IActionResult Problem(SearchProfileOperationStatus status) => status switch
    {
        SearchProfileOperationStatus.NotFound => NotFound(),
        SearchProfileOperationStatus.DuplicateAlias => Problem(
            "Another search profile already uses that alias.",
            statusCode: StatusCodes.Status400BadRequest,
            title: "Duplicate alias"),
        SearchProfileOperationStatus.InvalidAlias => Problem(
            "An alias must be 1-255 characters and contain only letters, digits, dashes and underscores.",
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid alias"),
        SearchProfileOperationStatus.CannotDeleteLastProfile => Problem(
            "This is the only search profile. Create another before deleting this one.",
            statusCode: StatusCodes.Status400BadRequest,
            title: "Cannot delete the last profile"),
        _ => Problem(
            "The search profile could not be saved. Check the Umbraco log for details.",
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Save failed"),
    };
}
