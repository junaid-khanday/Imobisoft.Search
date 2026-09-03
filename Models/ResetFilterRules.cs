using System.Text.Json.Serialization;

namespace Imobisoft.Search.Models;

/// <summary>Which of the visitor's filter selections a reset control clears.</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<ResetFilterScope>))]
public enum ResetFilterScope
{
    /// <summary>Every facet the visitor has chosen a value in.</summary>
    All,

    /// <summary>
    /// Only the facets named in <see cref="ResetFilterRules.Facets"/>. Anything not listed keeps
    /// its selection, which is what lets a site offer "clear the date filters, keep the section".
    /// </summary>
    Selected,
}

/// <summary>
/// The "reset filters" control a search page can offer: whether it appears at all, what it is
/// called, and how much of the visitor's selection it clears.
/// <para>
/// Off by default. A site that wants it turns it on in the profile's result rules; the themes render
/// it after the filter dropdowns.
/// </para>
/// </summary>
public sealed class ResetFilterRules
{
    /// <summary>Whether the control renders at all.</summary>
    public bool Enabled { get; set; }

    /// <summary>Text on the control. Empty falls back to "Reset filters".</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>How much of the visitor's selection the control clears.</summary>
    public ResetFilterScope Scope { get; set; } = ResetFilterScope.All;

    /// <summary>
    /// Facet aliases the control clears when <see cref="Scope"/> is
    /// <see cref="ResetFilterScope.Selected"/>. Ignored otherwise. An empty list with that scope
    /// clears nothing, so the engine treats it as "all" rather than rendering a control that does
    /// nothing when clicked.
    /// </summary>
    public IList<string> Facets { get; set; } = new List<string>();

    /// <summary>
    /// The aliases this control should actually clear, resolved against how it is configured.
    /// Empty means every active facet.
    /// </summary>
    public IReadOnlyList<string> ResolveTargets()
        => Scope == ResetFilterScope.Selected && Facets.Count > 0
            ? Facets.Where(a => !string.IsNullOrWhiteSpace(a)).ToList()
            : Array.Empty<string>();
}
