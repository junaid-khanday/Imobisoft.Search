using System.Text.Json.Serialization;

namespace Imobisoft.Search.Models;

/// <summary>How much text to return around a match.</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<SnippetMode>))]
public enum SnippetMode
{
    /// <summary>
    /// The whole sentence the matched word sits in. Reads naturally, because it starts and ends
    /// where the author's sentence does rather than mid-word.
    /// </summary>
    Sentence,

    /// <summary>A fixed number of characters centred on the match, cut at word boundaries.</summary>
    Characters,
}

/// <summary>Controls the text snippet returned alongside each result.</summary>
public sealed class HighlightRules
{
    /// <summary>Whether to return a snippet of matching text at all. On by default - snippets are
    /// what make a result list scannable.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether the matched word is wrapped in <see cref="StartTag"/> and <see cref="EndTag"/> inside
    /// the snippet. Independent of <see cref="Enabled"/>, so a site can show the sentence plain, or
    /// highlight without changing how much text comes back.
    /// </summary>
    public bool HighlightMatches { get; set; } = true;

    /// <summary>Whether the snippet is a whole sentence or a fixed span of characters.</summary>
    public SnippetMode Mode { get; set; } = SnippetMode.Sentence;

    /// <summary>
    /// Field the snippet is cut from. Empty means the first configured searchable field that has a
    /// value on the document.
    /// </summary>
    public string Field { get; set; } = string.Empty;

    /// <summary>
    /// Maximum snippet length in characters. In sentence mode this is a ceiling that stops a very
    /// long sentence running away, not the target length.
    /// </summary>
    public int SnippetLength { get; set; } = 200;

    /// <summary>
    /// How many sentences either side of the matching one to include, for a little more context.
    /// 0 returns just the sentence the word is in.
    /// </summary>
    public int SentenceContext { get; set; }

    /// <summary>Wraps the matched term when <see cref="HighlightMatches"/> is on.</summary>
    public string StartTag { get; set; } = "<mark>";

    public string EndTag { get; set; } = "</mark>";

    /// <summary>
    /// Builds the wrapping tag from <see cref="Style"/> instead of using
    /// <see cref="StartTag"/>/<see cref="EndTag"/> as written.
    /// <para>
    /// Off by default, so a profile that already names its own tag keeps it. On, the colour, weight
    /// and shape are set in the backoffice and emitted as an inline style - which is what makes
    /// them work without a stylesheet change, on the package's own page and on a site's own copy
    /// of it alike.
    /// </para>
    /// </summary>
    public bool UseStyleSettings { get; set; }

    /// <summary>How a highlighted term looks, when <see cref="UseStyleSettings"/> is on.</summary>
    public HighlightStyleRules Style { get; set; } = new();

    /// <summary>
    /// The opening tag to wrap a matched term in: either the configured tag, or one built from
    /// <see cref="Style"/>.
    /// </summary>
    public string ResolvedStartTag()
        => UseStyleSettings ? Style.BuildStartTag() : StartTag;

    /// <summary>The matching closing tag.</summary>
    public string ResolvedEndTag()
        => UseStyleSettings ? HighlightStyleRules.CloseTag : EndTag;
}

/// <summary>
/// How a highlighted search term looks: its colour, its weight, and the shape of the mark behind
/// it.
/// <para>
/// Emitted as an inline style rather than a class, because the snippet is written into a page this
/// package does not own - often a site's own copy of the search view - and a class would only work
/// if that site happened to define it. An inline style works everywhere or nowhere.
/// </para>
/// <para>
/// Every value here is validated before it reaches the markup. That is not belt and braces: the
/// snippet is rendered with <c>Html.Raw</c> - it has to be, or the highlight tag would show as
/// text - so a colour of <c>red"&gt;&lt;script&gt;</c> saved by an editor would otherwise be a
/// script tag on the search page of every site running this profile. Nothing is passed through.
/// </para>
/// </summary>
public sealed class HighlightStyleRules
{
    /// <summary>The closing tag that matches what <see cref="BuildStartTag"/> opens.</summary>
    public const string CloseTag = "</mark>";

