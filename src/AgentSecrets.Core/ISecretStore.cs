namespace AgentSecrets;

/// <summary>
/// Persistent storage for named secrets. Names passed in are already normalized
/// (see <see cref="SecretName"/>). Implementations must never include secret values
/// in exception messages or logs.
/// </summary>
public interface ISecretStore
{
    /// <summary>Human-readable backend name, used in diagnostics.</summary>
    string BackendName { get; }

    /// <summary>Creates or overwrites a secret.</summary>
    void Set(string name, string value);

    /// <summary>Returns the secret value, or null when it does not exist.</summary>
    string? Get(string name);

    /// <summary>Checks existence without materializing the secret value.</summary>
    bool Exists(string name);

    /// <summary>Removes a secret. Returns false when it did not exist.</summary>
    bool Remove(string name);

    /// <summary>Lists secret names (never values), sorted.</summary>
    IReadOnlyList<string> List();
}

/// <summary>A storage failure. The message is safe to show: it never contains secret values.</summary>
public sealed class SecretStoreException(string message) : Exception(message);
