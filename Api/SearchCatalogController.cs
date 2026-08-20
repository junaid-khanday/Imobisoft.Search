using Imobisoft.Search.Models;
using Imobisoft.Search.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Imobisoft.Search.Api;

/// <summary>
/// Read-only discovery endpoints that populate the dashboard's pick-lists. Everything here is read
/// from the running site, so a consumer's own Examine indexes and document types appear without the
/// package having to know about them.
/// </summary>
public sealed class SearchCatalogController : ImobisoftSearchControllerBase
{
    private readonly IIndexCatalogService _catalog;

    public SearchCatalogController(IIndexCatalogService catalog) => _catalog = catalog;

    /// <summary>Indexes, document types, media types and languages in one call.</summary>
    [HttpGet("catalog")]
    [ProducesResponseType(typeof(SearchCatalog), StatusCodes.Status200OK)]
    public IActionResult GetCatalog() => Ok(_catalog.GetCatalog());

    /// <summary>
    /// Every registered Examine index. Pass <c>includeFields=false</c> for a fast health-only view,
    /// which is what the index status dashboard polls.
    /// </summary>
    [HttpGet("catalog/index")]
    [ProducesResponseType(typeof(IEnumerable<IndexInfo>), StatusCodes.Status200OK)]
    public IActionResult GetIndexes([FromQuery] bool includeFields = true)
        => Ok(_catalog.GetIndexes(includeFields));

    /// <summary>A single index with its discovered fields.</summary>
    [HttpGet("catalog/index/{name}")]
    [ProducesResponseType(typeof(IndexInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetIndex(string name)
    {
        IndexInfo? index = _catalog.GetIndex(name);
        return index is null ? NotFound() : Ok(index);
    }

    /// <summary>The fields in an index that are worth searching, for the field weighting editor.</summary>
    [HttpGet("catalog/index/{name}/searchable-field")]
    [ProducesResponseType(typeof(IEnumerable<IndexFieldInfo>), StatusCodes.Status200OK)]
    public IActionResult GetSearchableFields(string name) => Ok(_catalog.GetSearchableFields(name));

    [HttpGet("catalog/content-type")]
    [ProducesResponseType(typeof(IEnumerable<ContentTypeInfo>), StatusCodes.Status200OK)]
    public IActionResult GetContentTypes() => Ok(_catalog.GetContentTypes());

    [HttpGet("catalog/media-type")]
    [ProducesResponseType(typeof(IEnumerable<ContentTypeInfo>), StatusCodes.Status200OK)]
    public IActionResult GetMediaTypes() => Ok(_catalog.GetMediaTypes());

    [HttpGet("catalog/language")]
    [ProducesResponseType(typeof(IEnumerable<LanguageInfo>), StatusCodes.Status200OK)]
    public IActionResult GetLanguages() => Ok(_catalog.GetLanguages());
}
