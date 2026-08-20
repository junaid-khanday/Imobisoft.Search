using Imobisoft.Search.Persistence;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Migrations;

namespace Imobisoft.Search.Migrations;

/// <summary>
/// Adds the two analytics tables. A separate step from the profile table so that a site upgrading
/// from an earlier version of the package gets them without touching what is already there.
/// </summary>
public class AddSearchAnalyticsTables : AsyncMigrationBase
{
    public AddSearchAnalyticsTables(IMigrationContext context)
        : base(context)
    {
    }

    protected override Task MigrateAsync()
    {
        CreateIfMissing(ImobisoftSearchConstants.Database.QueryTableName, () => Create.Table<SearchQuerySchema>().Do());
        CreateIfMissing(ImobisoftSearchConstants.Database.ClickTableName, () => Create.Table<SearchClickSchema>().Do());

        return Task.CompletedTask;
    }

    private void CreateIfMissing(string tableName, Action create)
    {
        if (TableExists(tableName))
        {
            Logger.LogDebug("{Table} already exists, skipping creation.", tableName);
            return;
        }

        create();
        Logger.LogInformation("Created {Table} for Imobisoft.Search.", tableName);
    }
}
