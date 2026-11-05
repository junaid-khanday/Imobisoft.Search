using Examine;
using Examine.Search;
using Microsoft.Extensions.Logging;

namespace Imobisoft.Search.Services.Ai;

/// <summary>Reads a page's text out of the search index, by node key.</summary>
public interface IAiContentReader
{
    /// <summary>
    /// The readable text of one node, or null when no indexed document carries that key.
    /// </summary>
    /// <param name="key">The node's GUID key, as the search results carry it.</param>
    /// <param name="maximumCharacters">Hard ceiling on what is returned, and so on what is sent.</param>
    AiContentText? Read(Guid key, int maximumCharacters);
}

/// <summary>A node's name and body text, as held in the index.</summary>
public sealed class AiContentText
{
    public string Name { get; init; } = string.Empty;

    public string Body { get; init; } = string.Empty;

    public bool HasBody => !string.IsNullOrWhiteSpace(Body);
}

/// <inheritdoc />
public sealed class AiContentReader : IAiContentReader
{
    /// <summary>
    /// Fields worth reading as prose, in the order they are tried. Umbraco's own body fields come
    /// first; anything else that looks like text is picked up by the sweep below.
    /// </summary>
    private static readonly string[] PreferredBodyFields =
    {
        "bodyText", "content", "mainContent", "description", "metaDescription", "summary",
        "introduction", "intro", "abstract", "text",
    };

    private readonly IExamineManager _examineManager;
    private readonly ILogger<AiContentReader> _logger;

    public AiContentReader(IExamineManager examineManager, ILogger<AiContentReader> logger)
    {
        _examineManager = examineManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public AiContentText? Read(Guid key, int maximumCharacters)
    {
        // The node is looked up here rather than taken from the browser on purpose: it means the
        // public endpoint can only ever be asked about content this site has published, so the
        // worst a caller can do with it is summarise the site's own pages.
        var query = $"{ImobisoftSearchConstants.IndexFields.Key}:\"{key:D}\"";

        foreach (IIndex index in _examineManager.Indexes)
        {
            try
            {
                ISearchResults results = index.Searcher
                    .CreateQuery()
                    .NativeQuery(query)
                    .Execute(QueryOptions.SkipTake(0, 1));

                ISearchResult? hit = results.FirstOrDefault();

                if (hit is null)
                {
                    continue;
                }

                return Build(hit, maximumCharacters);
            }
            catch (Exception ex)
            {
                // An index that cannot answer is skipped rather than failing the request - another
                // index may well hold the node.
                _logger.LogDebug(
                    ex,
                    "Imobisoft.Search could not look up {Key} in index {IndexName}.",
                    key,
                    index.Name);
            }
        }

        return null;
    }

    private static AiContentText Build(ISearchResult hit, int maximumCharacters)
    {
        var name = Read(hit, ImobisoftSearchConstants.IndexFields.NodeName)
                   ?? Read(hit, ImobisoftSearchConstants.IndexFields.SystemNodeName)
                   ?? string.Empty;

        var budget = Math.Max(200, maximumCharacters);
        var collected = new List<string>();
        var length = 0;

        void Take(string? value)
        {
            if (length >= budget || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var clean = Clean(value);

            if (clean.Length == 0)
            {
                return;
            }

            if (length + clean.Length > budget)
            {
                clean = clean[..(budget - length)];
            }

            collected.Add(clean);
            length += clean.Length;
        }

        foreach (var field in PreferredBodyFields)
        {
            Take(Read(hit, field));
        }

        // Then anything else that reads like prose. Umbraco's bookkeeping fields are skipped, along
        // with short values that are almost always ids, aliases or flags rather than text.
        foreach (KeyValuePair<string, string> value in hit.Values)
        {
            if (length >= budget)
            {
                break;
            }

            if (value.Key.StartsWith("__", StringComparison.Ordinal)
                || ImobisoftSearchConstants.IndexFields.Metadata.Contains(value.Key)
                || PreferredBodyFields.Contains(value.Key, StringComparer.OrdinalIgnoreCase)
                || value.Value.Length < 80)
            {
                continue;
            }

            Take(value.Value);
        }

        return new AiContentText
        {
            Name = Clean(name),
            Body = string.Join("\n\n", collected),
        };
    }

    private static string? Read(ISearchResult result, string field)
        => result.Values.TryGetValue(field, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    /// <summary>
    /// Indexed values keep the markup that was in the property. It is stripped here so the model is
    /// billed for words rather than tags, and so a stray tag cannot look like an instruction.
    /// </summary>
    private static string Clean(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        var insideTag = false;
        var lastWasSpace = false;

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

                // A closing tag is a word boundary: "one</p><p>two" must not become "onetwo".
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

        return System.Net.WebUtility.HtmlDecode(builder.ToString()).Trim();
    }
}
