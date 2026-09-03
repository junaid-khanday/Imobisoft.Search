using Imobisoft.Search.Models;
using Imobisoft.Search.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Services;

namespace Imobisoft.Search.Api;

/// <summary>
/// Read-only discovery endpoints that populate the dashboard's pick-lists. Everything here is read
/// from the running site, so a consumer's own Examine indexes and document types appear without the
/// package having to know about them.
/// </summary>
public sealed class SearchCatalogController : ImobisoftSearchControllerBase
{
    private readonly IIndexCatalogService _catalog;
    private readonly IContentService _content;
    private readonly IMediaService _media;

    public SearchCatalogController(
        IIndexCatalogService catalog,
        IContentService content,
        IMediaService media)
    {
        _catalog = catalog;
        _content = content;
        _media = media;
    }

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

    /// <summary>
    /// Resolves one node key to a display name, so the dashboard can show the page a picker chose
    /// rather than its raw key. Documents are tried first, then media. Accepts bare GUID keys,
    /// integer ids, and Udi strings such as "umb://document/&lt;guid&gt;".
    /// </summary>
    [HttpGet("catalog/node")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetNode([FromQuery] string? key)
    {
        if (!TryParseKey(key, out NodeKeyRef reference))
        {
            return NotFound();
        }

        var content =
            (reference.Guid is { } g ? _content.GetById(g) : null)
            ?? (reference.Id is { } ci ? _content.GetById(ci) : null);

        if (content is not null)
        {
            return Ok(new
            {
                key = content.Key,
                name = content.Name,
                kind = "content",
                icon = content.ContentType?.Icon ?? "icon-document",
            });
        }

        var media =
            (reference.Guid is { } mg ? _media.GetById(mg) : null)
            ?? (reference.Id is { } mi ? _media.GetById(mi) : null);

        if (media is not null)
        {
            return Ok(new
            {
                key = media.Key,
                name = media.Name,
                kind = "media",
                icon = media.ContentType?.Icon ?? "icon-picture",
            });
        }

        return NotFound();
    }

    private readonly record struct NodeKeyRef(Guid? Guid, int? Id);

    private static bool TryParseKey(string? raw, out NodeKeyRef reference)
    {
        reference = default;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim();

        if (Guid.TryParse(trimmed, out var guid))
        {
            reference = new NodeKeyRef(guid, null);
            return true;
        }

        // Pull the first embedded GUID out of Udi-style values.
        var match = System.Text.RegularExpressions.Regex.Match(
            trimmed,
            @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");

        if (match.Success && Guid.TryParse(match.Value, out guid))
        {
            reference = new NodeKeyRef(guid, null);
            return true;
        }

        if (int.TryParse(trimmed, out int id) && id > 0)
        {
            reference = new NodeKeyRef(null, id);
            return true;
        }

        return false;
    }
}
