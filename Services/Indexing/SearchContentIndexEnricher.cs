using Examine;
using Imobisoft.Search.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Infrastructure.Examine;

namespace Imobisoft.Search.Services.Indexing;

/// <summary>
/// Adds a clean body-text field to every content document Umbraco indexes.
/// <para>
/// Block editors store their content as JSON and Umbraco indexes much of it verbatim, so a snippet
/// cut from those fields shows block labels and layout names rather than prose. The words are
/// there, which is why searching works; what is missing is a field worth <em>reading</em>. This adds
/// one - <c>extractedContent</c> - built by <see cref="ExtractedContentBuilder"/> from the same
/// values, with the structure stripped out.
/// </para>
/// <para>
/// It only ever adds. Nothing Umbraco indexed is removed or rewritten, because these are the
/// indexes the backoffice search, the site's own code and any other package all read: a field
/// deleted here to tidy up one search page would go missing from all of them. The noise is left in
/// place and simply not used.
/// </para>
/// <para>
/// Hooked on <c>TransformingIndexValues</c>, which is the event Examine 4 raises while a document
/// is being built - earlier versions called it <c>TransformingIndexEntry</c>. Attaching to the
/// index rather than subclassing it is what lets a package do this at all: the consuming site owns
/// its indexes, and the package is a guest.
/// </para>
/// </summary>
public sealed class SearchContentIndexEnricher
    : INotificationHandler<UmbracoApplicationStartingNotification>
{
    private readonly IExamineManager _examineManager;
    private readonly ImobisoftSearchOptions _options;
    private readonly ILogger<SearchContentIndexEnricher> _logger;

    /// <summary>
    /// Indexes already hooked, so a second start notification - Umbraco raises one per runtime
    /// level - does not attach the handler twice and write the field twice.
    /// </summary>
    private readonly HashSet<string> _attached = new(StringComparer.OrdinalIgnoreCase);

    public SearchContentIndexEnricher(
        IExamineManager examineManager,
        IOptions<ImobisoftSearchOptions> options,
        ILogger<SearchContentIndexEnricher> logger)
    {
        _examineManager = examineManager;
        _options = options.Value;
        _logger = logger;
    }

    public void Handle(UmbracoApplicationStartingNotification notification)
    {
        if (!_options.Enabled || !_options.EnableContentExtraction)
        {
            return;
        }

        foreach (IIndex index in _examineManager.Indexes)
        {
            Attach(index);
        }
    }

    private void Attach(IIndex index)
    {
        // Content only. A media or member index carries no block editors, so there is nothing to
        // extract and nothing to gain from being in the path of every document it writes.
        if (index is not UmbracoContentIndex)
        {
            return;
        }

        lock (_attached)
        {
            if (!_attached.Add(index.Name))
            {
                return;
            }
        }

        index.TransformingIndexValues += OnTransforming;

        _logger.LogInformation(
            "Imobisoft.Search is adding a clean {Field} field to documents in {Index}, so result "
            + "snippets read as prose rather than as block layout data.",
            ImobisoftSearchConstants.IndexFields.ExtractedContent,
            index.Name);
    }

    private void OnTransforming(object? sender, IndexingItemEventArgs e)
    {
        try
        {
            if (!e.ValueSet.Category.Equals(IndexTypes.Content, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Dictionary<string, IEnumerable<object>> values = e.ValueSet.Values
                .ToDictionary(x => x.Key, x => (IEnumerable<object>)x.Value, StringComparer.Ordinal);

            var added = 0;

            // One field per culture the document actually carries, plus an invariant one. A variant
            // site needs extractedContent_en-gb to sit beside bodyText_en-gb, or a culture-scoped
            // search would find the words and have nothing readable to show for them.
            foreach (var culture in Cultures(values.Keys))
            {
                var text = ExtractedContentBuilder.Build(
                    values,
                    culture,
                    MinimumWords(),
                    MaximumCharacters());

                if (text.Length == 0)
                {
                    continue;
                }

                var field = culture is null
                    ? ImobisoftSearchConstants.IndexFields.ExtractedContent
                    : ImobisoftSearchConstants.IndexFields.ExtractedContent + "_" + culture;

                values[field] = new object[] { text };
                added++;
            }

            if (added > 0)
            {
                e.SetValues(values);
            }
        }
        catch (Exception ex)
        {
            // An index entry that cannot be enriched must still be indexed. Throwing here would
            // take the document out of the index altogether - the page would stop being findable
            // at all, which is far worse than a snippet that reads badly.
            _logger.LogWarning(
                ex,
                "Imobisoft.Search could not build {Field} for document {Id}; it is indexed without it.",
                ImobisoftSearchConstants.IndexFields.ExtractedContent,
                e.ValueSet.Id);
        }
    }

    /// <summary>
    /// The cultures this document has fields for, plus null for the invariant pass.
    /// <para>
    /// Read from the field names rather than from the language service, because what matters is
    /// what this document actually carries: a site with four languages and a page translated into
    /// one should get one culture field, not four, three of which would repeat the invariant text.
    /// </para>
    /// </summary>
    private static IEnumerable<string?> Cultures(IEnumerable<string> fieldNames)
    {
        var cultures = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in fieldNames)
        {
            if (name.StartsWith("__", StringComparison.Ordinal))
            {
                continue;
            }

            var underscore = name.LastIndexOf('_');

            if (underscore <= 0 || underscore == name.Length - 1)
            {
                continue;
            }

            var suffix = name[(underscore + 1)..];

            if (ExtractedContentBuilder.LooksLikeCulture(suffix))
            {
                cultures.Add(suffix.ToLowerInvariant());
            }
        }

        // Invariant first, so an invariant-only site gets exactly one field and the common case
        // costs one pass.
        yield return null;

        foreach (var culture in cultures)
        {
            yield return culture;
        }
    }

    private int MinimumWords()
        => Math.Clamp(
            _options.ContentExtractionMinimumWords <= 0
                ? ExtractedContentBuilder.DefaultMinimumWords
                : _options.ContentExtractionMinimumWords,
            1,
            100);

    private int MaximumCharacters()
        => Math.Clamp(
            _options.ContentExtractionMaxCharacters <= 0
                ? ExtractedContentBuilder.DefaultMaximumCharacters
                : _options.ContentExtractionMaxCharacters,
            200,
            100_000);
}
