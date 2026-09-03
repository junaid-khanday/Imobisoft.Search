using System.Collections.Concurrent;
using Imobisoft.Search.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Razor.Compilation;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace Imobisoft.Search.Services;

/// <inheritdoc />
public sealed class SearchThemeService : ISearchThemeService
{
    /// <summary>
    /// Where themes are looked for. A site authors them here; the package ships none, so the list
    /// is whatever the site has created (plus the built-in look, which is the absence of a theme).
    /// Compiled views are scanned too, so a Razor class library may contribute themes as well.
    /// </summary>
    private const string ThemeRoot = "Views/Partials/Search/Themes";

    /// <summary>Where a part falls back to when the chosen theme does not define it.</summary>
    private const string BaseRoot = "~/Views/Partials/Search";

    /// <summary>
    /// The pieces a theme may replace. A theme defining none of these is still listed - it simply
    /// renders exactly like the built-in look.
    /// </summary>
    private static readonly string[] Parts = { "styles", "results", "filters", "noresults", "loadmore", "searchbar" };

    private readonly IRazorViewEngine _viewEngine;
    private readonly ApplicationPartManager _partManager;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<SearchThemeService> _logger;

    /// <summary>Resolved "theme|part" to view path. Themes do not change between deployments.</summary>
    private readonly ConcurrentDictionary<string, string> _resolved = new(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<SearchThemeInfo>? _themes;

    public SearchThemeService(
        IRazorViewEngine viewEngine,
        ApplicationPartManager partManager,
        IWebHostEnvironment environment,
        ILogger<SearchThemeService> logger)
    {
        _viewEngine = viewEngine;
        _partManager = partManager;
        _environment = environment;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<SearchThemeInfo> GetThemes() => _themes ??= BuildThemes();

    /// <inheritdoc />
    public string ResolvePartial(string? theme, string part)
    {
        var fallback = $"{BaseRoot}/{part}.cshtml";

        if (string.IsNullOrWhiteSpace(theme) || !Parts.Contains(part, StringComparer.OrdinalIgnoreCase))
        {
            return fallback;
        }

        return _resolved.GetOrAdd($"{theme}|{part}", _ =>
        {
            var candidate = $"~/{ThemeRoot}/{theme.Trim()}/{part}.cshtml";

            // The view engine is the only check that sees both worlds at once: partials compiled
            // into this package and .cshtml files the site dropped in. A theme that does not carry
            // this part - or a theme name that no longer exists - simply falls back.
            return Exists(candidate) ? candidate : fallback;
        });
    }

    private bool Exists(string viewPath)
    {
        try
        {
            return _viewEngine.GetView(executingFilePath: null, viewPath: viewPath, isMainPage: false).Success;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not probe for the search theme view {Path}.", viewPath);
            return false;
        }
    }

    private IReadOnlyList<SearchThemeInfo> BuildThemes()
    {
        // Site themes are collected after package themes and overwrite them by name, which is the
        // same "the site's copy wins" rule the rest of the package's views follow.
        var found = new Dictionary<string, SearchThemeInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in CompiledThemeNames())
        {
            found[name] = new SearchThemeInfo { Name = name, Label = Humanise(name), IsSiteProvided = false };
        }

        foreach (var name in SiteThemeNames())
        {
            found[name] = new SearchThemeInfo { Name = name, Label = Humanise(name), IsSiteProvided = true };
        }

        foreach (SearchThemeInfo theme in found.Values)
        {
            theme.Parts = Parts
                .Where(p => Exists($"~/{ThemeRoot}/{theme.Name}/{p}.cshtml"))
                .ToList();
        }

        var themes = new List<SearchThemeInfo>
        {
            // The empty name is what a profile carries when no theme has been chosen, so it has to
            // be selectable rather than only being the implicit default.
            new() { Name = string.Empty, Label = "Default (built-in)", Parts = new List<string>() },
        };

        themes.AddRange(found.Values.OrderBy(t => t.Label, StringComparer.OrdinalIgnoreCase));

        return themes;
    }

    /// <summary>
    /// Theme folders compiled into an assembly - this package's own, and any Razor class library a
    /// site has installed that ships search themes of its own.
    /// </summary>
    private IEnumerable<string> CompiledThemeNames()
    {
        var feature = new ViewsFeature();

        try
        {
            _partManager.PopulateFeature(feature);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read compiled views while listing search themes.");
            return Array.Empty<string>();
        }

        return feature.ViewDescriptors
            .Select(d => ThemeNameFromPath(d.RelativePath))
            .Where(n => n is not null)
            .Select(n => n!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Theme folders the consuming site added under its own Views folder.</summary>
    private IEnumerable<string> SiteThemeNames()
    {
        try
        {
            IDirectoryContents contents = _environment.ContentRootFileProvider.GetDirectoryContents(ThemeRoot);

            return !contents.Exists
                ? Array.Empty<string>()
                : contents.Where(f => f.IsDirectory).Select(f => f.Name).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not list site search themes under {Root}.", ThemeRoot);
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Pulls "Compact" out of "/Views/Partials/Search/Themes/Compact/results.cshtml", ignoring any
    /// view that is not a theme part sitting directly in a theme folder.
    /// </summary>
    private static string? ThemeNameFromPath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        var normalised = relativePath.Replace('\\', '/').TrimStart('~', '/');
        const string marker = ThemeRoot + "/";

        if (!normalised.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var remainder = normalised[marker.Length..].Split('/');

        // Exactly "<theme>/<part>.cshtml" - anything deeper is a theme's own private partial.
        return remainder.Length == 2 && remainder[0].Length > 0 ? remainder[0] : null;
    }

    private static string Humanise(string name)
        => string.IsNullOrWhiteSpace(name)
            ? name
            : char.ToUpperInvariant(name[0]) + name[1..];
}
