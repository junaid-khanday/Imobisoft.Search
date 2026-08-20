using Imobisoft.Search.Persistence;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Migrations;

namespace Imobisoft.Search.Migrations;

/// <summary>
/// Creates the profile table in the consumer's database. Runs once, on the first boot after the
/// package is installed.
/// </summary>
public class AddSearchProfileTable : AsyncMigrationBase
{
    public AddSearchProfileTable(IMigrationContext context)
        : base(context)
    {
    }

    protected override Task MigrateAsync()
    {
        if (TableExists(ImobisoftSearchConstants.Database.ProfileTableName))
        {
            Logger.LogDebug(
                "{Table} already exists, skipping creation.",
                ImobisoftSearchConstants.Database.ProfileTableName);
            return Task.CompletedTask;
        }

        Create.Table<SearchProfileSchema>().Do();

        Logger.LogInformation(
            "Created {Table} for Imobisoft.Search.",
            ImobisoftSearchConstants.Database.ProfileTableName);

        return Task.CompletedTask;
    }
}
