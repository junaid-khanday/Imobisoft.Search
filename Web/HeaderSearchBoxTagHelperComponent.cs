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
/// Puts the header search box on every page of the site, without the site doing anything.
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

    private readonly ImobisoftSearchOptions _options;
    private readonly ISearchProfileService _profiles;

    public HeaderSearchBoxTagHelperComponent(
        IOptions<ImobisoftSearchOptions> options,
        ISearchProfileService profiles)
    {
        _options = options.Value;
        _profiles = profiles;
    }

    /// <summary>
    /// Runs late, so the stylesheet lands after a site's own - a site overriding the box's classes
    /// should not have to fight specificity to do it.
    /// </summary>
    public override int Order => 1000;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (!_options.Enabled || !_options.EnableHeaderSearchBox)
        {
            return;
        }

        // The box is a rule of the default profile - the same profile that answers the search it
        // starts - so it is edited on the profile editor's Search Box tab rather than in
        // configuration. Profiles are cached, so this costs nothing per request. A site with no
        // profile yet still gets a working box from the defaults.
        SearchBoxRules box = _profiles.ResolveForSearch(null)?.Rules.SearchBox ?? new SearchBoxRules();

        if (!box.Enabled)
        {
            return;
        }

        if (string.Equals(context.TagName, "head", StringComparison.OrdinalIgnoreCase))
        {
            // In the head rather than beside the script, so the panel is styled before it can
            // first be opened.
            output.PostContent.AppendHtml(
                $"<link id=\"{StylesheetElementId}\" rel=\"stylesheet\" href=\"{StylesheetPath}\" />");

            return;
        }

        if (string.Equals(context.TagName, "body", StringComparison.OrdinalIgnoreCase))
        {
            output.PostContent.AppendHtml(BuildScriptTag(box));
        }
    }

    private string BuildScriptTag(SearchBoxRules box)
    {
        HtmlEncoder encoder = HtmlEncoder.Default;

        var tag = new StringBuilder("<script src=\"")
            .Append(ScriptPath)
            .Append("\" defer");

        Attribute(tag, "data-containers", box.Containers, encoder);
        Attribute(tag, "data-placeholder", box.Placeholder, encoder);
        Attribute(tag, "data-label", box.ButtonLabel, encoder);
        Attribute(tag, "data-icon", box.IconUrl, encoder);
        Attribute(tag, "data-icon-size", box.IconSize > 0 ? box.IconSize.ToString() : null, encoder);
        Attribute(tag, "data-query", box.QueryName, encoder);
        Attribute(tag, "data-paths", BuildPaths(box), encoder);
        Attribute(tag, "data-excluded", Join(box.ExcludedPaths), encoder);

        // Handed to the script so it can add the stylesheet itself, for a layout that has no
        // <head> for the link above to have been written into.
        Attribute(tag, "data-stylesheet", StylesheetPath, encoder);

        return tag.Append("></script>").ToString();
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

        paths.AddRange(box.ExtraPaths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()));

        return string.Join(',', paths);
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
