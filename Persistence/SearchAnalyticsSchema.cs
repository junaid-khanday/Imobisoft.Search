using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace Imobisoft.Search.Persistence;

/// <summary>
/// One recorded search. Shipped schema - do not change; add a migration step instead.
/// <para>
/// <c>normalizedTerm</c> is stored alongside the raw term rather than computed at report time, so
/// grouping "what are people searching for" is an indexed GROUP BY rather than a table scan.
/// </para>
/// </summary>
[TableName(ImobisoftSearchConstants.Database.QueryTableName)]
[PrimaryKey("id", AutoIncrement = true)]
[ExplicitColumns]
public class SearchQuerySchema
{
    [PrimaryKeyColumn(AutoIncrement = true, IdentitySeed = 1)]
    [Column("id")]
    public int Id { get; set; }

    [Column("uniqueId")]
    [Index(IndexTypes.UniqueNonClustered, Name = "IX_imobisoftSearchQuery_uniqueId")]
    public Guid UniqueId { get; set; }

    [Column("profileAlias")]
    [Length(255)]
    public string ProfileAlias { get; set; } = string.Empty;

    [Column("term")]
    [Length(500)]
    public string Term { get; set; } = string.Empty;

    [Column("normalizedTerm")]
    [Length(500)]
    [Index(IndexTypes.NonClustered, Name = "IX_imobisoftSearchQuery_normalizedTerm")]
    public string NormalizedTerm { get; set; } = string.Empty;

    [Column("resultCount")]
    public int ResultCount { get; set; }

    [Column("durationMs")]
    public int DurationMs { get; set; }

    [Column("culture")]
    [Length(20)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? Culture { get; set; }

    [Column("createDate")]
    [Index(IndexTypes.NonClustered, Name = "IX_imobisoftSearchQuery_createDate")]
    public DateTime CreateDate { get; set; }
}

/// <summary>
/// A result a visitor opened. Kept in its own table rather than as a column on the query, because a
/// single search can lead to several clicks. Shipped schema - do not change.
/// </summary>
[TableName(ImobisoftSearchConstants.Database.ClickTableName)]
[PrimaryKey("id", AutoIncrement = true)]
[ExplicitColumns]
public class SearchClickSchema
{
    [PrimaryKeyColumn(AutoIncrement = true, IdentitySeed = 1)]
    [Column("id")]
    public int Id { get; set; }

    [Column("queryUniqueId")]
    [Index(IndexTypes.NonClustered, Name = "IX_imobisoftSearchClick_queryUniqueId")]
    public Guid QueryUniqueId { get; set; }

    [Column("nodeKey")]
    public Guid NodeKey { get; set; }

    [Column("nodeName")]
    [Length(255)]
    public string NodeName { get; set; } = string.Empty;

    [Column("position")]
    public int Position { get; set; }

    [Column("createDate")]
    [Index(IndexTypes.NonClustered, Name = "IX_imobisoftSearchClick_createDate")]
    public DateTime CreateDate { get; set; }
}
