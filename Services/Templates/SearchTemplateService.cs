using System.Reflection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Hosting;

namespace Imobisoft.Search.Services.Templates;

/// <summary>One view a site may take over, as the backoffice lists it.</summary>
public sealed class SearchTemplateInfo
{
    /// <summary>Stable id used in the API, so a path never travels in a URL.</summary>
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    /// <summary>Where a site's own copy lives, relative to the project root.</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>True when this site has its own copy, so the package's is no longer in use.</summary>
    public bool Overridden { get; init; }

    public DateTime? OverriddenAtUtc { get; init; }

    /// <summary>
    /// A page on this site where this view is actually rendered, so it can be previewed as a visitor
    /// sees it rather than only read as source. Empty where the package cannot know - a view a site
    /// placed itself could be on any page.
    /// </summary>
    public string PreviewPath { get; init; } = string.Empty;
}

/// <summary>The source of one view: what the package ships, and what the site has made of it.</summary>
public sealed class SearchTemplateContent
{
    public SearchTemplateInfo Info { get; init; } = new();

    /// <summary>The markup the package ships, read from the assembly. Always available, never written to.</summary>
    public string Default { get; init; } = string.Empty;

    /// <summary>The site's own copy, or null when it has none and the package's is in use.</summary>
    public string? Override { get; init; }
}

/// <summary>
/// Lets the backoffice show the source of the views the package ships, and write an editable copy
/// into the site so it can be changed without opening the project.
/// </summary>
public interface ISearchTemplateService
{
    IReadOnlyList<SearchTemplateInfo> List();

    SearchTemplateContent? Get(string id);

    /// <summary>Writes the site's own copy. Returns the problem, or null when it saved.</summary>
    string? Save(string id, string content);

    /// <summary>Deletes the site's copy, putting the package's own view back in use.</summary>
    string? Reset(string id);

    /// <summary>
    /// Whether an edit takes effect without rebuilding. False on a site without Razor runtime
    /// compilation, where a saved view is only picked up on the next build - worth saying plainly
    /// rather than leaving someone wondering why nothing changed.
    /// </summary>
    bool EditsApplyImmediately { get; }
}

/// <inheritdoc />
public sealed class SearchTemplateService : ISearchTemplateService
{
    /// <summary>
    /// The views a site may take over, and the only paths this service will ever write to. An
    /// allowlist rather than a path from the caller: nothing here can be pointed at a file the
    /// package does not own.
    /// </summary>
    private static readonly SearchTemplateDefinition[] Definitions =
    {
        new(
            "search-page",
            "Search Page",
            "The whole search page in one file - the search bar, the result cards, the filters, the "
            + "empty states and all of the styling. This is the file to edit to make search look "
            + "like the rest of the site.",
            "Views/ImobisoftSearch/SearchPage.cshtml",
            "/" + ImobisoftSearchConstants.Web.SearchPagePath),
        new(
            "header-search-box",
            "Header Search Box",
            "The search button and bar for a site header. Edit this to design the header search for "
            + "the consuming project.",
            "Views/Partials/Search/HeaderSearchBox.cshtml",
            "/"),
    };

    private static readonly Assembly PackageAssembly = typeof(SearchTemplateService).Assembly;

    private readonly IHostingEnvironment _hostingEnvironment;
    private readonly ILogger<SearchTemplateService> _logger;

    public SearchTemplateService(
        IHostingEnvironment hostingEnvironment,
        ILogger<SearchTemplateService> logger)
    {
        _hostingEnvironment = hostingEnvironment;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool EditsApplyImmediately { get; } = AppDomain.CurrentDomain
        .GetAssemblies()
        .Any(a => a.GetName().Name?.Equals(
            "Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation",
            StringComparison.OrdinalIgnoreCase) == true);

    /// <inheritdoc />
    public IReadOnlyList<SearchTemplateInfo> List()
        => Definitions.Select(Describe).ToList();

    /// <inheritdoc />
    public SearchTemplateContent? Get(string id)
    {
        SearchTemplateDefinition? definition = Find(id);

        if (definition is null)
        {
            return null;
        }

        var path = FullPath(definition);

        return new SearchTemplateContent
        {
            Info = Describe(definition),
            Default = ReadShipped(definition),
            Override = File.Exists(path) ? File.ReadAllText(path) : null,
        };
    }

    /// <inheritdoc />
    public string? Save(string id, string content)
    {
        SearchTemplateDefinition? definition = Find(id);

        if (definition is null)
        {
            return "That is not a view this package owns.";
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return "A view cannot be saved empty. Use Reset to go back to the package's own.";
        }

        try
        {
            var path = FullPath(definition);

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // Keep the previous copy. A Razor view that does not compile takes the page down, and
            // being able to put back what was working matters more than a tidy folder.
            if (File.Exists(path))
            {
                File.Copy(path, path + ".bak", true);
            }

            File.WriteAllText(path, content);

            _logger.LogInformation(
                "Imobisoft.Search wrote the site's own copy of {Template} to {Path}.",
                definition.Id,
                definition.RelativePath);

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Imobisoft.Search could not write {Template}.", definition.Id);

            return "Could not write the file: " + ex.Message;
        }
    }

    /// <inheritdoc />
    public string? Reset(string id)
    {
        SearchTemplateDefinition? definition = Find(id);

        if (definition is null)
        {
            return "That is not a view this package owns.";
        }

        try
        {
            var path = FullPath(definition);

            if (File.Exists(path))
            {
                // Moved aside rather than deleted, so a reset is not a way to lose work.
                File.Copy(path, path + ".bak", true);
                File.Delete(path);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Imobisoft.Search could not remove {Template}.", definition.Id);

            return "Could not remove the file: " + ex.Message;
        }
    }

    private static SearchTemplateDefinition? Find(string? id)
        => Definitions.FirstOrDefault(d => d.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private SearchTemplateInfo Describe(SearchTemplateDefinition definition)
    {
        var path = FullPath(definition);
        var exists = File.Exists(path);

        return new SearchTemplateInfo
        {
            Id = definition.Id,
            Name = definition.Name,
            Description = definition.Description,
            Path = definition.RelativePath,
            Overridden = exists,
            OverriddenAtUtc = exists ? File.GetLastWriteTimeUtc(path) : null,
            PreviewPath = definition.PreviewPath,
        };
    }

    /// <summary>
    /// Resolves to a path under the site's own root, built from the allowlist entry rather than
    /// from anything a caller sent - so there is no input to traverse out of.
    /// </summary>
    private string FullPath(SearchTemplateDefinition definition)
        => Path.GetFullPath(Path.Combine(
            _hostingEnvironment.MapPathContentRoot("~/"),
            definition.RelativePath.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>The markup the package ships, read from the assembly it is embedded in.</summary>
    private string ReadShipped(SearchTemplateDefinition definition)
    {
        var resource = "Imobisoft.Search." + definition.RelativePath
            .Replace("Views/", "Views.", StringComparison.Ordinal)
            .Replace('/', '.');

        using Stream? stream = PackageAssembly.GetManifestResourceStream(resource);

        if (stream is null)
        {
            _logger.LogWarning(
                "Imobisoft.Search could not find the embedded source for {Template} ({Resource}).",
                definition.Id,
                resource);

            return string.Empty;
        }

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    /// <summary>One allowlisted view.</summary>
    private sealed record SearchTemplateDefinition(
        string Id,
        string Name,
        string Description,
        string RelativePath,
        string PreviewPath = "");
}
