using Imobisoft.Search.Models;

namespace Imobisoft.Search.Services;

/// <summary>
/// Finds the themes a search page can render with, and resolves which copy of a given partial a
/// chosen theme should use.
/// </summary>
public interface ISearchThemeService
{
    /// <summary>
    /// Every theme available to this site - the folders found under
    /// <c>Views/Partials/Search/Themes/</c>.
    /// <para>
    /// The package ships no themes of its own on purpose: a site authors its own and they show up
    /// here. The first entry is always the built-in look, under an empty
    /// <see cref="SearchThemeInfo.Name"/>, so "no theme" stays selectable.
    /// </para>
    /// </summary>
    IReadOnlyList<SearchThemeInfo> GetThemes();

    /// <summary>
    /// The view path to render for one piece of the search page.
    /// </summary>
    /// <param name="theme">
    /// Theme name from the profile. Empty, unknown, or a theme that does not define this part all
    /// fall back to the package's own partial, so a caller never has to check first.
    /// </param>
    /// <param name="part">
    /// Partial name without extension: <c>styles</c>, <c>results</c>, <c>filters</c>,
    /// <c>noresults</c>, <c>loadmore</c> or <c>searchbar</c>.
    /// </param>
    string ResolvePartial(string? theme, string part);
}
