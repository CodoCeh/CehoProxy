using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public sealed class AppIdentityTests
{
    [Theory]
    [InlineData(OsKind.Windows, @"C:\Apps\Editor\editor.exe", "c:/apps/Editor/./editor.exe", true)]
    [InlineData(OsKind.Windows, @"C:\Apps\Editor\editor.exe", @"C:\Other\editor.exe", false)]
    [InlineData(OsKind.Linux, "/opt/Editor/editor", "/opt/editor/editor", false)]
    [InlineData(OsKind.Mac, "/Applications/Editor.app", "/Applications/editor.app", false)]
    [InlineData(OsKind.Linux, "/opt/editor/editor", "/opt/other/editor", false)]
    public void Path_identity_is_platform_aware_and_never_basename_only(OsKind platform, string a, string b, bool equal) =>
        Assert.Equal(equal, AppIdentity.SamePath(a, b, platform));

    [Fact]
    public void Routing_folder_does_not_replace_selected_executable_identity()
    {
        var root = Path.Combine(Path.GetTempPath(), "ceho-identity-not-created");
        var first = new AppEntry { Name = "same label", Folder = root, IdentityPath = Path.Combine(root, "first") };
        var second = new AppEntry { Name = "same label", Folder = root, IdentityPath = Path.Combine(root, "second") };
        Assert.NotEqual(AppIdentity.Id(first), AppIdentity.Id(second));
        Assert.Null(AppIdentity.Find(new[] { first }, second.IdentityPath!));
        first.DisplayName = "new display name";
        first.Enabled = false;
        Assert.Equal(AppIdentity.Id(first), AppIdentity.Id(new AppEntry { IdentityPath = first.IdentityPath }));
    }

    [Fact]
    public void Existing_executable_and_parent_aliases_resolve_without_running_them()
    {
        if (OperatingSystem.IsWindows()) return; // Unix symlinks need no special host privileges.
        var root = Directory.CreateTempSubdirectory("ceho-identity-").FullName;
        try
        {
            var targetDir = Directory.CreateDirectory(Path.Combine(root, "actual")).FullName;
            var target = Path.Combine(targetDir, "editor");
            File.WriteAllText(target, "This is test data, not an executable.");
            var alias = Path.Combine(root, "editor-link");
            File.CreateSymbolicLink(alias, target);
            var dirAlias = Path.Combine(root, "directory-link");
            Directory.CreateSymbolicLink(dirAlias, targetDir);
            Assert.True(AppIdentity.SamePath(target, alias));
            Assert.True(AppIdentity.SamePath(target, Path.Combine(dirAlias, "editor")));
            var app = new AppEntry { IdentityPath = AppIdentity.Normalize(alias) };
            var id = AppIdentity.Id(app);
            File.Delete(alias);
            File.CreateSymbolicLink(alias, Path.Combine(root, "not-resolvable"));
            Assert.Equal(id, AppIdentity.Id(app));
            Assert.False(AppIdentity.SamePath(alias, target));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Unresolved_shortcuts_do_not_become_the_target_from_their_name()
    {
        var root = Path.Combine(Path.GetTempPath(), "ceho-shortcut-not-created");
        Assert.False(AppIdentity.SamePath(Path.Combine(root, "Editor.lnk"), Path.Combine(root, "Editor.exe")));
        Assert.False(AppIdentity.SamePath(Path.Combine(root, "Editor.desktop"), Path.Combine(root, "Editor")));
    }

    [Fact]
    public void Coverage_is_separate_from_identity_and_follows_engine_regex_case_policy()
    {
        // AppDetector stores resolved folders. macOS temp paths may start at /var,
        // which is an alias of /private/var and is not the engine's literal rule path.
        var root = AppIdentity.Normalize(Directory.CreateTempSubdirectory("ceho-coverage-").FullName);
        try
        {
            var upper = Directory.CreateDirectory(Path.Combine(root, "App")).FullName;
            var executable = Path.Combine(upper, "program");
            File.WriteAllText(executable, "data");
            var app = new AppEntry { Name = "generic", Folder = upper, IdentityPath = upper };
            Assert.Null(AppIdentity.Find(new[] { app }, executable));
            Assert.Same(app, AppCoverage.FindCoveringApp(new[] { app }, executable));
            var config = new CehoConfig { Apps = new() { app } };
            Assert.True(AppCoverage.IsPathCovered(config, executable));
            if (Os.IsLinux)
            {
                Assert.False(AppCoverage.IsPathCovered(config, Path.Combine(root, "app", "program")));
                Assert.Null(AppCoverage.FindCoveringApp(new[] { app }, Path.Combine(root, "app", "program")));
            }
            app.Enabled = false;
            Assert.Null(AppCoverage.FindCoveringApp(new[] { app }, executable));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Alias_identity_does_not_rewrite_the_engine_rule_or_invent_coverage()
    {
        if (OperatingSystem.IsWindows()) return; // Creating Unix symlinks needs no privileged host setup.
        var root = AppIdentity.Normalize(Directory.CreateTempSubdirectory("ceho-coverage-alias-").FullName);
        try
        {
            var physical = Directory.CreateDirectory(Path.Combine(root, "physical-app")).FullName;
            var physicalExe = Path.Combine(physical, "program");
            File.WriteAllText(physicalExe, "Inert test data; never execute.");
            var alias = Path.Combine(root, "app-alias");
            Directory.CreateSymbolicLink(alias, physical);
            var aliasExe = Path.Combine(alias, "program");
            Assert.True(AppIdentity.SamePath(physicalExe, aliasExe));

            // The ordinary add path uses detection's canonical folder, including for aliases.
            var detected = AppDetector.Detect(aliasExe, "en");
            Assert.Equal(physical, detected.Folder);
            var canonicalRule = new AppEntry
            {
                Name = detected.Name, Folder = detected.Folder, SingleFile = detected.SingleFile,
                IdentityPath = AppIdentity.Normalize(aliasExe),
            };
            Assert.Same(canonicalRule, AppCoverage.FindCoveringApp(new[] { canonicalRule }, aliasExe));
            Assert.Same(canonicalRule, AppCoverage.FindCoveringApp(new[] { canonicalRule }, physicalExe));
            Assert.True(AppCoverage.IsPathCovered(new CehoConfig { Apps = new() { canonicalRule } }, physicalExe));

            // Legacy/imported literal aliases are different engine regexes. Identity equivalence
            // must not silently broaden their routing rule to an unconfigured physical path.
            var aliasRule = new AppEntry { Name = "generic", Folder = alias, IdentityPath = alias };
            var aliasConfig = new CehoConfig { Apps = new() { aliasRule } };
            Assert.True(AppCoverage.IsPathCovered(aliasConfig, aliasExe));
            Assert.False(AppCoverage.IsPathCovered(aliasConfig, physicalExe));
            Assert.Null(AppCoverage.FindCoveringApp(new[] { aliasRule }, aliasExe));
            Assert.Null(AppCoverage.FindCoveringApp(new[] { aliasRule }, physicalExe));
            Assert.Null(AppCoverage.FindEquivalentRule(new[] { aliasRule }, detected));
            Assert.Same(canonicalRule, AppCoverage.FindEquivalentRule(new[] { canonicalRule }, detected));
            Assert.False(AppIdentity.SameConfiguredPath(alias, physical));
            Assert.Equal(alias, aliasRule.Folder);
        }
        finally { Directory.Delete(root, true); }
    }


    [Fact]
    public void Equivalent_rules_preserve_file_and_version_agnostic_routing_scope()
    {
        const string windowsPackage = @"C:\Program Files\WindowsApps\Vendor.App_1.2.3_x64__publisher";
        var detected = new AppDetector.Detection(windowsPackage, "Fixture", true, false, "fixture");
        var fixedVersion = new AppEntry { Name = "Fixture", Folder = windowsPackage, VersionAgnostic = false };
        Assert.Null(AppCoverage.FindEquivalentRule(new[] { fixedVersion }, detected));
        Assert.Same(fixedVersion, AppCoverage.FindStoredRule(new[] { fixedVersion }, detected));
        var versionAgnostic = new AppEntry { Name = "Fixture", Folder = windowsPackage, VersionAgnostic = true, Enabled = false };
        Assert.Same(versionAgnostic, AppCoverage.FindEquivalentRule(new[] { versionAgnostic }, detected));
        var singleFile = new AppEntry { Name = "Fixture", Folder = windowsPackage, SingleFile = true };
        Assert.Null(AppCoverage.FindEquivalentRule(new[] { singleFile }, detected));
        Assert.Same(singleFile, AppCoverage.FindStoredRule(new[] { singleFile }, detected));
    }

}
