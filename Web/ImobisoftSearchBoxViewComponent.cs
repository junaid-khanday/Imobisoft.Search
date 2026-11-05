using Imobisoft.Search.Configuration;
using Imobisoft.Search.Models;
using Imobisoft.Search.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Imobisoft.Search.Web;

/// <summary>
/// The search button for a site header, as a single embeddable component:
/// <para><c>@await Component.InvokeAsync("ImobisoftSearchBox")</c></para>
/// <para>
/// Closed, it is just a button. Pressing it opens the search bar on the page the visitor is already
/// on - nothing navigates, nothing is fetched. Pressing it again, with a term typed, submits to the
/// search page, which is where the results are shown. That is the whole point of it: a header
/// button that asks for the term before it leaves the page, rather than one that jumps straight to
/// an empty search page.
/// </para>
/// <para>
/// It runs no search of its own, so it costs a header nothing - it is a GET form pointed at
/// whichever page shows results. By default that is the page the package serves at
/// <c>/imobisoft-search</c>; pass <c>resultsUrl</c> when the site has a search page of its own
/// (which it will have if <see cref="ImobisoftSearchOptions.EnableSearchPage"/> is turned off).
/// </para>
/// <para>
/// The default view lives in this package; a site can take over the markup by creating
/// <c>Views/Partials/Search/HeaderSearchBox.cshtml</c> in its own project, or by editing it from
/// the Search section - that copy wins, nothing else changes.
/// </para>
/// </summary>
/// <example>
/// <code>
/// &lt;!-- in the site's header, in place of a link to the search page --&gt;
/// @await Component.InvokeAsync("ImobisoftSearchBox")
///
/// &lt;!-- pointed at the site's own search page, opening leftwards from a button on the right --&gt;
/// @await Component.InvokeAsync("ImobisoftSearchBox", new { resultsUrl = "/search", placeholder = "Search the site..." })
/// </code>
/// </example>
public sealed class ImobisoftSearchBoxViewComponent : ViewComponent
{
    private readonly ImobisoftSearchOptions _options;
    private readonly ISearchProfileService _profiles;

    public ImobisoftSearchBoxViewComponent(
        IOptions<ImobisoftSearchOptions> options,
        ISearchProfileService profiles)
    {
        _options = options.Value;
        _profiles = profiles;
    }

    /// <summary>
    /// Renders the button. Every argument is optional - with none of them it points at the package's
    /// own search page and echoes back whatever term that page is currently showing.
    /// </summary>
    /// <param name="resultsUrl">
    /// Page a submitted search lands on. Null or empty uses the package's search page.
    /// </param>
    /// <param name="placeholder">Placeholder text inside the empty box.</param>
    /// <param name="buttonText">
    /// Visible text beside the icon. Null or empty - the default - keeps the button a plain icon.
    /// </param>
    /// <param name="buttonLabel">Accessible name for the button, announced by screen readers.</param>
    /// <param name="openWidth">How wide the bar opens to, as any CSS length.</param>
    /// <param name="openToTheLeft">
    /// Grows the bar leftwards so the button stays put, which is what a button at the right-hand end
    /// of a header wants. Pass false for a button at the left.
    /// </param>
    /// <param name="startOpen">
    /// Renders with the bar already open. Left null, it opens on its own when the current page is
    /// already showing results for a term, so the header keeps showing what was searched for.
    /// </param>
    /// <param name="queryName">
    /// Query-string parameter the term is submitted as. Only worth changing for a search page of the
    /// site's own that expects something other than <c>q</c>.
    /// </param>
    /// <param name="iconUrl">
    /// Image for the search icon - <c>.svg</c>, <c>.png</c>, <c>.webp</c> or <c>.gif</c>. Null uses
    /// the icon the profile configures; an empty string forces the built-in magnifier.
    /// </param>
    /// <param name="iconSize">Icon width and height in pixels. Null uses the profile's size.</param>
    public IViewComponentResult Invoke(
        string? resultsUrl = null,
        string? placeholder = null,
        string? buttonText = null,
        string? buttonLabel = null,
        string? openWidth = null,
        bool openToTheLeft = true,
        bool? startOpen = null,
        string? queryName = null,
        string? iconUrl = null,
        int? iconSize = null)
    {
        // Placeholder, label and icon default to what the profile's Search Box tab configures, so a
        // box placed by hand looks like the one the package injects unless told otherwise.
        SearchBoxRules configured = _profiles.ResolveForSearch(null)?.Rules.SearchBox ?? new SearchBoxRules();

        var parameter = string.IsNullOrWhiteSpace(queryName) ? configured.QueryName : queryName.Trim();

        if (string.IsNullOrWhiteSpace(parameter))
        {
            parameter = "q";
        }

        // Echoing the current term back is what makes the header box usable on the results page
        // itself: it shows what was searched for, and a second search starts from it.
        var term = Request.Query[parameter].ToString().Trim();

        var model = new SearchBoxModel
        {
            Action = string.IsNullOrWhiteSpace(resultsUrl)
                ? "/" + ImobisoftSearchConstants.Web.SearchPagePath
                : resultsUrl.Trim(),
            QueryName = parameter,
            Term = term,
            StartOpen = startOpen ?? !string.IsNullOrWhiteSpace(term),
            OpenToTheLeft = openToTheLeft,
            Placeholder = configured.Placeholder,
            ButtonLabel = configured.ButtonLabel,
            IconUrl = iconUrl ?? configured.IconUrl,
            IconSize = iconSize is > 0 ? iconSize.Value : (configured.IconSize > 0 ? configured.IconSize : 18),
        };

        if (!string.IsNullOrWhiteSpace(placeholder))
        {
            model.Placeholder = placeholder;
        }

        if (!string.IsNullOrWhiteSpace(buttonText))
        {
            model.ButtonText = buttonText;
        }

        if (!string.IsNullOrWhiteSpace(buttonLabel))
        {
            model.ButtonLabel = buttonLabel;
        }

        if (!string.IsNullOrWhiteSpace(openWidth))
        {
            model.OpenWidth = openWidth;
        }

        // Rendered straight from the overridable partial, so one file designs the header box with
        // no component view in between for a site to have to find.
        return View("~/Views/Partials/Search/HeaderSearchBox.cshtml", model);
    }
}
