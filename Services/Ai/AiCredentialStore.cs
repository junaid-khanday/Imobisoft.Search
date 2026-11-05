using Imobisoft.Search.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Services;

namespace Imobisoft.Search.Services.Ai;

/// <summary>What the backoffice is allowed to know about the stored key.</summary>
public sealed class AiCredentialStatus
{
    public bool HasKey { get; set; }

    /// <summary>Last four characters, so an editor can tell which key is in place without seeing it.</summary>
    public string Hint { get; set; } = string.Empty;

    /// <summary>Provider read from the key's own shape, or null when its shape says nothing.</summary>
    public AiProvider? DetectedProvider { get; set; }

    public string DetectedProviderName { get; set; } = string.Empty;
}

/// <summary>
/// Holds the AI key. Kept deliberately separate from the profile rules: rules are returned to the
/// browser whole, and a key must never be.
/// </summary>
public interface IAiCredentialStore
{
    /// <summary>Saves the key, replacing any existing one.</summary>
    void Save(string apiKey);

    /// <summary>Removes the key, which also turns the text tools off.</summary>
    void Clear();

    /// <summary>The key, for server-side use only. Null when none is stored or it cannot be read back.</summary>
    string? Read();

    /// <summary>What the backoffice may display: whether a key exists, and its last four characters.</summary>
    AiCredentialStatus Describe();
}

/// <inheritdoc />
public sealed class AiCredentialStore : IAiCredentialStore
{
    /// <summary>
    /// Purpose string for the data protector. Changing it makes every stored key unreadable, so it
    /// is fixed here rather than derived from anything.
    /// </summary>
    private const string ProtectorPurpose = "Imobisoft.Search.Ai.ApiKey";

    private readonly IKeyValueService _keyValueService;
    private readonly IDataProtector _protector;
    private readonly ILogger<AiCredentialStore> _logger;

    public AiCredentialStore(
        IKeyValueService keyValueService,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<AiCredentialStore> logger)
    {
        _keyValueService = keyValueService;
        _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
        _logger = logger;
    }

    /// <inheritdoc />
    public void Save(string apiKey)
    {
        var key = (apiKey ?? string.Empty).Trim();

        if (string.IsNullOrEmpty(key))
        {
            Clear();
            return;
        }

        // Encrypted at rest with the site's own data-protection keys, so a copy of the database is
        // not a copy of the key. The last four characters are stored in clear next to it: that is
        // all the backoffice ever shows, and it saves decrypting just to render the field.
        _keyValueService.SetValue(ImobisoftSearchConstants.Ai.KeyValueKey, _protector.Protect(key));
        _keyValueService.SetValue(ImobisoftSearchConstants.Ai.HintKeyValueKey, Hint(key));
    }

    /// <inheritdoc />
    public void Clear()
    {
        _keyValueService.SetValue(ImobisoftSearchConstants.Ai.KeyValueKey, string.Empty);
        _keyValueService.SetValue(ImobisoftSearchConstants.Ai.HintKeyValueKey, string.Empty);
    }

    /// <inheritdoc />
    public string? Read()
    {
        var stored = _keyValueService.GetValue(ImobisoftSearchConstants.Ai.KeyValueKey);

        if (string.IsNullOrWhiteSpace(stored))
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(stored);
        }
        catch (Exception ex)
        {
            // Data-protection keys are per-site. A restored database, a rotated key ring or a move
            // between environments leaves the stored value undecryptable - which is a prompt to
            // re-enter the key, not an error worth taking search down for.
            _logger.LogWarning(
                ex,
                "Imobisoft.Search could not decrypt the stored AI key. It was most likely saved on "
                + "another site or before the data-protection keys were rotated; re-enter it in the "
                + "Search section to fix this.");

            return null;
        }
    }

    /// <inheritdoc />
    public AiCredentialStatus Describe()
    {
        var hint = _keyValueService.GetValue(ImobisoftSearchConstants.Ai.HintKeyValueKey) ?? string.Empty;
        var hasKey = !string.IsNullOrWhiteSpace(_keyValueService.GetValue(ImobisoftSearchConstants.Ai.KeyValueKey));

        AiProvider? detected = hasKey ? AiProviderCatalog.Detect(Read()) : null;

        return new AiCredentialStatus
        {
            HasKey = hasKey,
            Hint = hint,
            DetectedProvider = detected,
            DetectedProviderName = detected is null ? string.Empty : AiProviderCatalog.For(detected.Value).DisplayName,
        };
    }

    private static string Hint(string key)
        => key.Length <= 4 ? new string('•', key.Length) : "••••" + key[^4..];
}
