using System.Globalization;
using System.Reflection;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

/// <summary>
/// Exercise the real POST admission handler and local checkpoint replacement without
/// opening an HTTP listener or starting an engine, network probe, or system repair.
/// The nonparallel collection isolates the process-global job registry.
/// </summary>
[Collection("verified-panel")]
public sealed class RestoreAdmissionTests
{
    [Fact]
    public async Task Mode_change_waits_for_restore_and_cannot_overwrite_recovered_routing()
    {
        var root = Directory.CreateTempSubdirectory("ceho-restore-admission-").FullName;
        var configPath = Path.Combine(root, "config.json");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Job? restoreJob = null;
        try
        {
            var verified = new CehoConfig { Language = "en", PanelMode = CehoConfig.PanelModePro, MixedPort = 2081 };
            verified.Apps.Add(new AppEntry { Name = "Recovered app", Folder = Path.Combine(root, "recovered-app") });
            verified.Save(configPath);
            VerifiedConfigStore.CommitVerified(root, VerifiedConfigStore.Capture(root, verified, "{}"));
            var identity = VerifiedConfigStore.ReadStatus(root).VerifiedUtc!.Value;

            var current = new CehoConfig { Language = "en", PanelMode = CehoConfig.PanelModePro, MixedPort = 2090 };
            current.Apps.Add(new AppEntry { Name = "Current app", Folder = Path.Combine(root, "current-app") });
            current.Save(configPath);
            var before = File.ReadAllText(configPath);
            var web = new WebServer(configPath, () => new(false, null, null, null, false), _ => { });
            web.OnRestoreVerified = async (acknowledge, expected, _) =>
            {
                entered.TrySetResult();
                await release.Task;
                VerifiedConfigStore.Restore(root, acknowledge, expected);
                return "Recovered";
            };

            var admitted = await Post(web, configPath, "/settings/restore", new()
            {
                ["confirm_restore"] = "1",
                ["verified_utc"] = identity.ToString("O", CultureInfo.InvariantCulture),
            });
            Assert.False(admitted.IsError);
            Assert.NotNull(admitted.JobId);
            restoreJob = Jobs.Find(admitted.JobId);
            Assert.NotNull(restoreJob);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var blocked = await Post(web, configPath, "/mode", new() { ["mode"] = CehoConfig.PanelModeSimple });
            Assert.Equal(restoreJob.Id, blocked.JobId);
            Assert.Contains("Wait for configuration recovery", blocked.Message);
            Assert.Equal(before, File.ReadAllText(configPath));
            Assert.Equal(CehoConfig.PanelModePro, CehoConfig.Load(configPath).PanelMode);

            release.TrySetResult();
            await Finished(restoreJob);
            Assert.Equal(JobState.Done, restoreJob.State);
            var recovered = CehoConfig.Load(configPath);
            Assert.Equal(2081, recovered.MixedPort);
            Assert.Equal("Recovered app", Assert.Single(recovered.Apps).Name);

            // A fresh request after completion may change the preference, but must
            // reload and retain the just-restored routing rather than an old copy.
            var accepted = await Post(web, configPath, "/mode", new() { ["mode"] = CehoConfig.PanelModeSimple });
            Assert.False(accepted.IsError);
            Assert.Null(accepted.JobId);
            var after = CehoConfig.Load(configPath);
            Assert.Equal(CehoConfig.PanelModeSimple, after.PanelMode);
            Assert.Equal(2081, after.MixedPort);
            Assert.Equal("Recovered app", Assert.Single(after.Apps).Name);
        }
        finally
        {
            release.TrySetResult();
            if (restoreJob is not null) await Finished(restoreJob);
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static Task<(string? Message, bool IsError, string? JobId)> Post(WebServer web,
        string configPath, string path, Dictionary<string, string> form)
    {
        var method = typeof(WebServer).GetMethod("ApplyPostAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (Task<(string? Message, bool IsError, string? JobId)>)method.Invoke(web,
            new object[] { path, form, CehoConfig.Load(configPath) })!;
    }

    private static async Task Finished(Job job)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (job.Running && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.False(job.Running, "The injected restore did not finish.");
    }
}
