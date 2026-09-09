using Imobisoft.Search.Configuration;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.OperationStatus;
using Umbraco.Cms.Core.Strings;

namespace Imobisoft.Search.Migrations;

/// <summary>
/// Creates the document type and template a site needs to add a search page from the Content tree.
/// <para>
/// The package already serves its own page at <c>/imobisoft-search</c>, but a site usually wants the
/// search to be a real content node - somewhere in the tree, with a name and a URL an editor
/// controls. That needs a document type, and a document type needs a template, and a template is a
/// file. This creates all three once, so an editor only has to add the page.
/// </para>
/// <para>
/// The template is a single line that renders the package's view component, so every piece of
/// markup - and the profile's selected theme - stays inside the package. Editing that one line is
/// how a site takes the page over.
/// </para>
/// <para>
/// Idempotent, and skipped entirely with <c>Imobisoft:Search:CreateSearchPageDocumentType</c> set
/// to <c>false</c>. Nothing is ever modified: if the document type alias already exists, it is left
/// exactly as it is.
/// </para>
/// </summary>
public sealed class SearchPageScaffolder
{
    /// <summary>Alias of both the document type and its template.</summary>
    public const string DocumentTypeAlias = "imobiSearch";

    public const string DocumentTypeName = "Imobi Search";

    /// <summary>
    /// What the generated template contains. One line, so the markup lives in the package and a
    /// theme change in the backoffice is reflected here without touching the file.
    /// </summary>
    private const string TemplateContent = """
@using Umbraco.Cms.Web.Common.PublishedModels;
@inherits Umbraco.Cms.Web.Common.Views.UmbracoViewPage
@{
    Layout = null;
}
@*
    Imobi Search page.

    The whole experience - the filter dropdowns above the search box, the results, spelling
    suggestions and Load More - comes from the package, rendered with whichever theme the search
    profile selects. Change the theme under Search > your profile > Results, not here.

    To put this inside the site's own chrome, set Layout above to your master view and delete the
    <!DOCTYPE> ... </html> wrapper below - the search block itself is the one line in the middle.
    To take the markup over completely, create Views/Partials/Search/Themes/<name>/ and pick that
    theme in the backoffice.
*@
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    @* Without this the page renders at desktop width on a phone, which is what an unstyled search
       page looks like when it is really just missing its viewport. *@
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>@Model.Name</title>
</head>
<body>
    @await Component.InvokeAsync("ImobisoftSearchListing")
</body>
</html>
""";

    private readonly IContentTypeService _contentTypeService;
    private readonly ITemplateService _templateService;
    private readonly IShortStringHelper _shortStringHelper;
    private readonly ImobisoftSearchOptions _options;
    private readonly ILogger<SearchPageScaffolder> _logger;

    public SearchPageScaffolder(
        IContentTypeService contentTypeService,
        ITemplateService templateService,
        IShortStringHelper shortStringHelper,
        ImobisoftSearchOptions options,
        ILogger<SearchPageScaffolder> logger)
    {
        _contentTypeService = contentTypeService;
        _templateService = templateService;
        _shortStringHelper = shortStringHelper;
        _options = options;
        _logger = logger;
    }

    public async Task ScaffoldAsync()
    {
        if (!_options.CreateSearchPageDocumentType)
        {
            return;
        }

        try
        {
            // Looked up by alias, which is the identity a site would collide on - a document type
            // already using it is left exactly as it is.
            if (_contentTypeService.Get(DocumentTypeAlias) is not null)
            {
                return;
            }

            ITemplate? template = await EnsureTemplateAsync();

            var contentType = new ContentType(_shortStringHelper, -1)
            {
                Alias = DocumentTypeAlias,
                Name = DocumentTypeName,
                Description = "A search page powered by Imobisoft Search. Renders the filters, "
                              + "search box and results using the theme selected on the search profile.",
                Icon = "icon-search color-blue",

                // Allowed at the root as well as under other pages, so an editor can put the search
                // wherever the site's structure wants it rather than only under the home page.
                AllowedAsRoot = true,
                IsElement = false,
            };

            if (template is not null)
            {
                contentType.AllowedTemplates = new[] { template };
                contentType.SetDefaultTemplate(template);
            }

            await _contentTypeService.CreateAsync(contentType, Constants.Security.SuperUserKey);

            _logger.LogInformation(
                "Created the '{Alias}' document type. Add a page of that type in Content and it "
                + "serves search with the profile's selected theme.",
                DocumentTypeAlias);
        }
        catch (Exception ex)
        {
            // Never block startup over a convenience: the package's own page at /imobisoft-search
            // still works, and the document type can be made by hand.
            _logger.LogWarning(
                ex,
                "Could not create the '{Alias}' document type. Add it by hand, with a template "
                + "containing @await Component.InvokeAsync(\"ImobisoftSearchListing\").",
                DocumentTypeAlias);
        }
    }

    private async Task<ITemplate?> EnsureTemplateAsync()
    {
        ITemplate? existing = await _templateService.GetAsync(DocumentTypeAlias);

        if (existing is not null)
        {
            return existing;
        }

        Attempt<ITemplate, TemplateOperationStatus> created = await _templateService.CreateAsync(
            DocumentTypeName,
            DocumentTypeAlias,
            TemplateContent,
            Constants.Security.SuperUserKey);

        if (!created.Success)
        {
            // A document type without a template still renders nothing, so this is worth saying out
            // loud rather than leaving an editor to wonder why their page is blank.
            _logger.LogWarning(
                "Could not create the '{Alias}' template ({Status}). The document type is still "
                + "created; give it a template rendering the ImobisoftSearchListing view component.",
                DocumentTypeAlias,
                created.Status);

            return null;
        }

        return created.Result;
    }
}
