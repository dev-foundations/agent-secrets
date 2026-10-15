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

    [PlatformFact(OS.Windows)]
    public void Merge_lets_explicit_bindings_win_case_insensitively_on_windows()
    {
        var manifest = Bindings.Create();
        manifest["OPENAI_API_KEY"] = "openai";
        manifest["ELEVENLABS_API_KEY"] = "elevenlabs";

        var merged = Bindings.Merge(manifest, [new("openai_api_key", "openai-work")]);

        Assert.Equal(2, merged.Count);
        Assert.Equal("openai-work", merged["OPENAI_API_KEY"]);
        Assert.Equal("elevenlabs", merged["ELEVENLABS_API_KEY"]);
    }

    [PlatformFact(OS.Unix)]
    public void Merge_treats_differently_cased_names_as_different_variables_on_unix()
    {
        var manifest = Bindings.Create();
        manifest["OPENAI_API_KEY"] = "openai";

        var merged = Bindings.Merge(manifest, [new("openai_api_key", "openai-work"), new("OPENAI_API_KEY", "openai-team")]);

        Assert.Equal(2, merged.Count);
        Assert.Equal("openai-team", merged["OPENAI_API_KEY"]);
        Assert.Equal("openai-work", merged["openai_api_key"]);
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

    [PlatformFact(OS.Windows)]
    public void Resolver_finds_executables_on_path_using_pathext()
    {
        using var temp = new TempDir();
        string tool = temp.Touch("mytool.cmd");
        temp.Touch("mytool.txt");

        Assert.Equal(tool, CommandResolver.Resolve("mytool", temp.Path, pathVariable: temp.Path, pathExtVariable: ".COM;.EXE;.BAT;.CMD"), ignoreCase: true);
        Assert.Equal(tool, CommandResolver.Resolve("mytool.cmd", temp.Path, pathVariable: temp.Path), ignoreCase: true);
    }

    [PlatformFact(OS.Windows)]
    public void Resolver_does_not_search_the_current_directory_for_bare_names()
    {
        using var temp = new TempDir();
        string local = temp.Touch("python.cmd");

        var ex = Assert.Throws<CommandResolutionException>(() => CommandResolver.Resolve("python", temp.Path, pathVariable: @"C:\agentsecrets-nonexistent"));
        Assert.True(ex.NotFound);

        // An explicit relative path is fine.
        Assert.Equal(local, CommandResolver.Resolve(@".\python", temp.Path, pathVariable: ""), ignoreCase: true);
    }

    [PlatformFact(OS.Windows)]
    public void Resolver_rejects_scripts_that_need_an_interpreter()
    {
        using var temp = new TempDir();
        temp.Touch("script.py");

        var ex = Assert.Throws<CommandResolutionException>(() => CommandResolver.Resolve(@".\script.py", temp.Path, pathVariable: ""));

        Assert.False(ex.NotFound);
        Assert.Contains("interpreter", ex.Message);
    }

    [PlatformFact(OS.Unix)]
    public void Unix_resolver_finds_executables_on_a_colon_separated_path()
    {
        using var first = new TempDir();
        using var second = new TempDir();
        first.Touch("tool"); // Not executable: skipped, as a shell does.
        string tool = second.TouchExecutable("tool");

        Assert.Equal(tool, CommandResolver.Resolve("tool", "/", pathVariable: $"{first.Path}:{second.Path}"));
    }

    [PlatformFact(OS.Unix)]
    public void Unix_resolver_accepts_scripts_and_symbolic_links_with_an_execute_bit()
    {
        using var temp = new TempDir();
        string script = temp.TouchExecutable("npm-cli.js"); // npm is a symbolic link to a "#!" script
        string link = System.IO.Path.Combine(temp.Path, "npm");
        File.CreateSymbolicLink(link, script);

        Assert.Equal(link, CommandResolver.Resolve("npm", "/", pathVariable: temp.Path));
        Assert.Equal(script, CommandResolver.Resolve("./npm-cli.js", temp.Path, pathVariable: ""));
    }

    [PlatformFact(OS.Unix)]
    public void Unix_resolver_does_not_search_the_current_directory_for_bare_names()
    {
        using var temp = new TempDir();
        string local = temp.TouchExecutable("python");

        Assert.True(Assert.Throws<CommandResolutionException>(() => CommandResolver.Resolve("python", temp.Path, pathVariable: "/agentsecrets-nonexistent")).NotFound);

        // Empty and relative PATH entries mean the current directory to a shell; they are ignored.
        Assert.True(Assert.Throws<CommandResolutionException>(() => CommandResolver.Resolve("python", temp.Path, pathVariable: ":.:bin")).NotFound);

        // So are '\' and ':', which are ordinary characters in a Unix file name, not directory separators.
        temp.TouchExecutable(@"a\b");
        Assert.True(Assert.Throws<CommandResolutionException>(() => CommandResolver.Resolve(@"a\b", temp.Path, pathVariable: "")).NotFound);

        // An explicit relative path is fine.
        Assert.Equal(local, CommandResolver.Resolve("./python", temp.Path, pathVariable: ""));
    }

    [PlatformFact(OS.Unix)]
    public void Unix_resolver_rejects_files_without_an_execute_bit_and_directories()
    {
        using var temp = new TempDir();
        temp.Touch("script.py");
        Directory.CreateDirectory(System.IO.Path.Combine(temp.Path, "tools"));

        var ex = Assert.Throws<CommandResolutionException>(() => CommandResolver.Resolve("./script.py", temp.Path, pathVariable: ""));
        Assert.False(ex.NotFound);
        Assert.Contains("chmod +x", ex.Message);

        Assert.True(Assert.Throws<CommandResolutionException>(() => CommandResolver.Resolve("./tools", temp.Path, pathVariable: "")).NotFound);
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

    [PlatformFact(OS.Windows)]
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

    [PlatformFact(OS.Unix)]
    public void Start_info_on_unix_passes_arguments_as_a_list_even_for_batch_file_names()
    {
        var env = new Dictionary<string, string> { ["DEMO_KEY"] = "x" };

        var startInfo = ChildProcess.CreateStartInfo("/opt/tools/npm.cmd", ["start", "a b"], env, "/");

        Assert.Equal("/opt/tools/npm.cmd", startInfo.FileName);
        Assert.Equal(["start", "a b"], startInfo.ArgumentList);
        Assert.Equal("x", startInfo.Environment["DEMO_KEY"]);
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

        public string TouchExecutable(string name)
        {
            string path = Touch(name);
            TestFiles.MakeExecutable(path);
            return path;
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
