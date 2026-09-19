namespace AgentSecrets.Tests;

public class ManifestTests
{
    private const string Valid = """
        {
          "version": 1,
          "bindings": {
            "OPENAI_API_KEY": "openai",
            "ELEVENLABS_API_KEY": "ElevenLabs"
          }
        }
        """;

    [Fact]
    public void Parses_bindings_and_normalizes_aliases()
    {
        Manifest manifest = Manifest.Parse(Valid, "test.json");

        Assert.Equal(2, manifest.Bindings.Count);
        Assert.Equal("openai", manifest.Bindings["OPENAI_API_KEY"]);
        Assert.Equal("elevenlabs", manifest.Bindings["ELEVENLABS_API_KEY"]);
    }

    [Fact]
    public void Allows_comments_trailing_commas_and_schema()
    {
        Manifest manifest = Manifest.Parse("""
            {
              // names only, never values
              "$schema": "https://example.invalid/schema.json",
              "version": 1,
              "bindings": { "OPENAI_API_KEY": "openai", },
            }
            """, "test.json");

        Assert.Single(manifest.Bindings);
    }

    [Theory]
    [InlineData("""{ "bindings": {} }""", "missing \"version\"")]
    [InlineData("""{ "version": 2, "bindings": {} }""", "unsupported \"version\"")]
    [InlineData("""{ "version": "1", "bindings": {} }""", "unsupported \"version\"")]
    [InlineData("""{ "version": 1 }""", "missing \"bindings\"")]
    [InlineData("""{ "version": 1, "bindings": [] }""", "missing \"bindings\"")]
    [InlineData("""{ "version": 1, "binding": {} }""", "unknown property \"binding\"")]
    [InlineData("""{ "version": 1, "bindings": { "1BAD": "openai" } }""", "not a valid environment variable name")]
    [InlineData("""{ "version": 1, "bindings": { "OPENAI_API_KEY": 5 } }""", "must be a secret name string")]
    [InlineData("""{ "version": 1, "bindings": { "KEY": "a", "key": "b" } }""", "bound more than once")]
    [InlineData("""[]""", "top level must be a JSON object")]
    [InlineData("""{ "version": 1, """, "invalid JSON at line 1")]
    public void Rejects_invalid_manifests_with_useful_messages(string json, string expectedMessagePart)
    {
        var ex = Assert.Throws<ManifestException>(() => Manifest.Parse(json, "test.json"));

        Assert.Contains(expectedMessagePart, ex.Message);
        Assert.Contains("test.json", ex.Message);
    }

    [Fact]
    public void A_secret_value_pasted_into_the_manifest_is_rejected_and_not_echoed()
    {
        string pasted = Fake.Secret() + "/not+a+name==";

        var ex = Assert.Throws<ManifestException>(() =>
            Manifest.Parse($$"""{ "version": 1, "bindings": { "OPENAI_API_KEY": "{{pasted}}" } }""", "test.json"));

        Assert.Contains("never secret values", ex.Message);
        Assert.DoesNotContain(pasted, ex.Message);
    }

    [Fact]
    public void Locate_finds_manifest_in_start_directory()
    {
        using var temp = new TempTree();
        temp.File(".agentsecrets.json", Valid);

        Assert.Equal(Path.Combine(temp.Root, ".agentsecrets.json"), Manifest.Locate(temp.Root));
    }

    [Fact]
    public void Locate_walks_up_to_the_git_root_and_prefers_the_nearest_manifest()
    {
        using var temp = new TempTree();
        temp.Directory("repo/.git");
        temp.File("repo/.agentsecrets.json", Valid);
        string deep = temp.Directory("repo/src/app");

        Assert.Equal(Path.Combine(temp.Root, "repo", ".agentsecrets.json"), Manifest.Locate(deep));

        temp.File("repo/src/.agentsecrets.json", Valid);
        Assert.Equal(Path.Combine(temp.Root, "repo", "src", ".agentsecrets.json"), Manifest.Locate(deep));
    }

    [Fact]
    public void Locate_does_not_look_above_the_git_root()
    {
        using var temp = new TempTree();
        temp.File(".agentsecrets.json", Valid);
        temp.Directory("repo/.git");
        string inside = temp.Directory("repo/src");

        Assert.Null(Manifest.Locate(inside));
    }

    [Fact]
    public void Locate_outside_a_git_repository_checks_only_the_start_directory()
    {
        using var temp = new TempTree();
        temp.File(".agentsecrets.json", Valid);
        string child = temp.Directory("child");

        // Guard: this test is only meaningful when the temp directory is not inside a Git repository.
        for (DirectoryInfo? dir = new(temp.Root); dir is not null; dir = dir.Parent)
        {
            Assert.False(System.IO.Directory.Exists(Path.Combine(dir.FullName, ".git")));
        }

        Assert.Null(Manifest.Locate(child));
    }

    private sealed class TempTree : IDisposable
    {
        public string Root { get; } = System.IO.Directory.CreateTempSubdirectory("agentsecrets-manifest-").FullName;

        public string Directory(string relative) =>
            System.IO.Directory.CreateDirectory(Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar))).FullName;

        public void File(string relative, string content)
        {
            string path = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, content);
        }

        public void Dispose() => System.IO.Directory.Delete(Root, recursive: true);
    }
}
