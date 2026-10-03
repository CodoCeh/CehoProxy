using System.Text.Json;
using ProxyCage.Cli;

namespace ProxyCage.Core.Tests;

/// <summary>
/// Calls only the in-memory CLI insertion helper against temporary fixture paths.
/// Program/Main, discovery, persistence, listeners and tunnel operations never run.
/// Source guards cover the side-effecting command boundary instead.
/// </summary>
public sealed class CliAppIdentityTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ceho-cli-identity-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Fixture(string directory, string name = "editor")
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, directory)).FullName;
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, "Fixture data only; never executed.");
        return path;
    }

    [Fact]
    public void Selected_executable_identity_is_preserved_separately_from_the_routing_folder()
    {
        var path = Fixture("Editor");
        var cfg = new CehoConfig { Language = "en" };

        Assert.True(Assistant.TryAddApp(cfg, path, out var detected, out var error));

        var app = Assert.Single(cfg.Apps);
        Assert.Null(error);
        Assert.NotNull(detected);
        Assert.Equal(AppIdentity.Normalize(path), app.IdentityPath);
        Assert.Equal(app.IdentityPath, app.Launch);
        Assert.Equal(detected.Folder, app.Folder);
        Assert.NotEqual(app.IdentityPath, app.Folder);
    }

    [Fact]
    public void Normalized_repeat_does_not_mutate_an_existing_disabled_app()
    {
        var path = Fixture("Editor");
        var cfg = new CehoConfig { Language = "en" };
        Assert.True(Assistant.TryAddApp(cfg, path, out _, out _));
        var app = Assert.Single(cfg.Apps);
        app.Enabled = false;
        app.DisplayName = "My existing editor";
        app.NoInternet = true;
        app.AllowedNodes.Add("fixture-node");
        var before = JsonSerializer.Serialize(cfg);
        var quoted = "  \"" + Path.Combine(Path.GetDirectoryName(path)!, ".", Path.GetFileName(path)) + "\"  ";

        Assert.False(Assistant.TryAddApp(cfg, quoted, out _, out var error));

        Assert.Equal(Strings.T("en", "err_already_added"), error);
        Assert.Equal(before, JsonSerializer.Serialize(cfg));
        Assert.Same(app, Assert.Single(cfg.Apps));
    }

    [Fact]
    public void Legacy_launch_path_still_identifies_the_existing_app()
    {
        var path = Fixture("Legacy");
        var cfg = new CehoConfig
        {
            Language = "en",
            Apps = { new AppEntry { Name = "Legacy", Folder = Path.GetDirectoryName(path)!, Launch = path, Enabled = false } },
        };
        var before = JsonSerializer.Serialize(cfg);

        Assert.False(Assistant.TryAddApp(cfg, path, out _, out var error));

        Assert.Equal(Strings.T("en", "err_already_added"), error);
        Assert.Equal(before, JsonSerializer.Serialize(cfg));
    }

    [Theory]
    [InlineData("en", "covered by the rule")]
    [InlineData("ru", "покрывается правилом")]
    public void Another_executable_in_the_same_rule_reports_coverage_without_claiming_identity(string language, string phrase)
    {
        var first = Fixture("Suite", "first");
        var second = Fixture("Suite", "second");
        var cfg = new CehoConfig { Language = language };
        Assert.True(Assistant.TryAddApp(cfg, first, out _, out _));
        var before = JsonSerializer.Serialize(cfg);
        Assert.Null(AppIdentity.Find(cfg.Apps, second));

        Assert.False(Assistant.TryAddApp(cfg, second, out _, out var error));

        Assert.NotNull(error);
        Assert.Contains(phrase, error);
        Assert.NotEqual(Strings.T(language, "err_already_added"), error);
        Assert.Equal(before, JsonSerializer.Serialize(cfg));
    }

    [Fact]
    public void A_disabled_rule_with_the_same_folder_is_reported_without_duplicate_or_reenable()
    {
        var first = Fixture("Suite", "first");
        var second = Fixture("Suite", "second");
        var cfg = new CehoConfig { Language = "en" };
        Assert.True(Assistant.TryAddApp(cfg, first, out _, out _));
        cfg.Apps[0].Enabled = false;
        var before = JsonSerializer.Serialize(cfg);
        Assert.Null(AppIdentity.Find(cfg.Apps, second));

        Assert.False(Assistant.TryAddApp(cfg, second, out _, out var error));

        Assert.NotNull(error);
        Assert.Contains("exists but is disabled", error);
        Assert.NotEqual(Strings.T("en", "err_already_added"), error);
        Assert.Equal(before, JsonSerializer.Serialize(cfg));
        Assert.False(Assert.Single(cfg.Apps).Enabled);
    }

    [Fact]
    public void Identical_basenames_in_different_installations_are_distinct()
    {
        var first = Fixture("First");
        var second = Fixture("Second");
        var cfg = new CehoConfig();

        Assert.True(Assistant.TryAddApp(cfg, first, out _, out _));
        Assert.True(Assistant.TryAddApp(cfg, second, out _, out _));

        Assert.Equal(2, cfg.Apps.Count);
        Assert.NotEqual(AppIdentity.Id(cfg.Apps[0]), AppIdentity.Id(cfg.Apps[1]));
    }

    [Fact]
    public void Linux_case_distinct_installations_are_both_added()
    {
        if (!OperatingSystem.IsLinux()) return;
        var upper = Fixture("Editor");
        var lower = Fixture("editor");
        var cfg = new CehoConfig();

        Assert.True(Assistant.TryAddApp(cfg, upper, out _, out _));
        Assert.True(Assistant.TryAddApp(cfg, lower, out _, out _));

        Assert.Equal(2, cfg.Apps.Count);
        Assert.NotEqual(AppIdentity.Id(cfg.Apps[0]), AppIdentity.Id(cfg.Apps[1]));
    }

    [Fact]
    public void Resolved_symlink_alias_is_an_exact_duplicate()
    {
        if (OperatingSystem.IsWindows()) return; // Creating Windows symlinks may need privileges.
        var path = Fixture("Actual");
        var alias = Path.Combine(_root, "editor-link");
        File.CreateSymbolicLink(alias, path);
        var cfg = new CehoConfig { Language = "en" };

        Assert.True(Assistant.TryAddApp(cfg, alias, out _, out _));
        Assert.Equal(AppIdentity.Normalize(path), Assert.Single(cfg.Apps).IdentityPath);
        var before = JsonSerializer.Serialize(cfg);
        Assert.False(Assistant.TryAddApp(cfg, path, out _, out var error));

        Assert.Equal(Strings.T("en", "err_already_added"), error);
        Assert.Equal(before, JsonSerializer.Serialize(cfg));
    }

    [Fact]
    public void Selected_folder_has_its_own_identity_and_covers_executables_without_a_duplicate()
    {
        var path = Fixture("FolderSelection");
        var folder = Path.GetDirectoryName(path)!;
        var cfg = new CehoConfig { Language = "en" };

        Assert.True(Assistant.TryAddApp(cfg, folder, out _, out _));
        var app = Assert.Single(cfg.Apps);
        Assert.Equal(AppIdentity.Normalize(folder), app.IdentityPath);
        Assert.Null(app.Launch);
        Assert.False(Assistant.TryAddApp(cfg, path, out _, out var error));

        Assert.NotNull(error);
        Assert.Contains("covered by the rule", error);
        Assert.Same(app, Assert.Single(cfg.Apps));
    }

    [Fact]
    public void Missing_path_does_not_change_configuration()
    {
        var path = Path.Combine(_root, "missing");
        var cfg = new CehoConfig { Language = "en" };
        var before = JsonSerializer.Serialize(cfg);

        Assert.False(Assistant.TryAddApp(cfg, path, out var detected, out var error));

        Assert.Null(detected);
        Assert.Equal(Strings.T("en", "err_no_such_path", path), error);
        Assert.Equal(before, JsonSerializer.Serialize(cfg));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Direct_add_rejection_returns_before_persistence_and_rebuild(string lineEnding)
    {
        // Exercise both checkout styles on every platform, including the Windows
        // CRLF source that previously made the multi-line boundary unfindable.
        var program = ReadSource("Program.cs").ReplaceLineEndings(lineEnding);
        var source = Between(program, "    case \"add-app\":", "    case \"apps\":");
        var direct = Between(source, "        var cfg = CehoConfig.Load", "        return 0;\n    }");
        var rejection = Between(direct, "if (!Assistant.TryAddApp", "        cfg.Save");
        Assert.Contains("Console.Error.WriteLine(error)", rejection);
        Assert.Contains("return 1;", rejection);
        Assert.DoesNotContain("Save(", rejection);
        Assert.DoesNotContain("Rebuild", rejection);
        Assert.DoesNotContain("ApplyAsync", rejection);
        Assert.Contains("await Cli.RebuildQuietlyAsync(cfg)", direct);
        Assert.DoesNotContain("OrdinalIgnoreCase", source);
        Assert.Contains("if (!Assistant.AddAllFound(all))", source);
        Assert.Contains("if (!Assistant.AskApps(c)) return 0;", source);
    }

    [Fact]
    public void Every_cli_selection_path_uses_the_same_in_memory_identity_admission()
    {
        var assistant = ReadSource("Assistant.cs");
        var helper = Between(assistant, "    public static bool TryAddApp(", "    private static void AskProtection(");
        Assert.Contains("AppIdentity.Find(cfg.Apps, selected)", helper);
        Assert.Contains("AppCoverage.FindCoveringApp(cfg.Apps, selected)", helper);
        Assert.Contains("AppCoverage.FindEquivalentRule(cfg.Apps, detected)", helper);
        Assert.Contains("AppCoverage.FindStoredRule(cfg.Apps, detected)", helper);
        Assert.Contains("IdentityPath = selected", helper);
        Assert.DoesNotContain(".Save(", helper);
        Assert.DoesNotContain("ApplyAsync", helper);
        Assert.DoesNotContain("Rebuild", helper);
        Assert.DoesNotContain("OrdinalIgnoreCase", helper);
        var add = Between(assistant, "    public static bool AddApp(", "    public static bool TryAddApp(");
        Assert.Contains("if (!TryAddApp(cfg, path", add);
        Assert.Contains("if (AddApp(cfg, path))", assistant);
        Assert.Contains("else if (AddApp(cfg, tool.Path))", assistant);
        Assert.Contains("if (AddApp(cfg, app.Path))", assistant);
        Assert.Contains("else Assistant.AddApp(cfg, path);", ReadSource("Cli.cs"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Same_stored_folder_with_different_rule_shape_is_preserved_without_coverage_claim(bool enabled)
    {
        var path = Fixture("StoredKeyCollision");
        var detected = AppDetector.Detect(AppIdentity.Normalize(path), "en");
        var cfg = new CehoConfig { Language = "en" };
        cfg.Apps.Add(new AppEntry
        {
            Name = "existing settings", Folder = detected.Folder,
            IdentityPath = Path.Combine(detected.Folder, "other-selected-file"),
            Enabled = enabled, SingleFile = !detected.SingleFile,
        });
        Assert.Null(AppCoverage.FindEquivalentRule(cfg.Apps, detected));
        var before = JsonSerializer.Serialize(cfg);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            Assert.False(Assistant.TryAddApp(cfg, path, out _, out var error));
            Assert.Contains("different settings", error);
            Assert.DoesNotContain("covered", error);
            Assert.Equal(before, JsonSerializer.Serialize(cfg));
            Assert.Equal(enabled, Assert.Single(cfg.Apps).Enabled);
        }
    }

    private static string ReadSource(string name)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var file = Path.Combine(directory.FullName, "src", "ProxyCage.Cli", name);
            if (File.Exists(file)) return File.ReadAllText(file);
        }
        throw new FileNotFoundException(name);
    }

    private static string Between(string source, string begin, string end)
    {
        source = source.ReplaceLineEndings("\n");
        var first = source.IndexOf(begin, StringComparison.Ordinal);
        Assert.True(first >= 0, begin);
        var last = source.IndexOf(end, first + begin.Length, StringComparison.Ordinal);
        Assert.True(last > first, end);
        return source[first..last];
    }
}
