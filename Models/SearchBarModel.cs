namespace Imobisoft.Search.Models;

/// <summary>
/// Everything the overridable search-bar partial needs. The package renders its own copy of
/// <c>Views/Partials/Search/searchbar.cshtml</c>; a consuming site that creates a file at that
/// exact path replaces it everywhere, and is free to ignore this model and hard-code its own
/// markup - the only contract is a text input named "q" inside a GET form.
/// </summary>
public sealed class SearchBarModel
{
    /// <summary>Form action URL. Empty submits to the current page, which is what both built-in views want.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>The current query text, echoed back into the box.</summary>
    public string Term { get; set; } = string.Empty;

    /// <summary>Placeholder text shown inside an empty box.</summary>
    public string Placeholder { get; set; } = "Type a word or phrase to search...";

    /// <summary>CSS class for the form element.</summary>
    public string FormClass { get; set; } = "imobisoft-search-form";

    /// <summary>CSS class for the wrapper around the input.</summary>
    public string WrapClass { get; set; } = "imobisoft-input-wrap";

    /// <summary>CSS class for the text input itself.</summary>
    public string InputClass { get; set; } = "imobisoft-input";

    /// <summary>CSS class for the submit button.</summary>
    public string ButtonClass { get; set; } = "imobisoft-btn imobisoft-btn-primary";

    /// <summary>Submit button label.</summary>
    public string ButtonText { get; set; } = "Search";
}
