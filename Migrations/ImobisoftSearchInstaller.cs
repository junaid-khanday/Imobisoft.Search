using Imobisoft.Search.Models;
using Imobisoft.Search.Persistence;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Migrations;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Migrations;
using Umbraco.Cms.Infrastructure.Migrations.Upgrade;

namespace Imobisoft.Search.Migrations;

/// <summary>
/// Everything the package needs to do to a consumer site on first boot: create its table, seed a
/// profile that searches everything, and make the Search section visible to administrators.
/// <para>
/// This runs the migration plan directly rather than relying on Umbraco's package migration runner,
/// so it does not depend on the consumer's <c>PackageMigrationsUnattended</c> setting.
/// </para>
/// </summary>
public sealed class ImobisoftSearchInstaller : INotificationAsyncHandler<UmbracoApplicationStartingNotification>
{
    private readonly ICoreScopeProvider _coreScopeProvider;
    private readonly IMigrationPlanExecutor _migrationPlanExecutor;
    private readonly IKeyValueService _keyValueService;
    private readonly IRuntimeState _runtimeState;
    private readonly ISearchProfileRepository _profileRepository;
    private readonly IUserGroupService _userGroupService;
    private readonly ILogger<ImobisoftSearchInstaller> _logger;

    public ImobisoftSearchInstaller(
        ICoreScopeProvider coreScopeProvider,
        IMigrationPlanExecutor migrationPlanExecutor,
        IKeyValueService keyValueService,
        IRuntimeState runtimeState,
        ISearchProfileRepository profileRepository,
        IUserGroupService userGroupService,
        ILogger<ImobisoftSearchInstaller> logger)
    {
        _coreScopeProvider = coreScopeProvider;
        _migrationPlanExecutor = migrationPlanExecutor;
        _keyValueService = keyValueService;
        _runtimeState = runtimeState;
        _profileRepository = profileRepository;
        _userGroupService = userGroupService;
        _logger = logger;
    }

    public async Task HandleAsync(UmbracoApplicationStartingNotification notification, CancellationToken cancellationToken)
    {
        // Before Run the database may not exist yet (install/upgrade screens). Umbraco fires this
        // notification again once the runtime reaches Run.
        if (_runtimeState.Level < RuntimeLevel.Run)
        {
            return;
        }

        if (!await RunMigrationsAsync())
        {
            return;
        }

        SeedDefaultProfile();
        await GrantSectionToAdministratorsAsync();
    }

    private async Task<bool> RunMigrationsAsync()
    {
        try
        {
            var plan = new MigrationPlan(ImobisoftSearchConstants.Database.MigrationPlanName);

            plan.From(string.Empty)
                .To<AddSearchProfileTable>(ImobisoftSearchConstants.Database.MigrationInitState)
                .To<AddSearchAnalyticsTables>(ImobisoftSearchConstants.Database.MigrationAnalyticsState);

            await new Upgrader(plan).ExecuteAsync(_migrationPlanExecutor, _coreScopeProvider, _keyValueService);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Imobisoft.Search could not run its database migration. Search will be unavailable.");
            return false;
        }
    }

    /// <summary>
    /// Seeds the profile that makes the package useful the moment it is installed: no include or
    /// exclude rules at all, which the engine reads as "search every index and every document type".
    /// </summary>
    private void SeedDefaultProfile()
    {
        try
        {
            if (_profileRepository.Count() > 0)
            {
                return;
            }

            SearchProfile profile = SearchProfile.CreateDefault();
            _profileRepository.Save(profile);

            _logger.LogInformation("Seeded the default Imobisoft.Search profile '{Alias}'.", profile.Alias);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Imobisoft.Search could not seed its default search profile.");
        }
    }

    /// <summary>
    /// Custom sections are hidden until a user group is granted access. Granting it to the
    /// administrators group on install means the section is simply there after the first run, rather
    /// than the package appearing to have done nothing. Other groups are still granted by hand.
    /// </summary>
    private async Task GrantSectionToAdministratorsAsync()
    {
        try
        {
            IUserGroup? adminGroup = await _userGroupService.GetAsync(Constants.Security.AdminGroupAlias);

            if (adminGroup is null || adminGroup.AllowedSections.Contains(ImobisoftSearchConstants.Sections.Search))
            {
                return;
            }

            adminGroup.AddAllowedSection(ImobisoftSearchConstants.Sections.Search);
            await _userGroupService.UpdateAsync(adminGroup, Constants.Security.SuperUserKey);

            _logger.LogInformation(
                "Granted the {Section} section to the administrators group.",
                ImobisoftSearchConstants.Sections.Search);
        }
        catch (Exception ex)
        {
            // Never block startup over a permission convenience - the section can be granted in the UI.
            _logger.LogWarning(
                ex,
                "Could not grant the {Section} section to the administrators group. Grant it manually under Users > User groups.",
                ImobisoftSearchConstants.Sections.Search);
        }
    }
}
