using Imobisoft.Search.Services.Templates;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Imobisoft.Search.Api;

/// <summary>Body for saving a view.</summary>
public sealed class SearchTemplateSaveRequest
{
    public string Content { get; set; } = string.Empty;
}

/// <summary>The view list, plus whether an edit needs a rebuild to show up.</summary>
public sealed class SearchTemplateListResult
{
    public IList<SearchTemplateInfo> Templates { get; set; } = new List<SearchTemplateInfo>();

    /// <summary>False on a site without Razor runtime compilation, where a save needs a rebuild.</summary>
    public bool EditsApplyImmediately { get; set; }
}

/// <summary>
/// Shows the source of the views the package ships, and writes an editable copy into the site.
/// <para>
/// A Razor view is executable C#, so editing one is editing code that runs on the server - the same
/// capability Umbraco's own template editor carries. It is gated the same way as every other
/// endpoint here: a signed-in backoffice user who holds the Search section. Only the views the
/// package owns can be written, from a fixed list, so there is no path for a caller to supply.
/// </para>
/// </summary>
public sealed class SearchTemplateController : ImobisoftSearchControllerBase
{
    private readonly ISearchTemplateService _templates;

    public SearchTemplateController(ISearchTemplateService templates) => _templates = templates;

    /// <summary>Every view a site may take over, and which ones it already has.</summary>
    [HttpGet("templates")]
    [ProducesResponseType(typeof(SearchTemplateListResult), StatusCodes.Status200OK)]
    public IActionResult List() => Ok(new SearchTemplateListResult
    {
        Templates = _templates.List().ToList(),
        EditsApplyImmediately = _templates.EditsApplyImmediately,
    });

    /// <summary>The package's own source for one view, and the site's copy if it has one.</summary>
    [HttpGet("templates/{id}")]
    [ProducesResponseType(typeof(SearchTemplateContent), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult Get(string id)
    {
        SearchTemplateContent? content = _templates.Get(id);

        return content is null
            ? NotFound(new { title = "Unknown view", detail = "That is not a view this package owns." })
            : Ok(content);
    }

    /// <summary>Writes the site's own copy, which then wins over the package's.</summary>
    [HttpPut("templates/{id}")]
    [ProducesResponseType(typeof(SearchTemplateContent), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult Save(string id, [FromBody] SearchTemplateSaveRequest request)
    {
        var problem = _templates.Save(id, request?.Content ?? string.Empty);

        if (problem is not null)
        {
            return BadRequest(new { title = "Could not save", detail = problem });
        }

        return Ok(_templates.Get(id));
    }

    /// <summary>Removes the site's copy, putting the package's own view back in use.</summary>
    [HttpDelete("templates/{id}")]
    [ProducesResponseType(typeof(SearchTemplateContent), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult Reset(string id)
    {
        var problem = _templates.Reset(id);

        if (problem is not null)
        {
            return BadRequest(new { title = "Could not reset", detail = problem });
        }

        return Ok(_templates.Get(id));
    }
}
