using System.Text;
using System.Text.Json;

namespace Imobisoft.Search.Services.Indexing;

/// <summary>
/// Turns whatever a property editor put in the index into clean body text.
/// <para>
/// Block Grid, Block List and the legacy Grid all store their content as JSON, and Umbraco indexes
/// a good deal of it verbatim. Searching that JSON works well enough - the words are in there - but
/// <em>showing</em> it does not: a snippet cut from it reads
/// <c>["Home page-square text layout"] Our World Who we are Home page layout</c>, which is block
/// labels, layout names and structure rather than anything a visitor wanted to read.
/// </para>
/// <para>
/// So the words are kept and the scaffolding is dropped. Everything here is a pure function of its
/// input - no Umbraco, no Examine - because the rules are the whole substance of this and they are
/// worth being able to test one string at a time.
/// </para>
/// </summary>
public static class ExtractedContentBuilder
{
    /// <summary>
    /// JSON property names that carry structure rather than content. Matched exactly and without
    /// case, so a content property that merely happens to contain one of these words is unaffected.
    /// <para>
    /// This is the list that keeps block identity out of the text: a <c>contentTypeKey</c> is a
    /// GUID, an <c>alias</c> is a machine name, and <c>layout</c> is where "page-square text
    /// layout" comes from.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> SkipSubtreeKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        // Block Grid / Block List scaffolding: positions, spans and the keys tying blocks to their
        // definitions. None of it is content, and all of it is where "page-square text layout"
        // lives.
        "layout", "settingsData", "settings",

