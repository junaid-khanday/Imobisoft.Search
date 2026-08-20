using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace Imobisoft.Search.Persistence;

/// <summary>
/// The table the package creates in the consumer's Umbraco database.
/// <para>
/// This class defines a shipped database schema, so it must not change once released - a migration
/// adds a new step instead. The rules themselves live in <see cref="ConfigJson"/> rather than in
/// columns, so adding a new rule never needs a schema migration.
/// </para>
/// <para>
/// Column names deliberately avoid <c>key</c> and <c>alias</c>, which are reserved words in some of
/// the databases Umbraco supports.
/// </para>
/// </summary>
[TableName(ImobisoftSearchConstants.Database.ProfileTableName)]
[PrimaryKey("id", AutoIncrement = true)]
[ExplicitColumns]
public class SearchProfileSchema
{
    [PrimaryKeyColumn(AutoIncrement = true, IdentitySeed = 1)]
    [Column("id")]
    public int Id { get; set; }

    [Column("uniqueId")]
    [Index(IndexTypes.UniqueNonClustered, Name = "IX_imobisoftSearchProfile_uniqueId")]
    public Guid UniqueId { get; set; }

    [Column("profileAlias")]
    [Length(255)]
    [Index(IndexTypes.UniqueNonClustered, Name = "IX_imobisoftSearchProfile_profileAlias")]
    public string ProfileAlias { get; set; } = string.Empty;

    [Column("profileName")]
    [Length(255)]
    public string ProfileName { get; set; } = string.Empty;

    [Column("isDefault")]
    public bool IsDefault { get; set; }

    [Column("isEnabled")]
    public bool IsEnabled { get; set; }

    [Column("configJson")]
    [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
    public string ConfigJson { get; set; } = "{}";

    [Column("createDate")]
    public DateTime CreateDate { get; set; }

    [Column("updateDate")]
    public DateTime UpdateDate { get; set; }
}