    /// <summary>Text colour of the matched term. Empty keeps the surrounding text's colour.</summary>
    public string TextColour { get; set; } = "#15803d";

    /// <summary>
    /// Colour behind the matched term. Empty draws no background, which reads as bold coloured
    /// text rather than a marker pen across the sentence.
    /// </summary>
    public string BackgroundColour { get; set; } = string.Empty;

    public bool Bold { get; set; } = true;

    public bool Italic { get; set; }

    public bool Underline { get; set; }

    /// <summary>
    /// Size of the matched term relative to the text around it, as a percentage. 100 leaves it
    /// alone; much above that and the line spacing of the snippet starts to jump around it.
    /// </summary>
    public int FontScalePercent { get; set; } = 100;

    /// <summary>Corner rounding of the background, in pixels. 0 is square; a large value is a pill.</summary>
    public int CornerRadius { get; set; }

    /// <summary>Space between the term and the edge of its background, in pixels.</summary>
    public int PaddingX { get; set; }

    public int PaddingY { get; set; }

    /// <summary>
    /// The opening tag, with every value validated and anything unrecognised dropped.
    /// <para>
    /// A <c>mark</c> element rather than a <c>span</c>: it is what the element is for, and it
    /// carries the meaning to a screen reader even when the styling says nothing.
    /// </para>
    /// </summary>
    public string BuildStartTag()
    {
        var declarations = new List<string>();

        if (Colour(TextColour) is { } text)
        {
            declarations.Add("color:" + text);
        }

        // Always stated, so the browser's own yellow <mark> default never shows through a profile
        // that deliberately asked for no background.
        declarations.Add("background:" + (Colour(BackgroundColour) ?? "transparent"));

        declarations.Add("font-weight:" + (Bold ? "700" : "inherit"));

        if (Italic)
        {
            declarations.Add("font-style:italic");
        }

        declarations.Add("text-decoration:" + (Underline ? "underline" : "none"));

        var scale = Math.Clamp(FontScalePercent <= 0 ? 100 : FontScalePercent, 50, 300);

        if (scale != 100)
        {
            declarations.Add("font-size:" + scale + "%");
        }

        var radius = Math.Clamp(CornerRadius, 0, 40);

        if (radius > 0)
        {
            declarations.Add("border-radius:" + radius + "px");
        }

        var padX = Math.Clamp(PaddingX, 0, 40);
        var padY = Math.Clamp(PaddingY, 0, 40);

        if (padX > 0 || padY > 0)
        {
            declarations.Add("padding:" + padY + "px " + padX + "px");
        }

        return "<mark style=\"" + string.Join(";", declarations) + "\">";
    }

    /// <summary>
    /// A colour the markup can safely carry, or null.
    /// <para>
    /// An allowlist, not an escape: only <c>#rgb</c>, <c>#rrggbb</c>, <c>#rrggbbaa</c> and the CSS
    /// colour keywords get through, so there is no sequence a value could take that would end the
    /// attribute or the tag. Anything else is dropped rather than corrected - a colour that does
    /// not apply is a visible, harmless mistake, and guessing what someone meant is how a sanitiser
    /// ends up passing something through.
    /// </para>
    /// </summary>
    public static string? Colour(string? value)
    {
        var text = (value ?? string.Empty).Trim();

        if (text.Length is 0 or > 32)
        {
            return null;
        }

        if (text[0] == '#')
        {
            var digits = text[1..];

            return digits.Length is 3 or 6 or 8 && digits.All(Uri.IsHexDigit)
                ? text.ToLowerInvariant()
                : null;
        }

        // A keyword is letters only, which rules out every character that could matter: no quotes,
        // no angle brackets, no semicolons, no parentheses, so no rgb() or url() either.
        return text.All(char.IsAsciiLetter) && text.Length <= 24
            ? text.ToLowerInvariant()
            : null;
    }
}

/// <summary>Defines the <em>shape</em> of what comes back: paging, fields and post-processing.</summary>
public sealed class ResultRules
{
    public int PageSize { get; set; } = 10;
    public bool EnableLoadMore { get; set; } = false;

