namespace Imobisoft.Search.Models;

/// <summary>
/// Defines <em>what</em> is searchable: which indexes are queried and which documents inside them
/// are eligible.
/// <para>
/// Include to switch on, throughout. Content and media are searchable only through
/// <see cref="IncludeContentTypes"/> and <see cref="IncludeMediaTypes"/>, so a profile that lists
/// neither returns nothing however its indexes and entity types are set. Members are the one
/// exception - they have no per-type list, so <see cref="IndexTypes"/> is what admits them.
/// </para>
/// </summary>
public sealed class SourceRules
{
    /// <summary>
    /// Examine index names to search. Empty means every index discovered at runtime, which is the
    /// default the package ships with.
    /// </summary>
    public IList<string> Indexes { get; set; } = new List<string>();

    /// <summary>
    /// Examine index types to search (<c>content</c>, <c>media</c>, <c>member</c>).
    /// <para>
    /// Only <c>member</c> is decided here. Content and media are decided by their own include
    /// lists, which already name an index type by naming a type within it - listing <c>content</c>
    /// here as well would be a second, quieter switch for the same thing, and two switches for one
    /// outcome is how a profile ends up doing something nobody chose.
    /// </para>
    /// </summary>
    public IList<string> IndexTypes { get; set; } = new List<string>();

    /// <summary>
    /// Document type aliases to search. <strong>Empty means none</strong> - a document type appears
    /// in results because it is listed here, and for no other reason.
    /// <para>
    /// Deliberately not "empty means all". The two readings are indistinguishable until a search
    /// runs, and reading it the other way means a profile built to expose three page types quietly
    /// exposes every page type on the site - including the ones nobody meant to publish to search.
    /// Of the two ways to be wrong, returning nothing is the one an editor notices immediately.
    /// </para>
    /// </summary>
    public IList<string> IncludeContentTypes { get; set; } = new List<string>();

    /// <summary>Document type aliases to skip. Applied after <see cref="IncludeContentTypes"/>.</summary>
    public IList<string> ExcludeContentTypes { get; set; } = new List<string>();

    /// <summary>
    /// Media type aliases to search. Empty means none, exactly as with
    /// <see cref="IncludeContentTypes"/>.
    /// </summary>
    public IList<string> IncludeMediaTypes { get; set; } = new List<string>();

    /// <summary>Media type aliases to skip.</summary>
    public IList<string> ExcludeMediaTypes { get; set; } = new List<string>();

    /// <summary>
    /// Restricts results to the subtrees below these nodes. Empty means the whole site. Matched
    /// against the indexed <c>__Path</c> field.
    /// </summary>
    public IList<Guid> RootNodeKeys { get; set; } = new List<Guid>();

    /// <summary>Individual nodes to keep out of results.</summary>
    public IList<Guid> ExcludedNodeKeys { get; set; } = new List<Guid>();

    /// <summary>When true, everything beneath an excluded node is excluded too.</summary>
    public bool ExcludeDescendantsOfExcludedNodes { get; set; } = true;

    /// <summary>ISO culture codes to search. Empty means all cultures.</summary>
    public IList<string> Cultures { get; set; } = new List<string>();

    /// <summary>Honour the <c>umbracoNaviHide</c> property and drop hidden nodes.</summary>
    public bool RespectNaviHide { get; set; } = true;

    /// <summary>
    /// Property aliases that hide a page from search when ticked on that page - typically a
    /// <c>hideFromSiteSearch</c> toggle the site already has on its document types.
    /// <para>
    /// This is the difference between "hidden from the menu" and "hidden from search", which are
    /// not the same decision and which almost every real site keeps apart.
    /// <see cref="RespectNaviHide"/> covers Umbraco's own navigation toggle; this covers the one
    /// the site invented, whatever it chose to call it. Without it, a page an editor has explicitly
    /// marked as not-for-search is returned anyway - and an editor who has ticked a box has every
    /// reason to believe it was honoured.
    /// </para>
    /// <para>
    /// Matched against the index with and without a culture suffix, so a property that varies by
    /// culture (<c>hideFromSiteSearch_en-gb</c>) is found by its plain alias. Any truthy value
    /// hides the page; a page that does not carry the property at all is unaffected, so naming a
    /// field no document has is harmless rather than a way to empty the results.
    /// </para>
    /// </summary>
    public IList<string> HideFromSearchFields { get; set; } = new List<string>();

    /// <summary>Drop nodes behind public access (member-protected) from anonymous searches.</summary>
    public bool ExcludeProtected { get; set; } = true;

    /// <summary>
    /// Only return published content. The ExternalIndex is published-only already; this matters when
    /// the InternalIndex is in the index list.
    /// </summary>
    public bool PublishedOnly { get; set; } = true;
}
