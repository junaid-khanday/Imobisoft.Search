using System.Text.Json;
using System.Text.Json.Serialization;

namespace Imobisoft.Search.Persistence;

/// <summary>
/// The single serialiser used for the <c>configJson</c> column and for the management API payloads,
/// so the shape the dashboard posts is byte-for-byte the shape the engine reads back.
/// </summary>
internal static class SearchProfileJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    /// <summary>
    /// Deserialises, falling back to a fresh instance rather than throwing. A profile row with
    /// corrupt or hand-edited JSON degrades to "search everything" instead of taking search down.
    /// </summary>
    public static T DeserializeOrDefault<T>(string? json)
        where T : new()
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new T();
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, Options) ?? new T();
        }
        catch (JsonException)
        {
            return new T();
        }
    }
}
