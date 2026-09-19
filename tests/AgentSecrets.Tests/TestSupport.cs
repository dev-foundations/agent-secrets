using System.Security.Cryptography;
using System.Text;
using AgentSecrets.Cli;

namespace AgentSecrets.Tests;

/// <summary>The fake backend used by most tests. Counts value reads so tests can assert "never retrieved".</summary>
public sealed class InMemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _secrets = new(StringComparer.OrdinalIgnoreCase);

    public int GetCalls { get; private set; }

    public string BackendName => "In-memory test store";

    public void Set(string name, string value) => _secrets[name] = value;

    public string? Get(string name)
    {
        GetCalls++;
        return _secrets.GetValueOrDefault(name);
    }

    public bool Exists(string name) => _secrets.ContainsKey(name);

    public bool Remove(string name) => _secrets.Remove(name);

    public IReadOnlyList<string> List() => _secrets.Keys.Order(StringComparer.Ordinal).ToList();
}

/// <summary>Runs the CLI in-process against a fake store and captures its output.</summary>
public sealed class CliHarness : IDisposable
{
    public InMemorySecretStore Store { get; } = new();
    public StringWriter Out { get; } = new();
    public StringWriter Error { get; } = new();
    public string WorkingDirectory { get; } = Directory.CreateTempSubdirectory("agentsecrets-test-").FullName;

    public string AllOutput => Out.ToString() + Error.ToString();

    public int Run(params string[] args) => Run(args, stdin: null, typedSecret: null);

    /// <param name="stdin">Non-null simulates redirected (non-interactive) stdin.</param>
    /// <param name="typedSecret">What the "user" types at the hidden prompt; confirmations come from <paramref name="answers"/>.</param>
    public int Run(string[] args, string? stdin, string? typedSecret, string answers = "")
    {
        var context = new CliContext
        {
            Store = Store,
            Out = Out,
            Error = Error,
            In = new StringReader(stdin ?? answers),
            InputRedirected = stdin is not null,
            ReadSecretInteractive = () => typedSecret ?? throw new InvalidOperationException("No interactive secret expected."),
            WorkingDirectory = WorkingDirectory,
        };
        return CliApp.Run(args, context);
    }

    public void WriteManifest(string json, string? directory = null) =>
        File.WriteAllText(Path.Combine(directory ?? WorkingDirectory, Manifest.FileName), json);

    public void Dispose()
    {
        try
        {
            Directory.Delete(WorkingDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

public static class TestPaths
{
    public static string Probe => Path.Combine(AppContext.BaseDirectory, "test-probe.exe");

    public static string Cli => Path.Combine(AppContext.BaseDirectory, "agent-secrets.exe");
}

public static class Fake
{
    /// <summary>An obviously fake credential, unique per call. Never a real key format.</summary>
    public static string Secret() => $"FAKE-TEST-VALUE-{Guid.NewGuid():N}";

    public static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
