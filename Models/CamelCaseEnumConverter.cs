using System.Text.Json.Serialization;

namespace Imobisoft.Search.Models;

/// <summary>
/// Serialises an enum as a camelCase string.
/// <para>
/// Applied directly to the package's enums rather than left to the host's JSON options, because
/// those differ between Umbraco's Management API, a consumer's own controllers and the package's
/// own database serialiser. Pinning it to the type keeps one contract everywhere: what the
/// dashboard posts, what the API returns and what the profile row stores are all the same strings.
/// </para>
/// </summary>
public sealed class CamelCaseEnumConverter<TEnum> : JsonStringEnumConverter<TEnum>
    where TEnum : struct, Enum
{
    public CamelCaseEnumConverter()
        : base(System.Text.Json.JsonNamingPolicy.CamelCase)
    {
    }
}
