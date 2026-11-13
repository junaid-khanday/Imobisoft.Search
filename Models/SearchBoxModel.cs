namespace Imobisoft.Search.Models;

/// <summary>
/// The header search box: one button that opens a search bar in place, on the page the visitor is
/// already on. Nothing navigates until a term has been typed and the button is pressed again - that
/// submit is what takes the visitor to <see cref="Action"/> with the term as <c>?q=</c>.
/// <para>
/// Rendered by <c>Views/Partials/Search/HeaderSearchBox.cshtml</c>. A consuming site that creates a file
/// at that exact path replaces the markup everywhere, and is free to ignore this model and hard-code
/// its own - the only contract is a GET form containing a text input named <see cref="QueryName"/>
/// and a submit button.
/// </para>
/// </summary>
public sealed class SearchBoxModel
{
    /// <summary>
    /// Where a submitted search lands. Defaults to the page the package serves itself; point it at
    /// the site's own search page when there is one.
    /// </summary>
    public string Action { get; set; } = "/" + ImobisoftSearchConstants.Web.SearchPagePath;

    /// <summary>
    /// Name of the query-string parameter the term is submitted as. The package's own search page
    /// (and every built-in view) reads <c>q</c>; change it only to match a search page of the site's
    /// own that expects something else.
    /// </summary>
    public string QueryName { get; set; } = "q";

    /// <summary>The current term, echoed back into the box - so the header still shows what was searched for.</summary>
    public string Term { get; set; } = string.Empty;

    /// <summary>
    /// Renders with the bar already open instead of closed. The view component sets this on its own
    /// when the page it is on is already showing results for a term.
    /// </summary>
    public bool StartOpen { get; set; }

    /// <summary>Placeholder text shown inside the empty box.</summary>
    public string Placeholder { get; set; } = "Search...";

    /// <summary>
    /// Accessible name for the button, used as its <c>aria-label</c> and its tooltip. The button
    /// itself shows an icon, so this is what a screen reader announces.
    /// </summary>
    public string ButtonLabel { get; set; } = "Search";

    /// <summary>
    /// Optional visible text next to the icon. Empty - the default - keeps the button a plain icon,
    /// which is what fits a site header.
    /// </summary>
    public string ButtonText { get; set; } = string.Empty;

    /// <summary>
    /// Image used for the search icon - any format a browser renders, so <c>.svg</c>, <c>.png</c>,
    /// <c>.webp</c> or <c>.gif</c>. Empty uses the magnifier drawn inline, which takes the colour of
    /// whatever it sits on. Defaults to the icon configured on the profile's Search Box tab.
    /// </summary>
    public string IconUrl { get; set; } = string.Empty;

    /// <summary>Width and height of the icon in pixels.</summary>
    public int IconSize { get; set; } = 18;

    /// <summary>
    /// How wide the bar opens to, as any CSS length. Narrow headers can shrink it; on small screens
    /// the bar drops below the button at full width regardless.
    /// </summary>
    public string OpenWidth { get; set; } = "260px";

    /// <summary>
    /// Which way the bar grows. <c>true</c> - the default - grows it leftwards so the button stays
    /// put, which is what a button at the right-hand end of a header wants. <c>false</c> grows it
    /// rightwards, for a button at the left.
    /// </summary>
    public bool OpenToTheLeft { get; set; } = true;

    /// <summary>
    /// Id given to the input, so the button's <c>aria-controls</c> can point at it. Left empty, one
    /// is generated - set it only when the page needs a predictable id.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>CSS class added to the wrapper.</summary>
    public string RootClass { get; set; } = string.Empty;

    /// <summary>CSS class added to the form element.</summary>
    public string FormClass { get; set; } = string.Empty;

    /// <summary>CSS class added to the text input.</summary>
    public string InputClass { get; set; } = string.Empty;

    /// <summary>CSS class added to the button.</summary>
    public string ButtonClass { get; set; } = string.Empty;
}
