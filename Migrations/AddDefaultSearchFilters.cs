using Imobisoft.Search.Models;
using Imobisoft.Search.Persistence;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Migrations;
using Umbraco.Extensions;

namespace Imobisoft.Search.Migrations;

/// <summary>
/// Gives every existing profile the filter bar a fresh install now gets.
/// <para>
/// The package used to seed a profile with no facets and no sort options, which left the front-end
/// search page with nothing above the search box - the filter bar is driven by facet
/// <em>definitions</em>, so with none defined there was nothing to render. Sites that installed the
/// package before <see cref="SearchProfile.CreateDefault"/> seeded them would otherwise have kept
/// the bare page forever, since the installer only seeds a profile when there is not one already.
/// </para>
/// <para>
/// Only profiles that define neither a facet nor a sort option are touched, so a site that
/// configured its own filters keeps exactly what it configured. Being a migration step rather than
/// a startup task, it also runs once and once only: an editor who then deletes the filters does not
/// find them back on the next boot.
/// </para>
/// </summary>
public class AddDefaultSearchFilters : AsyncMigrationBase
{
    public AddDefaultSearchFilters(IMigrationContext context)
        : base(context)
    {
    }

    protected override Task MigrateAsync()
    {
        // The install path runs this before any profile is seeded, so an empty (or absent) table is
        // the normal case rather than a problem - the seeded profile already carries the defaults.
        if (!TableExists(ImobisoftSearchConstants.Database.ProfileTableName))
        {
            return Task.CompletedTask;
        }

        List<SearchProfileSchema> rows = Database.Fetch<SearchProfileSchema>(
            Sql().SelectAll().From<SearchProfileSchema>());

        var updated = 0;

        foreach (SearchProfileSchema row in rows)
        {
            SearchRuleSet rules = SearchProfileJson.DeserializeOrDefault<SearchRuleSet>(row.ConfigJson);

            // Either one being present means someone has already made a decision about this
            // profile's filters. Adding to it would be overwriting that decision.
            if (rules.Results.Facets.Count > 0 || rules.Results.SortOptions.Count > 0)
            {
                continue;
            }

            rules.Results.Facets = SearchProfile.DefaultFacets();
            rules.Results.SortOptions = SearchProfile.DefaultSortOptions();

            row.ConfigJson = SearchProfileJson.Serialize(rules);
            row.UpdateDate = DateTime.UtcNow;

            Database.Update(row);
            updated++;
        }

        if (updated > 0)
        {
            Logger.LogInformation(
                "Added the default Imobisoft.Search filters and sort options to {Count} profile(s).",
                updated);
        }

        return Task.CompletedTask;
    }
}
