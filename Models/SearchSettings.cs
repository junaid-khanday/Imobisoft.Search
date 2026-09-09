namespace Imobisoft.Search.Models;

/// <summary>How much of what visitors search for is recorded.</summary>
public sealed class AnalyticsSettings
{
    /// <summary>Recording is on by default; turning it off stops all writes immediately.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Queries older than this are deleted by a nightly job. 0 keeps them forever.</summary>
    public int RetentionDays { get; set; } = 90;

    /// <summary>
    /// Records only the searches that found nothing. Useful on a busy site where the interesting
    /// signal is the failures rather than the volume.
    /// </summary>
    public bool RecordZeroResultsOnly { get; set; }

    /// <summary>Ignores very short terms, which are usually mistypes rather than intent.</summary>
    public int MinimumTermLength { get; set; } = 2;

    /// <summary>
    /// Records which result a visitor opened, so the reports can show which searches are actually
    /// answering the question. Requires the site to call <c>RecordClickAsync</c>.
    /// </summary>
    public bool TrackClicks { get; set; } = true;
}

/// <summary>Controls "did you mean" and type-ahead.</summary>
public sealed class SuggestionSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// A search returning fewer than this many results gets a spelling suggestion. 0 only suggests
    /// when nothing at all was found.
    /// </summary>
    public int SuggestBelowResultCount { get; set; } = 3;

    /// <summary>How close a word has to be before it is offered as a correction, between 0 and 1.</summary>
    public float Fuzziness { get; set; } = 0.65f;

    /// <summary>
    /// The largest number of single-character edits between the typed word and a suggestion. Keeps
    /// "prodct" → "product" while rejecting a wholly different word that happens to score well.
    /// </summary>
    public int MaximumEditDistance { get; set; } = 3;

    /// <summary>Default number of type-ahead suggestions returned.</summary>
    public int AutocompleteSize { get; set; } = 10;
}

/// <summary>
/// Site-wide package settings, as opposed to the per-profile rules.
/// <para>
/// Stored as a single JSON value in Umbraco's key/value table, so turning analytics on or off needs
/// no schema change and no configuration file in the consuming project.
/// </para>
/// </summary>
public sealed class SearchSettings
{
    public AnalyticsSettings Analytics { get; set; } = new();

    public SuggestionSettings Suggestions { get; set; } = new();

    /// <summary>
    /// The AI add-on: the credential and which of the AI features are switched on. Entirely inert
    /// until a key is saved, so an existing site is unaffected by the add-on existing.
    /// </summary>
    public AiSettings Ai { get; set; } = new();
}
