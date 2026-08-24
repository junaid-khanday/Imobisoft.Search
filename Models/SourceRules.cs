namespace Imobisoft.Search.Models;

/// <summary>
/// Defines <em>what</em> is searchable: which indexes are queried and which documents inside them
/// are eligible. Per-type lists narrow an index that is already in scope through
/// <see cref="IndexTypes"/>; a profile that names neither stays dark, which is the "include to
/// switch search on" model.
/// </summary>
public sealed class SourceRules
{
    /// <summary>
    /// Examine index names to search. Empty means every index discovered at runtime, which is the
    /// default the package ships with.
    /// </summary>
    public IList<string> Indexes { get; set; } = new List<string>();

    /// <summary>
    /// Restricts to documents of these Examine index types (<c>content</c>, <c>media</c>,
    /// <c>member</c>). Empty means all of them.
    /// </summary>
    public IList<string> IndexTypes { get; set; } = new List<string>();

    /// <summary>Document type aliases to search. Empty means every document type.</summary>
    public IList<string> IncludeContentTypes { get; set; } = new List<string>();

    /// <summary>Document type aliases to skip. Applied after <see cref="IncludeContentTypes"/>.</summary>
    public IList<string> ExcludeContentTypes { get; set; } = new List<string>();

    /// <summary>Media type aliases to search. Empty means every media type.</summary>
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

    /// <summary>Drop nodes behind public access (member-protected) from anonymous searches.</summary>
    public bool ExcludeProtected { get; set; } = true;

    /// <summary>
    /// Only return published content. The ExternalIndex is published-only already; this matters when
    /// the InternalIndex is in the index list.
    /// </summary>
    public bool PublishedOnly { get; set; } = true;
}