    /// <summary>
    /// How many results a search page shows before the visitor has typed anything (browse mode).
    /// Defaults to the normal page size behaviour with 10. Set to 0 to open the page with no
    /// results at all - filters still show their counts, but nothing is listed until a search
    /// term is submitted.
    /// </summary>
    public int BrowsePageSize { get; set; } = 10;

    /// <summary>
    /// The pages the search page opens with, picked from the content tree, in the order they are
    /// picked. This is the curated version of <see cref="BrowsePageSize"/>: where that says how
    /// many of whatever the index returns to list, this says exactly which pages to list.
    /// <para>
    /// Pick nothing - the default - and the automatic list is used, as before. Pick anything and
    /// those pages are what an untouched search page shows, all of them, whatever
    /// <see cref="BrowsePageSize"/> says; it goes back to governing the automatic list the moment
    /// the picks are cleared. Nothing here affects a real search: the moment a visitor types, the
    /// results are the ones that matched.
    /// </para>
    /// <para>
    /// A page that has since been unpublished or deleted is skipped rather than listed as a dead
    /// link, and the filter counts above the list still come from the whole site.
    /// </para>
    /// </summary>
    public IList<Guid> BrowseNodeKeys { get; set; } = new List<Guid>();

    /// <summary>
    /// Hard ceiling on how many documents are pulled from the index before post-processing. Keeps
    /// boosting and de-duplication bounded on large sites.
    /// </summary>
    public int MaxResults { get; set; } = 500;

    /// <summary>
    /// The property aliases a result's description is taken from, in order of preference - the
    /// first one with anything in it wins.
    /// <para>
    /// This is the line under a result's name when there is no matched sentence to show there
    /// instead: on a page nobody has searched yet, and on a result that matched something other
    /// than its prose. Empty - the default - uses the aliases Umbraco sites normally use, which is
    /// why descriptions appear without this being configured at all. Name your own here when the
    /// site calls them something else.
    /// </para>
    /// </summary>
    public IList<string> DescriptionFields { get; set; } = new List<string>();

    /// <summary>
    /// How much of the description to show, in characters. 0 falls back to
    /// <see cref="HighlightRules.SnippetLength"/>.
    /// <para>
    /// Its own setting rather than borrowing the snippet length, because the two are not the same
    /// measure. A snippet is the sentence a word was found in and wants to be tight; a description
    /// is what a result says for itself when nothing matched its prose - on a browse page, that is
    /// every result - and a couple of lines there is worth having.
    /// </para>
    /// </summary>
    public int DescriptionLength { get; set; } = 320;

    /// <summary>
    /// Index fields to take a result's heading from, in order of preference. The first one with
    /// anything in it wins; the node's own name is used when none of them do.
    /// <para>
    /// A result's heading came from the node name and nothing else, which is right for most sites
    /// and wrong for the ones whose editors set a <c>pageTitle</c> or <c>metaTitle</c> that differs
    /// from the name in the tree - there the search said one thing and the page said another, with
    /// no way to reconcile them short of renaming content. Falling back to the node name means
    /// leaving this empty behaves exactly as before.
    /// </para>
    /// </summary>
    public IList<string> TitleFields { get; set; } = new List<string>();

    /// <summary>
    /// Index fields copied into each result. Empty means the package returns a useful default set
    /// (name, url, content type, dates) plus whatever fields were searched.
    /// </summary>
    public IList<string> ReturnFields { get; set; } = new List<string>();

    /// <summary>
    /// Whether a result says what kind of thing it is, and what it calls it.
    /// </summary>
    public ResultTypeLabelRules TypeLabels { get; set; } = new();

    /// <summary>Adds a content type grouping breakdown to the response.</summary>
    public bool GroupByContentType { get; set; }

    /// <summary>
    /// Master switch for de-duplication. When off, the same page may appear once per index it is
    /// found in. When on, identical pages collapse to one result even without a field rule, and
    /// <see cref="DeduplicateByField"/> removes deeper copies as well.
    /// </summary>
    public bool EnableDeduplication { get; set; } = true;

    /// <summary>
    /// How many facet groups must be active before ANY of them narrows results. 0 (the default)
    /// lets a single filter act alone; 2 would make every selection inert until a second filter
    /// joins it.
    /// </summary>
    public int MinimumActiveFilters { get; set; }

