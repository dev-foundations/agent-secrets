namespace AgentSecrets.Tests;

public class CommandLineTests
{
    [Fact]
    public void Bindings_parse_and_validate()
    {
        Assert.Equal(("OPENAI_API_KEY", "openai"), Bindings.Parse("OPENAI_API_KEY=OpenAI"));
        Assert.Throws<ArgumentException>(() => Bindings.Parse("OPENAI_API_KEY"));
        Assert.Throws<ArgumentException>(() => Bindings.Parse("=openai"));
        Assert.Throws<ArgumentException>(() => Bindings.Parse("OPENAI_API_KEY="));
        Assert.Throws<ArgumentException>(() => Bindings.Parse("9KEY=openai"));
        Assert.Throws<ArgumentException>(() => Bindings.Parse("KEY=bad name"));
    }

    [Fact]
    public void Merge_lets_explicit_bindings_win_case_insensitively()
    {
        var manifest = Bindings.Create();
        manifest["OPENAI_API_KEY"] = "openai";
        manifest["ELEVENLABS_API_KEY"] = "elevenlabs";

        var merged = Bindings.Merge(manifest, [new("openai_api_key", "openai-work")]);

        Assert.Equal(2, merged.Count);
        Assert.Equal("openai-work", merged["OPENAI_API_KEY"]);
        Assert.Equal("elevenlabs", merged["ELEVENLABS_API_KEY"]);
    }

    [Theory]
    [InlineData("openai", true)]
    [InlineData("azure.prod_2-eu", true)]
    [InlineData("", false)]
    [InlineData("-leading", false)]
    [InlineData("has space", false)]
    [InlineData("slash/name", false)]
    [InlineData("wild*", false)]
    public void Secret_name_validation(string name, bool valid)
    {
        Assert.Equal(valid, SecretName.IsValid(name));
    }

    [Fact]
    public void Resolver_finds_executables_on_path_using_pathext()
    {
        using var temp = new TempDir();
        string tool = temp.Touch("mytool.cmd");
        temp.Touch("mytool.txt");

        Assert.Equal(tool, CommandResolver.Resolve("mytool", temp.Path, pathVariable: temp.Path, pathExtVariable: ".COM;.EXE;.BAT;.CMD"), ignoreCase: true);
        Assert.Equal(tool, CommandResolver.Resolve("mytool.cmd", temp.Path, pathVariable: temp.Path), ignoreCase: true);
    }

    [Fact]
    public void Resolver_does_not_search_the_current_directory_for_bare_names()
    {
        using var temp = new TempDir();
        string local = temp.Touch("python.cmd");

        var ex = Assert.Throws<CommandResolutionException>(() => CommandResolver.Resolve("python", temp.Path, pathVariable: @"C:\agentsecrets-nonexistent"));
        Assert.True(ex.NotFound);

        // An explicit relative path is fine.
        Assert.Equal(local, CommandResolver.Resolve(@".\python", temp.Path, pathVariable: ""), ignoreCase: true);
    }

    [Fact]
    public void Resolver_rejects_scripts_that_need_an_interpreter()
    {
        using var temp = new TempDir();
        temp.Touch("script.py");

        var ex = Assert.Throws<CommandResolutionException>(() => CommandResolver.Resolve(@".\script.py", temp.Path, pathVariable: ""));

        Assert.False(ex.NotFound);
        Assert.Contains("interpreter", ex.Message);
    }

    [Fact]
    public void Batch_command_line_escapes_cmd_metacharacters()
    {
        string line = BatchCommandLine.Build(@"C:\tools\npm.cmd", ["run", "a b", "x&calc", "%PATH%", "say \"hi\"", @"dir\", ""]);

        Assert.Equal(
            "/e:ON /v:OFF /d /c \"\"C:\\tools\\npm.cmd\" run \"a b\" \"x&calc\" \"%%cd:~,%PATH%%cd:~,%\" \"say \"\"hi\"\"\" \"dir\\\\\" \"\"\"",
            line);
    }

    [Fact]
    public void Batch_command_line_rejects_line_breaks()
    {
        Assert.Throws<ArgumentException>(() => BatchCommandLine.Build(@"C:\tools\npm.cmd", ["a\nb"]));
    }

    [Fact]
    public void Start_info_uses_argument_list_for_executables_and_cmd_for_batch_files()
    {
        var env = new Dictionary<string, string> { ["DEMO_KEY"] = "x" };

        var exe = ChildProcess.CreateStartInfo(@"C:\tools\python.exe", ["-c", "print('a b')"], env, @"C:\");
        Assert.Equal(@"C:\tools\python.exe", exe.FileName);
        Assert.Equal(["-c", "print('a b')"], exe.ArgumentList);
        Assert.False(exe.UseShellExecute);
        Assert.Equal("x", exe.Environment["DEMO_KEY"]);

        var batch = ChildProcess.CreateStartInfo(@"C:\tools\npm.cmd", ["start"], env, @"C:\");
        Assert.EndsWith("cmd.exe", batch.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(batch.ArgumentList);
        Assert.Contains("npm.cmd", batch.Arguments);
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("agentsecrets-cmd-").FullName;

        public string Touch(string name)
        {
            string path = System.IO.Path.Combine(Path, name);
            File.WriteAllText(path, "");
            return path;
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
