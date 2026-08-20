using Microsoft.AspNetCore.Authorization;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Security;

namespace Imobisoft.Search.Api.Security;

/// <summary>Requires the current backoffice user to have been granted the Search section.</summary>
public sealed class ImobisoftSearchSectionRequirement : IAuthorizationRequirement
{
}

/// <summary>
/// Checks the Search section against the user's granted sections.
/// <para>
/// Umbraco only publishes section policies for its own built-in sections, so a package that adds a
/// section has to bring its own. Without this, any authenticated backoffice user - including one
/// with access to nothing but Content - could call the package's endpoints and read the entire
/// index.
/// </para>
/// </summary>
public sealed class ImobisoftSearchSectionHandler : AuthorizationHandler<ImobisoftSearchSectionRequirement>
{
    private readonly IBackOfficeSecurityAccessor _backOfficeSecurityAccessor;

    public ImobisoftSearchSectionHandler(IBackOfficeSecurityAccessor backOfficeSecurityAccessor)
        => _backOfficeSecurityAccessor = backOfficeSecurityAccessor;

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ImobisoftSearchSectionRequirement requirement)
    {
        IBackOfficeSecurity? security = _backOfficeSecurityAccessor.BackOfficeSecurity;
        IUser? user = security?.CurrentUser;

        if (user is not null
            && security!.UserHasSectionAccess(ImobisoftSearchConstants.Sections.Search, user))
        {
            context.Succeed(requirement);
        }

        // Not calling Fail() leaves other handlers free to satisfy the requirement; the policy still
        // fails overall if nothing succeeds.
        return Task.CompletedTask;
    }
}
