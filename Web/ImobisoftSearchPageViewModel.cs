namespace Imobisoft.Search.Web;

/// <summary>
/// What the package's built-in search page renders.
/// <para>
/// It is the listing model plus the one thing only a standalone page needs - the path it is served
/// from, for building its own links. Inheriting rather than repeating the properties is what lets
/// the built-in page render the very same overridable (and themeable) partials the embeddable
/// component does, instead of carrying a second copy of the markup that could drift from it.
/// </para>
/// </summary>
public sealed class ImobisoftSearchPageViewModel : ImobisoftSearchListingViewModel
{
    /// <summary>Path the page is served from, used to build its own links.</summary>
    public string PagePath { get; set; } = "/" + ImobisoftSearchConstants.Web.SearchPagePath;
}
