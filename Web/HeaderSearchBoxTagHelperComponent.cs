using System.Text;
using System.Text.Encodings.Web;
using Imobisoft.Search.Configuration;
using Imobisoft.Search.Models;
using Imobisoft.Search.Services;
using Microsoft.AspNetCore.Mvc.Razor.TagHelpers;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;

namespace Imobisoft.Search.Web;

/// <summary>
/// Puts the header search box, and type-ahead for whatever search boxes a page has, on every page of
/// the site without the site doing anything.
/// <para>
/// This is what makes the behaviour the package's own rather than the consuming project's: a tag
/// helper component is how a library adds markup to every Razor view, so the stylesheet and script
/// arrive on their own - no partial to place, no layout to edit, no view component to call. The
/// script then turns the site's existing search link into a box that opens where the visitor
/// already is. Nothing is injected on a site that has turned the box off.
/// </para>
/// <para>
/// Needs only what a default Umbraco or MVC project already has: a layout whose <c>&lt;head&gt;</c>
/// and <c>&lt;body&gt;</c> are written in Razor, and
/// <c>@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers</c> in <c>_ViewImports.cshtml</c>.
/// </para>
/// </summary>
public sealed class HeaderSearchBoxTagHelperComponent : TagHelperComponent
{
    private const string StylesheetElementId = "imobisoft-searchbox-css";

    private const string StylesheetPath = "/App_Plugins/" + ImobisoftSearchConstants.PackageName + "/searchbox.css";

    private const string ScriptPath = "/App_Plugins/" + ImobisoftSearchConstants.PackageName + "/searchbox.js";

    /// <summary>Where the script fetches the rendered header box from.</summary>
    private const string BoxEndpoint = "/" + ImobisoftSearchConstants.Web.SearchPagePath + "/box";

    private const string SuggestScriptPath = "/App_Plugins/" + ImobisoftSearchConstants.PackageName + "/suggest.js";

    private const string SuggestEndpoint = "/" + ImobisoftSearchConstants.Web.SearchPagePath + "/suggest";

    private readonly ImobisoftSearchOptions _options;
    private readonly ISearchProfileService _profiles;
    private readonly ISearchSettingsService _settings;

    public HeaderSearchBoxTagHelperComponent(
        IOptions<ImobisoftSearchOptions> options,
        ISearchProfileService profiles,
        ISearchSettingsService settings)
    {
        _options = options.Value;
        _profiles = profiles;
        _settings = settings;
    }

    /// <summary>
    /// Runs late, so the stylesheet lands after a site's own - a site overriding the box's classes
    /// should not have to fight specificity to do it.
    /// </summary>
    public override int Order => 1000;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (!_options.Enabled)
        {
            return;
        }

        // The box is a rule of the default profile - the same profile that answers the search it
        // starts - so it is edited on the profile editor's Search Box tab rather than in
        // configuration. Profiles are cached, so this costs nothing per request. A site with no
        // profile yet still gets a working box from the defaults.
        SearchBoxRules box = _profiles.ResolveForSearch(null)?.Rules.SearchBox ?? new SearchBoxRules();

        var wantsBox = _options.EnableHeaderSearchBox && box.Enabled;

        // Type-ahead is its own feature with its own switch, so it is not tied to the header box:
        // a site that places its own search box, or has turned the header box off, still gets
        // suggestions in whatever box it does have.
        var wantsSuggest = Suggestions();

        if (!wantsBox && !wantsSuggest)
        {
            return;
        }

        if (string.Equals(context.TagName, "head", StringComparison.OrdinalIgnoreCase))
        {
            // In the head rather than beside the script, so the panel is styled before it can
            // first be opened. One stylesheet covers both, which is why it goes in for either.
            output.PostContent.AppendHtml(
                $"<link id=\"{StylesheetElementId}\" rel=\"stylesheet\" href=\"{StylesheetPath}\" />");

            return;
        }

        if (string.Equals(context.TagName, "body", StringComparison.OrdinalIgnoreCase))
        {
            output.PostContent.AppendHtml(BuildScriptTag(box, wantsBox, wantsSuggest));
        }
    }

    /// <summary>
    /// Whether type-ahead is on, from the site settings rather than the profile - the same switch
    /// the engine itself honours, so the script is never injected to call an endpoint that is
    /// already answering nothing.
    /// </summary>
    private bool Suggestions()
    {
        try
        {
            return _settings.Get().Suggestions.Enabled;
        }
        catch
        {
            // Settings live in the database. A page must still render if that read fails, so the
            // feature simply stays off rather than taking the request with it.
            return false;
        }
    }

    private string BuildScriptTag(SearchBoxRules box, bool wantsBox, bool wantsSuggest)
    {
        HtmlEncoder encoder = HtmlEncoder.Default;
        var tag = new StringBuilder();

        if (wantsBox)
        {
            tag.Append("<script src=\"").Append(ScriptPath).Append("\" defer");

            Attribute(tag, "data-containers", box.Containers, encoder);
            Attribute(tag, "data-query", box.QueryName, encoder);

            // Where the script fetches the box markup from. Everything about how the box looks now
            // comes from the view rendered there, so the placeholder, label and icon no longer
            // travel as attributes - they are applied server-side when that view is rendered.
            Attribute(tag, "data-box-endpoint", BoxEndpoint, encoder);
            Attribute(tag, "data-paths", BuildPaths(box), encoder);
            Attribute(tag, "data-excluded", Join(box.ExcludedPaths), encoder);

            // Handed to the script so it can add the stylesheet itself, for a layout that has no
            // <head> for the link above to have been written into.
            Attribute(tag, "data-stylesheet", StylesheetPath, encoder);

            tag.Append("></script>");
        }

        if (wantsSuggest)
        {
            // Attaches to the package's own boxes and to any input a site marks with
            // data-imobisoft-suggest, so a site needs nothing placed for it either.
            tag.Append("<script src=\"").Append(SuggestScriptPath).Append("\" defer");
            Attribute(tag, "data-suggest-endpoint", SuggestEndpoint, encoder);
            Attribute(tag, "data-stylesheet", StylesheetPath, encoder);
            tag.Append("></script>");
        }

        return tag.ToString();
    }

    /// <summary>
    /// The search pages the script should recognise by name. Its own rule already covers a URL
    /// whose last segment is the word "search", so this is the package's page plus whatever the
    /// site has named differently.
    /// </summary>
    private string BuildPaths(SearchBoxRules box)
    {
        var paths = new List<string>();

        if (_options.EnableSearchPage)
        {
            paths.Add("/" + ImobisoftSearchConstants.Web.SearchPagePath);
        }

        // The site's own pages that search is rendered into. The box has to know about them or it
        // would treat the site's search page as an ordinary page and open a second box on it.
        paths.AddRange(SitePagePaths());

        paths.AddRange(box.ExtraPaths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()));

        return string.Join(',', paths);
    }

    /// <summary>
    /// The site pages search is placed into, from the settings. Empty when that read fails, for the
    /// same reason as <see cref="Suggestions"/>: a page must still render.
    /// </summary>
    private IEnumerable<string> SitePagePaths()
    {
        try
        {
            return _settings.Get().SearchPage.SitePagePaths
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string Join(IEnumerable<string> paths)
        => string.Join(',', paths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()));

    private static void Attribute(StringBuilder tag, string name, string? value, HtmlEncoder encoder)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        tag.Append(' ')
           .Append(name)
           .Append("=\"")
           .Append(encoder.Encode(value))
           .Append('"');
    }
}
