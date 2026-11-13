namespace Imobisoft.Search.Models;

/// <summary>How much of what visitors search for is recorded.</summary>
public sealed class AnalyticsSettings
{
    /// <summary>Recording is on by default; turning it off stops all writes immediately.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Queries older than this are deleted by a nightly job. 0 keeps them forever.</summary>
    public int RetentionDays { get; set; } = 90;

    /// <summary>
    /// Records only the searches that found nothing. Useful on a busy site where the interesting
    /// signal is the failures rather than the volume.
    /// </summary>
    public bool RecordZeroResultsOnly { get; set; }

    /// <summary>Ignores very short terms, which are usually mistypes rather than intent.</summary>
    public int MinimumTermLength { get; set; } = 2;

    /// <summary>
    /// Records which result a visitor opened, so the reports can show which searches are actually
    /// answering the question. Requires the site to call <c>RecordClickAsync</c>.
    /// </summary>
    public bool TrackClicks { get; set; } = true;
}

/// <summary>Controls "did you mean" and type-ahead.</summary>
public sealed class SuggestionSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// A search returning fewer than this many results gets a spelling suggestion. 0 only suggests
    /// when nothing at all was found.
    /// </summary>
    public int SuggestBelowResultCount { get; set; } = 3;

    /// <summary>How close a word has to be before it is offered as a correction, between 0 and 1.</summary>
    public float Fuzziness { get; set; } = 0.65f;

    /// <summary>
    /// The largest number of single-character edits between the typed word and a suggestion. Keeps
    /// "prodct" → "product" while rejecting a wholly different word that happens to score well.
    /// </summary>
    public int MaximumEditDistance { get; set; } = 3;

    /// <summary>Default number of type-ahead suggestions returned.</summary>
    public int AutocompleteSize { get; set; } = 10;
}

/// <summary>How the package's built-in search page is rendered.</summary>
public sealed class SearchPageSettings
{
    /// <summary>
    /// Renders the search page inside the site's own layout, so it gets the site's header, footer
    /// and navigation. Off, the page is its own standalone document with only the package's own
    /// plain styling.
    /// <para>
    /// A layout that declares a view model of its own cannot be used - a layout shares its model
    /// with the page inside it - and such a layout is detected and skipped rather than failing to
    /// render. To put search inside a layout like that, render
    /// <c>@await Component.InvokeAsync("ImobisoftSearchPage")</c> from one of the site's own
    /// templates instead.
    /// </para>
    /// </summary>
    public bool UseSiteLayout { get; set; } = true;

    /// <summary>
    /// The layout to use, as an application-rooted path such as
    /// <c>~/Views/Shared/_Layout.cshtml</c>. Empty looks in the usual places for one. A path that
    /// is set but unusable is not quietly replaced by a guess: the page falls back to its
    /// standalone look and says why in the log.
    /// </summary>
    public string? LayoutPath { get; set; }

    /// <summary>
    /// Paths of the site's own pages that search is rendered into, one per entry - "/search",
    /// "/site-search". This is what gets search the site's header, footer and navigation on a site
    /// whose layout declares a view model of its own: the site renders its own page exactly as it
    /// always does, and the search page is placed into it.
    /// <para>
    /// Create the page in Umbraco with whatever document type the site already uses for an ordinary
    /// content page, so the site's own controllers and template build it, then put its path here.
    /// Nothing is injected anywhere else.
    /// </para>
    /// </summary>
    public IList<string> SitePagePaths { get; set; } = new List<string>();

    /// <summary>
    /// A CSS selector for where in that page search is placed - the site's main content area.
    /// Empty tries the usual ones. The first match wins; where nothing matches, search is left at
    /// the end of the page rather than not shown at all.
    /// </summary>
    public string? SitePageContainer { get; set; }

    /// <summary>
    /// Finds the search page the site already had and answers it, without its path being listed
    /// above.
    /// <para>
    /// On by default, because a site that already has a search page has links pointing at it: left
    /// alone, those links keep arriving at the site's own search while the package answers a
    /// different page, and the site has two. A page is recognised by its document type -
    /// <c>searchPage</c>, <c>siteSearch</c>, <c>searchResults</c> - or by its URL being
    /// <c>/search</c> or similar. Nothing in the site's content, templates or links has to change.
    /// </para>
    /// <para>
    /// Turn this off to leave the site's own search page as it was; search is then only placed into
    /// the pages named in <see cref="SitePagePaths"/>.
    /// </para>
    /// </summary>
    public bool TakeOverSitePage { get; set; } = true;

    /// <summary>
    /// Stands the site's own search form and results down on a page that was taken over, so the
    /// visitor sees one search rather than two.
    /// <para>
    /// Applies only to a page the package recognised by itself. A page whose path was typed into
    /// <see cref="SitePagePaths"/> is left as it is: someone placed search into that page
    /// deliberately and the rest of it is theirs.
    /// </para>
    /// </summary>
    public bool ReplaceSitePageSearch { get; set; } = true;

    /// <summary>
    /// A CSS selector for the site's own search markup to stand down, when the usual ones do not
    /// match it. Empty looks for a search form and a results list by the names sites give them.
    /// <para>
    /// The package's own markup is never matched by this, whatever is typed, and neither is
    /// anything outside the page's content area.
    /// </para>
    /// </summary>
    public string? SitePageSearchSelector { get; set; }
}

/// <summary>
/// Site-wide package settings, as opposed to the per-profile rules.
/// <para>
/// Stored as a single JSON value in Umbraco's key/value table, so turning analytics on or off needs
/// no schema change and no configuration file in the consuming project.
/// </para>
/// </summary>
public sealed class SearchSettings
{
    public AnalyticsSettings Analytics { get; set; } = new();

    public SuggestionSettings Suggestions { get; set; } = new();

    public SearchPageSettings SearchPage { get; set; } = new();
}
