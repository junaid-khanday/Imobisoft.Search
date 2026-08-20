using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Common.Attributes;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.Routing;
using Umbraco.Cms.Web.Common.Authorization;

namespace Imobisoft.Search.Api;

/// <summary>
/// Base for the package's backoffice endpoints.
/// <para>
/// <see cref="MapToApiAttribute"/> puts them in their own OpenAPI document rather than mixing them
/// into Umbraco's. The two authorization attributes are combined by ASP.NET Core into a single
/// policy, so a caller must both be signed in to the backoffice - which is where the bearer token
/// and its authentication scheme come from - and hold access to the Search section.
/// </para>
/// </summary>
[ApiVersion("1.0")]
[VersionedApiBackOfficeRoute(ImobisoftSearchConstants.Api.RouteBase)]
[MapToApi(ImobisoftSearchConstants.Api.ApiName)]
[ApiExplorerSettings(GroupName = ImobisoftSearchConstants.Api.GroupName)]
[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]
[Authorize(Policy = ImobisoftSearchConstants.Api.SectionAccessPolicy)]
public abstract class ImobisoftSearchControllerBase : ManagementApiControllerBase
{
}
