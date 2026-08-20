using Imobisoft.Search.Models;

namespace Imobisoft.Search.Web;

/// <summary>What the package's built-in search page renders.</summary>
public sealed class ImobisoftSearchPageViewModel
{
    /// <summary>What the visitor typed.</summary>
    public string Term { get; set; } = string.Empty;

    /// <summary>1-based page number.</summary>
    public int Page { get; set; } = 1;

    /// <summary>Null before a search has been run, which is how the page knows to show nothing yet.</summary>
    public SearchResponse? Response { get; set; }

    /// <summary>Path the page is served from, used to build its own links.</summary>
    public string PagePath { get; set; } = "/" + ImobisoftSearchConstants.Web.SearchPagePath;

    public bool HasSearched => !string.IsNullOrWhiteSpace(Term);
}
