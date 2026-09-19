using System.Text.Json;

namespace AgentSecrets;

/// <summary>
/// The optional project file ".agentsecrets.json". It maps environment variable
/// names to secret aliases and never contains secret values, so it is safe to commit.
/// </summary>
public sealed class Manifest
{
    public const string FileName = ".agentsecrets.json";
    public const int SupportedVersion = 1;

    private Manifest(string path, Dictionary<string, string> bindings)
    {
        Path = path;
        Bindings = bindings;
    }

    public string Path { get; }

    /// <summary>Environment variable name -> normalized secret alias.</summary>
    public IReadOnlyDictionary<string, string> Bindings { get; }

    /// <summary>
    /// Finds the manifest for <paramref name="startDirectory"/>: that directory first, then each
    /// parent up to and including the Git root (the first directory containing ".git").
    /// Outside a Git repository only the start directory itself is checked.
    /// </summary>
    public static string? Locate(string startDirectory)
    {
        var candidates = new List<string>();
        bool insideGitRepo = false;
        for (DirectoryInfo? dir = new(startDirectory); dir is not null; dir = dir.Parent)
        {
            candidates.Add(dir.FullName);
            string git = System.IO.Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(git) || File.Exists(git))
            {
                insideGitRepo = true;
                break;
            }
        }

        foreach (string directory in insideGitRepo ? candidates : candidates.Take(1))
        {
            string path = System.IO.Path.Combine(directory, FileName);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    public static Manifest Load(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ManifestException(path, $"the file could not be read ({ex.GetType().Name}).");
        }

        return Parse(json, path);
    }

    public static Manifest Parse(string json, string path)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch (JsonException ex)
        {
            throw new ManifestException(path, $"invalid JSON at line {ex.LineNumber + 1}, position {ex.BytePositionInLine + 1}.");
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new ManifestException(path, "the top level must be a JSON object.");
            }

            JsonElement? bindingsElement = null;
            bool hasVersion = false;
            foreach (JsonProperty property in root.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "$schema":
                        break;
                    case "version":
                        if (property.Value.ValueKind != JsonValueKind.Number
                            || !property.Value.TryGetInt32(out int version)
                            || version != SupportedVersion)
                        {
                            throw new ManifestException(path, $"unsupported \"version\". This AgentSecrets supports version {SupportedVersion}.");
                        }

                        hasVersion = true;
                        break;
                    case "bindings":
                        bindingsElement = property.Value;
                        break;
                    default:
                        throw new ManifestException(path, $"unknown property \"{property.Name}\". Allowed: \"version\", \"bindings\".");
                }
            }

            if (!hasVersion)
            {
                throw new ManifestException(path, $"missing \"version\". Add: \"version\": {SupportedVersion}");
            }

            if (bindingsElement is not { ValueKind: JsonValueKind.Object } bindingsObject)
            {
                throw new ManifestException(path, "missing \"bindings\" object, e.g. \"bindings\": { \"OPENAI_API_KEY\": \"openai\" }");
            }

            var bindings = AgentSecrets.Bindings.Create();
            foreach (JsonProperty binding in bindingsObject.EnumerateObject())
            {
                if (!AgentSecrets.Bindings.IsValidEnvName(binding.Name))
                {
                    throw new ManifestException(path, $"\"{binding.Name}\" is not a valid environment variable name.");
                }

                if (binding.Value.ValueKind != JsonValueKind.String)
                {
                    throw new ManifestException(path, $"the value of \"{binding.Name}\" must be a secret name string such as \"openai\" (never the secret itself).");
                }

                // Deliberately do not echo the offending value: someone may have pasted a real key here.
                string? alias = binding.Value.GetString();
                if (!SecretName.IsValid(alias))
                {
                    throw new ManifestException(path, $"the value of \"{binding.Name}\" is not a valid secret name. {SecretName.Rules} The manifest holds secret names only, never secret values.");
                }

                if (!bindings.TryAdd(binding.Name, alias!.ToLowerInvariant()))
                {
                    throw new ManifestException(path, $"\"{binding.Name}\" is bound more than once (environment variable names are case-insensitive on Windows).");
                }
            }

            return new Manifest(path, bindings);
        }
    }
}

public sealed class ManifestException(string path, string problem)
    : Exception($"Invalid manifest '{path}': {problem}");
