using Imobisoft.Search.Models;

namespace Imobisoft.Search.Services.Ai;

/// <summary>What a connection test found, so the backoffice can say more than "it failed".</summary>
public sealed class AiConnectionResult
{
    public bool Success { get; set; }

    /// <summary>Human-readable outcome, shown verbatim in the dashboard.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>The model that answered, echoed back so a typo in the model id is obvious.</summary>
    public string? Model { get; set; }

    public long ElapsedMilliseconds { get; set; }
}

/// <summary>
/// The AI half of the search: understanding the question, reordering what came back, and answering
/// from it.
/// <para>
/// Every method here is best effort by contract. A disabled add-on, a missing key, a rate ceiling,
/// a timeout, a refusal or an outright error all return null rather than throwing - a visitor must
/// get their results whether or not the model was reachable, so no caller ever has to guard a
/// search behind a try/catch.
/// </para>
/// </summary>
public interface IAiSearchService
{
    /// <summary>True when a key and model are configured and the add-on is switched on.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Extra search terms drawn out of a natural-language question, or null when the pass is off,
    /// the term is already keyword-shaped, or the model could not help.
    /// <para>
    /// The visitor's own words are never replaced - these are added to them, so a rewrite that
    /// misreads the question still finds everything a plain keyword search would have.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<string>?> ExpandQueryAsync(string term, CancellationToken cancellationToken = default);

    /// <summary>
    /// The candidates reordered by how well each answers the question, or null to keep the original
    /// order. The returned list is a permutation of what went in - nothing is added or dropped.
    /// </summary>
    Task<IReadOnlyList<SearchResultItem>?> RerankAsync(
        string term,
        IReadOnlyList<SearchResultItem> candidates,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A short answer written from <paramref name="results"/> and nothing else, or null when no
    /// answer could be produced.
    /// </summary>
    Task<AiAnswer?> AnswerAsync(
        string term,
        IReadOnlyList<SearchResultItem> results,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends one trivial request to check a key and model actually work. Used by the dashboard's
    /// "Test connection" button so an editor finds out at configuration time rather than from a
    /// silently AI-less search page.
    /// </summary>
    /// <param name="apiKey">
    /// Key to test. Null or empty tests the saved one, which is how the panel verifies a key it can
    /// no longer read back.
    /// </param>
    /// <param name="model">Model to test. Null or empty tests the saved one.</param>
    /// <param name="cancellationToken">Abandons the test if the editor navigates away.</param>
    Task<AiConnectionResult> TestConnectionAsync(
        string? apiKey = null,
        string? model = null,
        CancellationToken cancellationToken = default);
}