        // How a property is rendered rather than what it says.
        "editor", "config", "styles", "view", "appearance",
    };

    /// <summary>
    /// JSON property names whose own value is a machine value or a label, but which say nothing
    /// about their children.
    /// <para>
    /// Kept apart from <see cref="SkipSubtreeKeys"/> because the distinction is load bearing: the
    /// legacy Grid nests its content inside <c>sections</c>, <c>rows</c>, <c>areas</c> and
    /// <c>controls</c>, so treating those as scaffolding to skip would throw away the whole
    /// document. They are containers to walk through, not structure to drop.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> SkipValueKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "contentTypeKey", "contentTypeAlias", "contentTypeName", "udi", "key", "id", "$type",
        "alias", "columnSpan", "rowSpan", "areaGridColumns", "gridColumns", "identifier",
        "rowKey", "settingsKey", "contentKey", "name", "label", "icon", "sortOrder", "level",
        "path", "nodeType", "writerName", "creatorName", "template", "culture", "segment",
        "expanded", "disabled", "grid", "cols", "colSpan",

        // A block's own heading, for the same reason as the fields above: the excerpt should read
        // as prose rather than as a run of the headings a visitor can already see on the page.
        "title", "heading", "subheading", "subHeading", "headline", "subtitle", "subTitle",
    };

    /// <summary>
    /// Index field names never read for body text. Umbraco's own bookkeeping, plus the fields that
    /// hold a machine value rather than prose.
    /// </summary>
    private static readonly HashSet<string> ExcludedFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "__NodeId", "__Key", "__IndexType", "__NodeTypeAlias", "__Path", "__Icon",
        "__Published", "__VariesByCulture", "__Raw_", "nodeType", "templateID", "icon",
        "creatorID", "creatorName", "writerID", "writerName", "level", "parentID", "sortOrder",
        "path", "createDate", "updateDate", "published", "trashed", "urlName", "umbracoNaviHide",
        "loginName", "umbracoFile", "umbracoFileSrc", "umbracoExtension", "umbracoBytes",
        "umbracoWidth", "umbracoHeight", "umbracoRedirect", "umbracoUrlAlias", "umbracoUrlName",

        // Headings and titles, which a result already shows as its own link text. Reading them
        // into the excerpt as well means every result opens by repeating its own heading before
        // getting to anything new - the excerpt is there to say what the title did not.
        "nodeName", "pageTitle", "title", "heading", "subheading", "subHeading", "headline",
        "subtitle", "subTitle", "pageHeading", "bannerTitle", "bannerHeading", "metaTitle",
        "seoTitle", "browserTitle", "linkTitle", "navigationTitle", "navTitle", "shortTitle",
        "cardTitle", "listingTitle",
    };

    /// <summary>
    /// Fewest words a string needs before it counts as body text.
    /// <para>
    /// This single threshold is what separates a paragraph from a label, and it is why
    /// "Home page-square text layout" (five words) does not survive while "ECOM operates in more
    /// than 40 major producing countries, working with thousands of farmers" does. A heading like
    /// "Our World" goes too, which is the intended trade: the field exists to produce a readable
    /// excerpt, and a run of headings is not one.
    /// </para>
    /// </summary>
    public const int DefaultMinimumWords = 6;

    /// <summary>How deep to walk nested JSON before giving up. Blocks nest inside blocks.</summary>
    private const int MaxDepth = 12;

    /// <summary>
    /// Ceiling on what is produced for one page. A field that no snippet will ever read past is
    /// only index weight.
    /// </summary>
    public const int DefaultMaximumCharacters = 8000;

    /// <summary>
    /// The clean body text for one document, built from its indexed field values.
    /// </summary>
    /// <param name="values">Field name to raw indexed values, as Examine holds them.</param>
    /// <param name="culture">
    /// Culture to build for, or null for the invariant pass. A culture takes fields suffixed with
    /// it plus the unsuffixed ones, because a page mixes variant and invariant properties and both
    /// are that culture's content.
    /// </param>
    /// <param name="minimumWords">Override for <see cref="DefaultMinimumWords"/>.</param>
    /// <param name="maximumCharacters">Override for <see cref="DefaultMaximumCharacters"/>.</param>
    public static string Build(
        IEnumerable<KeyValuePair<string, IEnumerable<object>>> values,
        string? culture = null,
        int minimumWords = DefaultMinimumWords,
        int maximumCharacters = DefaultMaximumCharacters)
    {
        var collected = new List<string>();

        // Case-insensitive so the same paragraph repeated by two properties - a summary that also
        // opens the body, say - is written once.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var length = 0;

        foreach (KeyValuePair<string, IEnumerable<object>> field in values)
        {
            if (length >= maximumCharacters || !IsCandidateField(field.Key, culture))
            {
                continue;
            }

            foreach (var value in field.Value ?? Enumerable.Empty<object>())
            {
                if (length >= maximumCharacters)
                {
                    break;
                }

                foreach (var text in Extract(value, minimumWords))
                {
                    if (length >= maximumCharacters || !seen.Add(text))
                    {
                        continue;
                    }

                    collected.Add(text);
                    length += text.Length + 1;
                }
            }
        }

        var built = string.Join(" ", collected);

        return built.Length <= maximumCharacters ? built : built[..maximumCharacters].TrimEnd();
    }

    /// <summary>
    /// Whether this field is worth reading for body text, for this culture.
    /// <para>
    /// Umbraco's own <c>__</c>-prefixed fields and its bookkeeping are out, and so is anything for
    /// a different culture - mixing languages into one excerpt would produce an excerpt in neither.
    /// </para>
    /// </summary>
    private static bool IsCandidateField(string? fieldName, string? culture)
    {
        var name = (fieldName ?? string.Empty).Trim();

        if (name.Length == 0 || name.StartsWith("__", StringComparison.Ordinal))
        {
            return false;
        }

        var underscore = name.LastIndexOf('_');

        // A suffix is only a culture if it reads like one - "en", "en-gb". Without that check a
        // property genuinely called "body_text" would be read as the "text" culture and dropped.
        var suffix = underscore > 0 && underscore < name.Length - 1
            ? name[(underscore + 1)..]
            : string.Empty;

        var isVariant = suffix.Length > 0 && LooksLikeCulture(suffix);

        // The exclusion list is checked against the name with any culture stripped off, as well as
        // the name as it stands. On a variant site every property arrives suffixed, so matching
        // only the full name would exclude "title" and let "title_en-gb" straight through - which
        // is to say it would work on a single-language site and silently not on any other.
        var baseName = isVariant ? name[..underscore] : name;

        if (ExcludedFields.Contains(name) || ExcludedFields.Contains(baseName))
        {
            return false;
        }

        if (!isVariant)
        {
            // No culture of its own: an invariant property, which belongs to every culture.
            return true;
        }

        return culture is not null && suffix.Equals(culture, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether a field name suffix reads as a culture code: <c>en</c>, <c>eng</c>, <c>en-gb</c>.
    /// <para>
    /// Shaped tightly on purpose. A looser test treats the suffix of a property genuinely called
    /// <c>body_text</c> as the "text" culture, which then drops that property from every culture's
    /// extract - a field silently missing its content. So the language part is the 2 or 3 letters
    /// ISO actually uses, and the second part is a 2-letter region or a 4-letter script.
    /// </para>
    /// </summary>
    public static bool LooksLikeCulture(string suffix)
    {
        if (suffix.Length is < 2 or > 10)
        {
            return false;
        }

        var parts = suffix.Split('-');

        if (parts.Length is < 1 or > 2 || !parts.All(p => p.All(char.IsLetter)))
        {
            return false;
        }

        if (parts[0].Length is not (2 or 3))
        {
            return false;
        }

        return parts.Length == 1 || parts[1].Length is 2 or 4;
    }

    /// <summary>
    /// Every run of body text inside one indexed value, whatever shape it arrived in: a JSON
    /// document from a block editor, an HTML string from a rich text property, or a plain string.
    /// </summary>
    private static IEnumerable<string> Extract(object? value, int minimumWords)
    {
        var raw = value?.ToString();

        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<string>();
        }

        var trimmed = raw.TrimStart();

        // Block editors store JSON. So does an array of block labels - ["Home page-square text
        // layout"] - which is exactly the thing this exists to drop, and which is dropped by the
        // word count rather than by being JSON.
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            List<string>? fromJson = FromJson(trimmed, minimumWords);

            if (fromJson is not null)
            {
                return fromJson;
            }
        }

        var clean = CleanText(raw);

        return Words(clean) >= minimumWords ? new[] { clean } : Array.Empty<string>();
    }

    /// <summary>
    /// Body text found by walking a JSON document, or null when the text was not JSON after all -
    /// a paragraph that merely opens with a brace, for instance, which then falls through to being
    /// treated as prose.
    /// </summary>
    private static List<string>? FromJson(string json, int minimumWords)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);

            var found = new List<string>();

            Walk(document.RootElement, propertyName: null, depth: 0, minimumWords, found);

            return found;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Collects the body text out of a JSON element, skipping the structure.
    /// <para>
    /// A string is kept on its own merits - it has to read like a paragraph - and skipped outright
    /// if it sits under a property name that carries structure. Both checks matter: the name alone
    /// would miss a layout name stored under a content property, and the word count alone would
    /// keep a long enough machine value.
    /// </para>
    /// </summary>
    private static void Walk(
        JsonElement element,
        string? propertyName,
        int depth,
        int minimumWords,
        List<string> found)
    {
        if (depth > MaxDepth)
        {
            return;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (SkipSubtreeKeys.Contains(property.Name))
                    {
                        continue;
                    }

                    Walk(property.Value, property.Name, depth + 1, minimumWords, found);
                }

                break;

            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                {
                    // The property name travels into the array, because a block editor's content
                    // lives in an array under a named property and the name is the only clue to
                    // what the items are.
                    Walk(item, propertyName, depth + 1, minimumWords, found);
                }

                break;

            case JsonValueKind.String:
                if (propertyName is not null && SkipValueKeys.Contains(propertyName))
                {
                    return;
                }

                var text = element.GetString();

                if (string.IsNullOrWhiteSpace(text) || IsMachineValue(text))
                {
                    return;
                }

                // A string inside JSON can itself be JSON - a nested block editor stored as an
                // escaped document - so it gets the same treatment rather than being read as prose.
                var inner = text.TrimStart();

                if (inner.StartsWith('{') || inner.StartsWith('['))
                {
                    List<string>? nested = FromJson(inner, minimumWords);

                    if (nested is not null)
                    {
                        found.AddRange(nested);
                        return;
                    }
                }

                var clean = CleanText(text);

                if (Words(clean) >= minimumWords)
                {
                    found.Add(clean);
                }

                break;
        }
    }

    /// <summary>
    /// Whether a string is an identifier rather than language: a GUID, a UDI, a URL, or a single
    /// run of characters with no spaces at all.
    /// </summary>
    private static bool IsMachineValue(string value)
    {
        var text = value.Trim();

        if (text.Length == 0)
        {
            return true;
        }

        if (text.StartsWith("umb://", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (Guid.TryParse(text, out _))
        {
            return true;
        }

        // No whitespace anywhere: an alias, a class name, a file name. Body text has spaces.
        return !text.Any(char.IsWhiteSpace);
    }

    /// <summary>
    /// Plain readable text: markup removed, entities decoded, whitespace collapsed.
    /// <para>
    /// A closing tag counts as a word boundary, so <c>one&lt;/p&gt;&lt;p&gt;two</c> does not become
    /// "onetwo" - which would both read wrongly and corrupt the word count this relies on.
    /// </para>
    /// </summary>
    public static string CleanText(string value)
    {
        var builder = new StringBuilder(value.Length);
        var insideTag = false;
        var lastWasSpace = true;

        foreach (var c in value)
        {
            if (c == '<')
            {
                insideTag = true;
                continue;
            }

            if (c == '>')
            {
                insideTag = false;

                if (!lastWasSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }

                continue;
            }

            if (insideTag)
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }

                continue;
            }

            builder.Append(c);
            lastWasSpace = false;
        }

        var decoded = System.Net.WebUtility.HtmlDecode(builder.ToString()).Trim();

        // Decoding can reintroduce whitespace - &nbsp; and &#10; among others - so it is collapsed
        // again afterwards rather than before.
        return CollapseWhitespace(decoded);
    }

    private static string CollapseWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var lastWasSpace = true;

        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }

                continue;
            }

            builder.Append(c);
            lastWasSpace = false;
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// How many words a cleaned string holds. Counted on whitespace runs, which is all the word
    /// count is used for - deciding whether this reads like a paragraph.
    /// </summary>
    public static int Words(string value)
    {
        var count = 0;
        var inWord = false;

        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c))
            {
                inWord = false;
                continue;
            }

            if (!inWord)
            {
                count++;
                inWord = true;
            }
        }

        return count;
    }
}
