namespace Imobisoft.Search;

/// <summary>
/// Aliases, names and keys used across the package. Anything that appears in the database, in a
/// route, or in the backoffice manifest lives here so the C# and JavaScript sides cannot drift.
/// </summary>
public static class ImobisoftSearchConstants
{
    public const string PackageName = "Imobisoft.Search";

    public static class Api
    {
        /// <summary>Name of the dedicated OpenAPI document, so our endpoints get their own Swagger doc.</summary>
        public const string ApiName = "imobisoft-search";

        public const string ApiTitle = "Imobisoft Search API";

        /// <summary>Appended to the backoffice management API base route.</summary>
        public const string RouteBase = "imobisoft-search";

        public const string GroupName = "Imobisoft Search";

        /// <summary>
        /// Authorization policy requiring the signed-in backoffice user to have been granted the
        /// Search section. These endpoints can read the whole content index, so backoffice
        /// authentication on its own is not enough.
        /// </summary>
        public const string SectionAccessPolicy = "Imobisoft.Search.SectionAccess";
    }

    public static class Sections
    {
        public const string Search = "Imobisoft.Section.Search";
    }

    public static class Web
    {
        /// <summary>
        /// Where the package's built-in search page is served from. Deliberately not <c>/search</c>,
        /// which a site is likely to want for a content node of its own.
        /// </summary>
        public const string SearchPagePath = "imobisoft-search";
    }

    public static class Database
    {
        public const string ProfileTableName = "imobisoftSearchProfile";
        public const string QueryTableName = "imobisoftSearchQuery";
        public const string ClickTableName = "imobisoftSearchClick";

        public const string MigrationPlanName = "Imobisoft.Search";
        public const string MigrationInitState = "imobisoft-search-init";
        public const string MigrationAnalyticsState = "imobisoft-search-analytics";
    }

    public static class Settings
    {
        /// <summary>Key the site-wide settings JSON is stored under in Umbraco's key/value table.</summary>
        public const string KeyValueKey = "Imobisoft.Search.Settings";

        public const string CacheKey = "Imobisoft.Search.Settings.Cache";
    }

    public static class Profiles
    {
        public const string DefaultAlias = "default";
        public const string DefaultName = "Default";
    }

    public static class Caching
    {
        public const string ProfileCacheKey = "Imobisoft.Search.Profiles";
    }

    /// <summary>
    /// Field names Umbraco/Examine writes into every document. Used for filtering and for excluding
    /// internal plumbing from the "searchable fields" pick-list in the dashboard.
    /// </summary>
    public static class IndexFields
    {
        public const string NodeName = "nodeName";
        public const string SystemNodeName = "__NodeName";
        public const string NodeTypeAlias = "__NodeTypeAlias";
        public const string IndexType = "__IndexType";
        public const string Key = "__Key";
        public const string Path = "__Path";
        public const string Icon = "__Icon";
        public const string Published = "__Published";
        public const string VariesByCulture = "__VariesByCulture";
        public const string Culture = "__Culture";
        public const string Raw = "__Raw_";
        public const string SortPrefix = "__Sort_";

        public const string CreateDate = "createDate";
        public const string UpdateDate = "updateDate";
        public const string NaviHide = "umbracoNaviHide";
        public const string UrlName = "urlName";
        public const string Level = "level";
        public const string ParentId = "parentID";

        /// <summary>
        /// Fields Umbraco writes for bookkeeping rather than for reading. They carry no declared field
        /// definition, so without this list they would be auto-detected as free text and drag every
        /// node id, template id and icon name into the searchable field set.
        /// </summary>
        public static readonly IReadOnlySet<string> Metadata = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "id",
            "nodeType",
            "templateID",
            "icon",
            "creatorID",
            "creatorName",
            "writerID",
            "writerName",
            "level",
            "parentID",
            "sortOrder",
            "path",
            "createDate",
            "updateDate",
            "published",
            "trashed",
            "urlName",
            "umbracoNaviHide",
            "loginName",
        };
    }

    public static class IndexTypes
    {
        public const string Content = "content";
        public const string Media = "media";
        public const string Member = "member";
    }
}
