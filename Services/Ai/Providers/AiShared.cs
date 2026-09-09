using System.Security.Cryptography;
using System.Text;
using Imobisoft.Search.Models;

namespace Imobisoft.Search.Services.Ai.Providers;

/// <summary>The deadline a model call is held to, shared so every provider honours the same setting.</summary>
internal static class AiTimeouts
{
    /// <summary>
    /// Clamped rather than trusted: a search page is somewhere a visitor waits, so neither a zero
    /// nor a five-minute value saved by accident should reach the wire.
    /// </summary>
    public static TimeSpan For(AiSettings settings)
        => TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 2, 120));
}

/// <summary>Short, stable hashes for cache keys and for telling one credential from another.</summary>
internal static class AiHash
{
    /// <summary>
    /// Never the value itself. Used so a credential can be compared across calls without being held
    /// in a field that could end up in a log, a dump or a cache key.
    /// </summary>
    public static string Fingerprint(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24];
}