    /// <summary>
    /// Collapses results sharing the same value for this field, keeping the highest scoring one.
    /// Empty keeps only the identity-level de-duplication.
    /// </summary>
    public string DeduplicateByField { get; set; } = string.Empty;

    public HighlightRules Highlight { get; set; } = new();

    /// <summary>
    /// Filter dimensions returned with live counts so the site can render a filter sidebar. Empty
    /// means no faceting, which is the default.
    /// </summary>
    public IList<FacetDefinition> Facets { get; set; } = new List<FacetDefinition>();

    /// <summary>
    /// The visitor-facing sort filter: the choices offered in a "Sort by" dropdown, such as A-Z,
    /// Z-A, price lowest-first or highest-first. When a visitor picks one it overrides the
    /// profile's ranking order for that request; otherwise <see cref="RankingRules.SortBy"/> rules.
    /// </summary>
    public IList<SortOption> SortOptions { get; set; } = new List<SortOption>();
}

/// <summary>
/// Whether a result is labelled with what kind of thing it is, and in what words.
/// <para>
/// The label was decided in C#: shown for media, members and best bets, hidden on every content
/// result, and printed as the raw document type alias when shown. A site whose results are a mix
/// of pages, news articles and downloads had no way to say so, and a site that wanted the label
/// got <c>umbracoMediaVideo</c> rather than "Video". Both halves are settings now, and the
/// defaults reproduce the previous behaviour exactly.
/// </para>
/// </summary>
public sealed class ResultTypeLabelRules
{
    /// <summary>
    /// Show the label on content results too, not only on media, members and best bets.
    /// </summary>
    public bool ShowOnContent { get; set; }

    /// <summary>
    /// Hide the label everywhere, including on media and members.
    /// </summary>
    public bool Hidden { get; set; }

    /// <summary>
    /// What to call each document or media type, keyed by its alias - <c>newsArticle</c> to
    /// "News". An alias with no entry is labelled with its alias, as it always was.
    /// </summary>
    public IDictionary<string, string> Labels { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether an editor has given this type wording of its own.
    /// <para>
    /// Naming a type is taken as saying it should be shown. The wording is set beside the tick that
    /// includes the type in search, where "Shown on the frontend as: Page" plainly means the word
    /// Page appears on the frontend - and it would not have, because the label is hidden on content
    /// results unless <see cref="ShowOnContent"/> says otherwise. A field that quietly does nothing
    /// until a switch on another tab is found is worse than no field.
    /// </para>
    /// </summary>
    public bool HasLabel(string? alias, string? indexType)
        => TryFindLabel(KeyFor(alias, indexType), out _);

    /// <summary>
    /// The label for one alias: the configured wording, or the alias itself when none is set.
    /// </summary>
    public string Resolve(string? alias, string? indexType)
    {
        var key = KeyFor(alias, indexType);

        if (key.Length == 0)
        {
            return string.Empty;
        }

        return TryFindLabel(key, out var label) ? label : key;
    }

    /// <summary>The alias a label is keyed by, falling back to the index type.</summary>
    private static string KeyFor(string? alias, string? indexType)
    {
        var key = (alias ?? string.Empty).Trim();

        return key.Length == 0 ? (indexType ?? string.Empty).Trim() : key;
    }

    /// <summary>The wording set for this key, if any.</summary>
    private bool TryFindLabel(string key, out string label)
    {
        label = string.Empty;

        if (key.Length == 0)
        {
            return false;
        }

        if (Labels.TryGetValue(key, out var exact) && !string.IsNullOrWhiteSpace(exact))
        {
            label = exact.Trim();
            return true;
        }

        // Deserialising the profile rebuilds this dictionary with the default comparer, so the
        // case-insensitive lookup the property declares does not survive a round trip. An alias
        // typed in one case and indexed in another would otherwise silently lose its wording.
        foreach (KeyValuePair<string, string> candidate in Labels)
        {
            if (string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(candidate.Value))
            {
                label = candidate.Value.Trim();
                return true;
            }
        }

        return false;
    }
}
