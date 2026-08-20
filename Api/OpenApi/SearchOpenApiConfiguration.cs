using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using Umbraco.Cms.Api.Management.OpenApi;

namespace Imobisoft.Search.Api.OpenApi;

/// <summary>
/// Adds the backoffice bearer token requirement to this package's operations, so the Swagger UI can
/// authorise against them.
/// </summary>
public sealed class SearchSecurityRequirementsOperationFilter : BackOfficeSecurityRequirementsOperationFilterBase
{
    protected override string ApiName => ImobisoftSearchConstants.Api.ApiName;
}

/// <summary>
/// Registers a dedicated OpenAPI document for the package.
/// <para>
/// Keeping the endpoints out of Umbraco's own Management API document means an Umbraco upgrade never
/// reshapes our contract, and a consumer can generate a client for just this package.
/// </para>
/// </summary>
public sealed class ConfigureSearchSwaggerOptions : IConfigureOptions<SwaggerGenOptions>
{
    public void Configure(SwaggerGenOptions options)
    {
        options.SwaggerDoc(
            ImobisoftSearchConstants.Api.ApiName,
            new OpenApiInfo
            {
                Title = ImobisoftSearchConstants.Api.ApiTitle,
                Version = "1.0",
                Description =
                    "Backoffice endpoints for Imobisoft Search: search profile management, index discovery "
                    + "and rule previewing. Requires a backoffice user with access to the Search section.",
            });

        options.OperationFilter<SearchSecurityRequirementsOperationFilter>();
    }
}
