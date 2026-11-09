using Imobisoft.Search.Models;

namespace Imobisoft.Search.Web;

/// <summary>What the package's built-in search page renders.</summary>
public sealed class ImobisoftSearchPageViewModel
{
    /// <summary>What the visitor typed.</summary>
    public string Term { get; set; } = string.Empty;

    /// <summary>1-based page number.</summary>
    public int Page { get; set; } = 1;

    /// <summary>Null before a search has been run, which is how the page knows to show nothing yet.</summary>
    public SearchResponse? Response { get; set; }

    /// <summary>Path the page is served from, used to build its own links.</summary>
    public string PagePath { get; set; } = "/" + ImobisoftSearchConstants.Web.SearchPagePath;

    public bool HasSearched { get; set; }

    /// <summary>
    /// The facet dimensions the profile defines. These drive the filter bar so the dropdowns exist
    /// above the search box from the moment the page opens, with counts joining once a search - or
    /// the automatic browse pass - has produced them.
    /// </summary>
    public IList<FacetDefinition> ConfiguredFacets { get; set; } = new List<FacetDefinition>();

    /// <summary>The sort choices the profile defines, driving the page's "Sort by" dropdown.</summary>
    public IList<SortOption> ConfiguredSortOptions { get; set; } = new List<SortOption>();

    /// <summary>The currently selected sort alias, echoed so the right option renders selected.</summary>
    public string? SelectedSort { get; set; }

    /// <summary>
    /// Whether the search box still holds what was searched for, from the profile's matching rules.
    /// Off, the box is cleared after a search.
    /// </summary>
    public bool KeepSearchTerm { get; set; } = true;

    /// <summary>
    /// What the visitor typed, before AI query understanding rewrote it. Null when nothing was
    /// rewritten.
    /// <para>
    /// Kept apart from <see cref="Term"/> so the page can say which words it actually searched for
    /// and offer the original back. <see cref="Term"/> is what ran, because that is what the box
    /// and the paging links have to carry.
    /// </para>
    /// </summary>
    public string? OriginalTerm { get; set; }

    /// <summary>
    /// True when this search was run with the AI stages deliberately off, because the visitor asked
    /// for their own words. Stops the page offering a link to do what it has already done.
    /// </summary>
    public bool AiDisabled { get; set; }

    /// <summary>
    /// The layout to render the page into, or null for no layout at all - either because the site
    /// has none that can be used, or because the page is already inside one of the site's own pages.
    /// </summary>
    public string? LayoutPath { get; set; }

    /// <summary>
    /// Whether the page writes its own <c>&lt;html&gt;</c>, <c>&lt;head&gt;</c> and
    /// <c>&lt;body&gt;</c>. False when something else is already providing them: a site layout, or
    /// the site page the view component is being rendered from. A second document nested inside the
    /// site's own would be invalid.
    /// </summary>
    public bool RenderDocumentShell { get; set; } = true;

    /// <summary>
    /// The line under a result's name, or empty when there is nothing worth showing.
    /// <para>
    /// A last line of defence rather than the main one. The index now carries
    /// <c>extractedContent</c> - clean body text with block editor structure stripped out - and a
    /// profile pointed at it gets a readable excerpt without any of this. But a profile can be
    /// pointed at a raw block property instead, by an older configuration or by someone picking the
    /// obvious-looking field, and the result is the thing this exists to stop appearing:
    /// <c>["Home page-square text layout"]</c> printed under the result name.
    /// </para>
    /// <para>
    /// Showing nothing is the right answer when what we have is structure. A result with just a
    /// name and a link tells a visitor something; a result captioned with a JSON array tells them
    /// the site is broken.
    /// </para>
    /// </summary>
    public string Excerpt(SearchResultItem item)
    {
        var text = (item.Summary ?? string.Empty).Trim();

        return IsStructuredLeftover(text) ? string.Empty : text;
    }

    /// <summary>
    /// Whether the document type and culture are worth showing on this result.
    /// <para>
    /// Not on a content page: <c>landing</c> is an internal alias and tells a visitor nothing. Kept
    /// for media and members, where the type is the one thing that distinguishes a result from an
    /// ordinary page, and for a best bet, whose badge has already marked the row as different.
    /// </para>
    /// <para>
    /// The date is not governed by this. It sits under the description on every result, because it
    /// is context for what was just read rather than a label on the result.
    /// </para>
    /// </summary>
    public bool ShowResultType(SearchResultItem item)
    {
        if (item.IsBestBet)
        {
            return true;
        }

        return !string.Equals(
            item.IndexType,
            ImobisoftSearchConstants.IndexTypes.Content,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether this text is block editor structure rather than prose: a JSON document, a JSON
    /// array, or a run of words with no sentence in it that reads as a layout name.
    /// </summary>
    private static bool IsStructuredLeftover(string text)
    {
        if (text.Length == 0)
        {
            return true;
        }

        // Anything that still looks like JSON never reads as a sentence.
        if ((text.StartsWith('[') && text.EndsWith(']'))
            || (text.StartsWith('{') && text.EndsWith('}')))
        {
            return true;
        }

        // Unbalanced quotes and braces survive a snippet being cut out of a JSON blob mid-way.
        if (text.Contains("\":", StringComparison.Ordinal)
            || text.Contains("umb://", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // A floor low enough to keep a genuinely terse meta description - "Our worldwide
        // operations" is a real description someone wrote - and high enough to drop a single
        // stray token. Labels and layout names are kept out at index time now, so this no longer
        // has to be the thing that catches them.
        return CountWords(text) < 2;
    }

    private static int CountWords(string value)
    {
        var count = 0;
        var inWord = false;

        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c))
            {
                inWord = false;
                continue;
            }

            if (!inWord)
            {
                count++;
                inWord = true;
            }
        }

        return count;
    }
}
