using System.Text.Json;

namespace Imobisoft.Search.Services.Ai;

/// <summary>
/// Pulls a JSON object out of a model's reply.
/// <para>
/// A model asked for JSON and nothing else will still, often enough to matter, wrap it in a
/// ```json fence or introduce it with a sentence. The pipeline stages cannot fall back to reading
/// prose - they need a term, a list of ids, an ordering - so the object is found in whatever came
/// back rather than the whole reply being thrown away for having a preamble.
/// </para>
/// <para>
/// Everything here returns null rather than throwing. A stage that cannot read the reply carries on
/// without it, which is the same thing it does when the provider is slow or down.
/// </para>
/// </summary>
internal static class AiJson
{
    private static readonly JsonDocumentOptions Options = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// The first JSON object in the text, parsed, or null when there is not one that parses.
    /// <para>
    /// Caller owns the document and must dispose it.
    /// </para>
    /// </summary>
    public static JsonDocument? Object(string? text)
    {
        var json = Extract(text);

        if (json is null)
        {
            return null;
        }

        try
        {
            JsonDocument document = JsonDocument.Parse(json, Options);

            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                return document;
            }

            document.Dispose();

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// A string property, trimmed, or null when it is absent, empty, or not a string. Also null for
    /// a value longer than <paramref name="maximumLength"/>, because every string read here lands
    /// in a query or a cache key and none of them has a use for an essay.
    /// </summary>
    public static string? String(JsonElement parent, string name, int maximumLength)
    {
        if (!parent.TryGetProperty(name, out JsonElement value)
            || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = (value.GetString() ?? string.Empty).Trim();

        return text.Length == 0 || text.Length > maximumLength ? null : text;
    }

    /// <summary>
    /// A property holding an array of strings, trimmed and de-duplicated without case, capped at
    /// <paramref name="maximum"/> entries and <paramref name="maximumLength"/> characters each.
    /// Empty when the property is missing or is not an array.
    /// </summary>
    public static List<string> Strings(
        JsonElement parent,
        string name,
        int maximum,
        int maximumLength)
    {
        var values = new List<string>();

        if (!parent.TryGetProperty(name, out JsonElement array)
            || array.ValueKind != JsonValueKind.Array)
        {
            return values;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (JsonElement item in array.EnumerateArray())
        {
            if (values.Count >= maximum)
            {
                break;
            }

            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var text = (item.GetString() ?? string.Empty).Trim();

            if (text.Length == 0 || text.Length > maximumLength || !seen.Add(text))
            {
                continue;
            }

            values.Add(text);
        }

        return values;
    }

    /// <summary>
    /// An integer property, however the model wrote it - a number, or a number inside a string.
    /// Null when it is neither.
    /// </summary>
    public static int? Int(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    /// <summary>
    /// The outermost JSON object in the text, as a string, by matching braces.
    /// <para>
    /// Braces are counted rather than the first and last being taken, so a reply holding an object
    /// followed by a sentence does not swallow the sentence. Braces inside strings are skipped, so
    /// a term containing one does not end the object early.
    /// </para>
    /// </summary>
    private static string? Extract(string? text)
    {
        var value = (text ?? string.Empty).Trim();

        var start = value.IndexOf('{');

        if (start < 0)
        {
            return null;
        }

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < value.Length; i++)
        {
            var c = value[i];

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (c == '\\' && inString)
            {
                escaped = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;

                if (depth == 0)
                {
                    return value[start..(i + 1)];
                }
            }
        }

        return null;
    }
}
