namespace Imobisoft.Search.Models;

/// <summary>One theme the search page can render with, as offered in the backoffice.</summary>
public sealed class SearchThemeInfo
{
    /// <summary>Folder name under <c>Views/Partials/Search/Themes/</c>, and the value saved on the profile.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Title-cased name for the dropdown.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// True when the theme comes from the consuming site rather than the package. A site's theme
    /// wins over a package theme of the same name, which is what this flag reports.
    /// </summary>
    public bool IsSiteProvided { get; set; }

    /// <summary>
    /// Which partials this theme actually defines (<c>results</c>, <c>filters</c>, ...). Anything
    /// absent falls back to the package's own copy, so a theme is free to restyle only one piece.
    /// </summary>
    public IList<string> Parts { get; set; } = new List<string>();
}
