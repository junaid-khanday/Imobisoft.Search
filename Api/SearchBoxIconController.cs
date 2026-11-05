using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.IO;

namespace Imobisoft.Search.Api;

/// <summary>What the dashboard needs back after an upload: where the icon now lives.</summary>
public sealed class SearchBoxIconUploadResult
{
    /// <summary>URL to store on the profile's search box rules and render on the site.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>File name as stored, for showing in the dashboard.</summary>
    public string FileName { get; set; } = string.Empty;
}

/// <summary>
/// Takes a search icon uploaded from the editor's own machine, so choosing one is a file picker
/// rather than a path that has to be typed and has to already exist on the server.
/// <para>
/// The file goes into Umbraco's media filesystem, which means it is served like any other media -
/// and keeps working on a site whose media lives in blob storage rather than on disk. The name is
/// the hash of the contents, so uploading the same icon twice reuses one file and a changed icon
/// always gets a URL browsers have not cached.
/// </para>
/// </summary>
public sealed class SearchBoxIconController : ImobisoftSearchControllerBase
{
    /// <summary>An icon is a few kilobytes; this is generous and still refuses a stray photo.</summary>
    private const long MaximumBytes = 512 * 1024;

    /// <summary>Folder inside the media filesystem, so these files are obviously the package's.</summary>
    private const string Folder = "imobisoft-search";

    /// <summary>
    /// What a browser will actually render inside an <c>img</c> element. Anything else would leave
    /// an editor with a silently broken icon.
    /// </summary>
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".svg", ".png", ".webp", ".gif", ".jpg", ".jpeg", ".ico", ".avif",
    };

    /// <summary>
    /// Markup an SVG icon has no business carrying. An SVG is served from the site's own origin, so
    /// a visitor who opened the file directly would run whatever script it held - this is a basic
    /// check against the obvious cases, not a full sanitiser, and the endpoint is in any case open
    /// only to a backoffice user who already holds the Search section.
    /// </summary>
    private static readonly string[] ScriptMarkers =
    {
        "<script", "javascript:", "onload=", "onerror=", "onclick=", "onmouseover=", "<foreignobject",
    };

    private readonly MediaFileManager _mediaFileManager;

    public SearchBoxIconController(MediaFileManager mediaFileManager) => _mediaFileManager = mediaFileManager;

    [HttpPost("search-box-icon")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaximumBytes * 2)]
    [ProducesResponseType(typeof(SearchBoxIconUploadResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Upload([FromForm] IFormFile? file, CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { title = "No file", detail = "Choose an image file to upload." });
        }

        if (file.Length > MaximumBytes)
        {
            return BadRequest(new
            {
                title = "File too large",
                detail = $"An icon must be {MaximumBytes / 1024} KB or smaller; this one is {file.Length / 1024} KB.",
            });
        }

        var extension = Path.GetExtension(file.FileName);

        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
        {
            return BadRequest(new
            {
                title = "Unsupported file type",
                detail = "Use an image a browser can render: " + string.Join(", ", AllowedExtensions.Order()) + ".",
            });
        }

        // Read once into memory - an icon is tiny - so the contents can be both checked and hashed
        // before anything is written.
        byte[] contents;

        await using (Stream upload = file.OpenReadStream())
        {
            using var buffer = new MemoryStream();
            await upload.CopyToAsync(buffer, cancellationToken);
            contents = buffer.ToArray();
        }

        if (extension.Equals(".svg", StringComparison.OrdinalIgnoreCase) && CarriesScript(contents))
        {
            return BadRequest(new
            {
                title = "SVG contains script",
                detail = "This SVG carries script or event handlers. Export it as a plain shape, or upload a PNG instead.",
            });
        }

        var fileName = $"searchbox-icon-{Hash(contents)}{extension.ToLowerInvariant()}";
        var path = Folder + "/" + fileName;

        using (var source = new MemoryStream(contents))
        {
            _mediaFileManager.FileSystem.AddFile(path, source, true);
        }

        return Ok(new SearchBoxIconUploadResult
        {
            Url = _mediaFileManager.FileSystem.GetUrl(path),
            FileName = fileName,
        });
    }

    private static bool CarriesScript(byte[] contents)
    {
        var text = Encoding.UTF8.GetString(contents);

        return ScriptMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Content-addressed name: the same icon uploaded twice is one file, and a different icon can
    /// never be hidden behind a URL a browser has already cached.
    /// </summary>
    private static string Hash(byte[] contents)
        => Convert.ToHexString(SHA256.HashData(contents))[..10].ToLowerInvariant();
}
