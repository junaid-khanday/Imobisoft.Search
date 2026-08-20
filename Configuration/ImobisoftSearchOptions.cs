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
