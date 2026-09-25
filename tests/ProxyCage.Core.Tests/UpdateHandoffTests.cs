using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public sealed class UpdateHandoffTests
{
    [Fact]
    public void Pending_status_is_not_reported_as_success()
    {
        Assert.Equal(UpdateHandoff.PendingExitCode,
            UpdateHandoff.ExitCode(UpdateHandoff.Pending("cli-test", "1.2.70")));
        Assert.Equal(0, UpdateHandoff.ExitCode(UpdateHandoff.Verified("cli-test", "1.2.70")));
        Assert.Equal(1, UpdateHandoff.ExitCode(UpdateHandoff.Failed("cli-test", "1.2.70")));
    }

    [Fact]
    public void Old_status_cannot_complete_a_new_web_job_before_handoff()
    {
        var oldResult = UpdateHandoff.Verified("update-1", "1.2.69");
        Assert.False(UpdateHandoff.MatchesJob(oldResult, "update-1", handoffStageReached: false));
        Assert.True(UpdateHandoff.MatchesJob(oldResult, "update-1", handoffStageReached: true));
        Assert.False(UpdateHandoff.MatchesJob(oldResult, "update-2", handoffStageReached: true));
    }

    [Fact]
    public void Status_file_round_trips_job_identity_and_terminal_state()
    {
        var root = Path.Combine(Path.GetTempPath(), "ceho-update-status-" + Guid.NewGuid().ToString("N"));
        try
        {
            UpdateHandoff.Write(root, UpdateHandoff.Pending("update-12", "1.2.70"));
            var pending = UpdateHandoff.Read(root);
            Assert.Equal("update-12", pending?.JobId);
            Assert.Equal("pending", pending?.State);
            Assert.False(UpdateHandoff.IsTerminal(pending));

            UpdateHandoff.Write(root, UpdateHandoff.Verified("update-12", "1.2.70"));
            var verified = UpdateHandoff.Read(root);
            Assert.Equal("verified", verified?.State);
            Assert.True(UpdateHandoff.IsTerminal(verified));
            Assert.Equal(0, UpdateHandoff.ExitCode(verified));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Windows_helper_verifies_binary_and_daemon_before_success_and_rolls_back_on_failure()
    {
        var script = DaemonControl.WindowsUpdateRelaunchScript(
            42,
            @"C:\ProgramData\CehoProxy\cehoproxy.exe",
            @"C:\ProgramData\CehoProxy\cehoproxy.exe.new",
            @"C:\ProgramData\CehoProxy",
            autostart: true,
            expectedVersion: "1.2.70",
            jobId: "update-12",
            statusPath: @"C:\ProgramData\CehoProxy\update-status.json");

        var move = script.IndexOf("Move-Item -LiteralPath $downloaded", StringComparison.Ordinal);
        var versionCheck = script.IndexOf("-ne $expectedVersion", StringComparison.Ordinal);
        var daemonCheck = script.IndexOf("if (-not (Wait-InstalledDaemon)) { throw 'Новый daemon не запустился' }", StringComparison.Ordinal);
        var verified = script.IndexOf("Set-UpdateStatus 'verified'", StringComparison.Ordinal);
        Assert.True(move >= 0 && move < versionCheck && versionCheck < daemonCheck && daemonCheck < verified);
        Assert.Contains("Set-UpdateStatus 'failed'", script);
        Assert.Contains("Move-Item -LiteralPath $backup -Destination $exe -Force", script);
        Assert.Contains("schtasks /run /tn CehoProxy", script);
        Assert.Contains("schtasks /end /tn CehoProxy", script);
        Assert.Contains("cehoproxy-update.log", script);
    }

    [Fact]
    public void Missing_download_cannot_restore_a_stale_backup_over_the_current_binary()
    {
        var script = DaemonControl.WindowsUpdateRelaunchScript(
            42, @"C:\ProgramData\CehoProxy\cehoproxy.exe",
            @"C:\ProgramData\CehoProxy\cehoproxy.exe.new",
            @"C:\ProgramData\CehoProxy", autostart: true,
            expectedVersion: "1.2.70", jobId: "update-12",
            statusPath: @"C:\ProgramData\CehoProxy\update-status.json");

        var missingDownloadCheck = script.IndexOf("if (-not (Test-Path -LiteralPath $downloaded))", StringComparison.Ordinal);
        var staleBackupDelete = script.IndexOf("if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup", StringComparison.Ordinal);
        Assert.True(missingDownloadCheck >= 0 && missingDownloadCheck < staleBackupDelete);
        Assert.Contains("$movedCurrentToBackup = $false", script);
        Assert.Contains("$movedCurrentToBackup = $true", script);
        Assert.Contains("if ($movedCurrentToBackup -and (Test-Path -LiteralPath $backup))", script);
    }

    [Fact]
    public void Web_job_script_redirects_helper_failure_and_waits_during_pending()
    {
        Assert.Contains("if(j.state==='running')", WebUi.JobScript);
        Assert.Contains("if(j.state==='failed'&&box.dataset.relaunch)", WebUi.JobScript);
        Assert.Contains("j.result||'Обновление не удалось.'", WebUi.JobScript);
    }
}
