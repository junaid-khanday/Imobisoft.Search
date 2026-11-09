namespace Imobisoft.Search.Configuration;

/// <summary>
/// Package settings read from the consumer's <c>appsettings.json</c>, under <c>Imobisoft:Search</c>.
/// <para>
/// Everything here has a working default, so a consuming site needs no configuration at all. The
/// settings exist so a site can turn the package off, or fit it to conventions it already has,
/// without writing code.
/// </para>
/// </summary>
/// <example>
/// <code>
/// "Imobisoft": {
///   "Search": {
///     "Enabled": true,
///     "FilterQueryPrefix": "f_"
///   }
/// }
/// </code>
/// </example>
public sealed class ImobisoftSearchOptions
{
    /// <summary>Configuration section these settings bind to.</summary>
    public const string SectionName = "Imobisoft:Search";

    /// <summary>
    /// Master switch. Turning this off makes every search return nothing, which is how a site falls
    /// back to whatever search it had before without removing the package or redeploying.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Puts the package behind the site's own search service, so an existing search page starts
    /// obeying the Search section without any code being written in the site.
    /// <para>
    /// On by default: installing the package is taken to mean you want it answering searches. Set
    /// this to <c>false</c> to leave the site's own search in place and call
    /// <c>IImobisoftSearchService</c> explicitly instead.
    /// </para>
    /// </summary>
    public bool TakeOverSiteSearch { get; set; } = true;

    /// <summary>
    /// Name of the interface to take over, when the site's search service is not called one of the
    /// usual names. Left empty, the package looks for <c>ISearchService</c>,
    /// <c>ISiteSearchService</c> and <c>ISearchProvider</c>.
    /// </summary>
    public string SiteSearchInterfaceName { get; set; } = string.Empty;

    /// <summary>
    /// Serves the package's own search page at <c>/imobisoft-search</c>, so a site has working
    /// search the moment the package is installed. Turn it off once the site has a search page of
    /// its own.
    /// </summary>
    public bool EnableSearchPage { get; set; } = true;

    /// <summary>
    /// Finds the search page a site already had and answers it with the package, so installing on
    /// a site that already has search leaves it with one search page rather than two.
    /// <para>
    /// This is the companion to <see cref="TakeOverSiteSearch"/>: that one puts the package behind
    /// the site's search <em>service</em>, this one behind the site's search <em>page</em>. A site
    /// that built search without a service of its own - the query written straight into a
    /// controller or a template - is covered by this and not by that.
    /// </para>
    /// <para>
    /// On by default, for the same reason: installing the package is taken to mean you want it
    /// answering searches. This is the deployment-level switch; day to day it is controlled in the
    /// backoffice (Search → Settings), and setting this to <c>false</c> stops the package touching
    /// the site's own search page at all, whatever the backoffice says.
    /// </para>
    /// </summary>
    public bool TakeOverSiteSearchPage { get; set; } = true;

    /// <summary>
    /// Adds the header search box to every page, which is what turns a site's existing search link
    /// into a box that opens in place rather than a link that leaves for an empty search page. No
    /// markup or code is needed in the site for this.
    /// <para>
    /// This is the deployment-level switch. Day to day the box is controlled in the backoffice
    /// (Search → Settings); setting this to <c>false</c> stops the package injecting anything at
    /// all, whatever the backoffice says, which is the way to rule it out on a site that never
    /// wants it.
    /// </para>
    /// </summary>
    public bool EnableHeaderSearchBox { get; set; } = true;

    /// <summary>
    /// Allows the AI text tools - summarise, shorten, key points, translate - to run on the site.
    /// <para>
    /// This is the deployment-level switch, and it governs a public endpoint that spends money at an
    /// AI provider on request. Day to day the tools are controlled in the backoffice (the profile
    /// editor's AI tab) and stay off until a key is added; setting this to <c>false</c> takes the
    /// endpoint out of the site altogether, whatever the backoffice says.
    /// </para>
    /// </summary>
    public bool EnableAiTextTools { get; set; } = true;

    /// <summary>
    /// Allows AI inside the search itself - reading the query before it runs, re-ordering the
    /// results, and answering above them.
    /// <para>
    /// Separate from <see cref="EnableAiTextTools"/>, which governs the buttons a visitor presses.
    /// These stages spend money per search rather than per press, so a site may well want one
    /// without the other.
    /// </para>
    /// <para>
    /// This is the deployment-level switch and it is on, but every stage behind it is off until an
    /// editor switches it on per profile (the profile editor's AI tab). Setting this to
    /// <c>false</c> takes them out of the site altogether, whatever the backoffice says.
    /// </para>
    /// </summary>
    public bool EnableAiSearch { get; set; } = true;

    /// <summary>
    /// Reads filter selections straight off the query string, so a search page needs no code to
    /// support filtering - the links the package generates are enough.
    /// </summary>
    public bool ReadFiltersFromQueryString { get; set; } = true;

    /// <summary>
    /// Prefix marking a filter in the query string: <c>?q=news&amp;f_type=newsArticle</c>. Change it
    /// only if it clashes with a parameter the site already uses.
    /// </summary>
    public string FilterQueryPrefix { get; set; } = "f_";

    /// <summary>
    /// Adds <c>X-Imobisoft-Search</c> to the response naming the profile that ran. Makes it obvious
    /// which engine answered a request without reading the markup.
    /// </summary>
    public bool AddResponseHeader { get; set; } = true;
}
