using System.Text.Json.Serialization;

namespace Imobisoft.Search.Models;

/// <summary>
/// One choice in the profile's sort filter - the entries a visitor picks between in a "Sort by"
/// dropdown, such as A-Z, Z-A, price lowest-first or highest-first. Lives under the profile's
/// result rules beside the facet definitions, so it is configured, enabled and disabled exactly
/// like any other filter dimension.
/// </summary>
public sealed class SortOption
{
    /// <summary>
    /// Stable identifier carried in the <c>sort</c> query parameter when the visitor picks this
    /// option. Generated from the label in the backoffice; must stay unique within the profile.
    /// </summary>
    public string Alias { get; set; } = string.Empty;

    /// <summary>Text the site renders in the dropdown, e.g. "A - Z".</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Index field to sort on. The reserved value <see cref="SortRule.ScoreField"/> ("score")
    /// orders by relevance instead and needs no sortable field in the index.
    /// </summary>
    public string Field { get; set; } = SortRule.ScoreField;

    public SortDirection Direction { get; set; } = SortDirection.Ascending;

    /// <summary>Disabled options stay configured but never appear on the site.</summary>
    public bool Enabled { get; set; } = true;
}
