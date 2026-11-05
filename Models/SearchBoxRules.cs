namespace Imobisoft.Search.Models;

/// <summary>
/// The header search box: the package turns a site's existing search link into a box that opens
/// where the visitor already is, rather than a link that leaves for an empty search page.
/// <para>
/// On by default, and needs nothing in the consuming site - the package injects the behaviour on
/// every page. These are the default profile's rules, edited on the Search Box tab of the profile
/// editor, so changing the box is an editor's job rather than a deployment.
/// </para>
/// </summary>
public sealed class SearchBoxRules
{
    /// <summary>
    /// Turns the box off. Search links then behave the way they did before the package was
    /// installed: one press, straight to the search page.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Placeholder text shown inside the empty box.</summary>
    public string Placeholder { get; set; } = "Search...";

    /// <summary>Accessible name for the box's own submit button.</summary>
    public string ButtonLabel { get; set; } = "Search";

    /// <summary>
    /// Image used for the search icon on the box's own button - any format a browser renders, so
    /// <c>.svg</c>, <c>.png</c>, <c>.webp</c> or <c>.gif</c>. Either a path in the site
    /// (<c>/images/search.svg</c>), a media item's URL, or an absolute URL.
    /// <para>
    /// Empty - the default - uses the magnifier the package draws itself, which needs no file and
    /// takes the colour of whatever it sits on. A site's own icon is rendered as an image, so it
    /// keeps its own colours.
    /// </para>
    /// <para>
    /// This is the icon on the box, not the one on the site's search link: that link is left
    /// exactly as the site renders it, icon and all.
    /// </para>
    /// </summary>
    public string IconUrl { get; set; } = string.Empty;

    /// <summary>Width and height of the icon in pixels, whether it is a site's own or the built-in one.</summary>
    public int IconSize { get; set; } = 18;

    /// <summary>
    /// Where in a page the package looks for search links. Narrow this to stop it reaching a part
    /// of the site it should leave alone; widening it to <c>body</c> would catch search links in
    /// page content too, which is rarely wanted.
    /// </summary>
    public string Containers { get; set; } = "header, nav, [role=\"banner\"], [role=\"navigation\"]";

    /// <summary>
    /// Paths of search pages the package would not recognise on its own. It already treats a URL
    /// whose last segment is the word "search" - alone or hyphenated, as in
    /// <c>/imobisoft-search</c> or <c>/search-results</c> - as a search page, so this is only for a
    /// page named something else entirely, such as <c>/find</c>.
    /// </summary>
    public IList<string> ExtraPaths { get; set; } = new List<string>();

    /// <summary>
    /// Paths that never get a box, however well they match. This is how a site with more than one
    /// search page keeps the box on one of them and leaves the other a plain link, without needing
    /// anything added to its own markup.
    /// </summary>
    public IList<string> ExcludedPaths { get; set; } = new List<string>();

    /// <summary>
    /// Query-string parameter the term is submitted as. The package's own search page, and every
    /// built-in view, reads <c>q</c>.
    /// </summary>
    public string QueryName { get; set; } = "q";
}
