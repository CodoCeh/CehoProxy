namespace ProxyCage.Core.Tests;

/// <summary>
/// Установщик Windows качается с GitHub и часто запускается через iex.
/// Чистая машина и оборванное скачивание не должны закрывать окно PowerShell.
/// </summary>
public class InstallScriptTests
{
    [Fact]
    public void Fresh_install_does_not_call_schtasks_in_a_way_that_stops_the_script()
    {
        var script = File.ReadAllText(RepoFile("scripts/install.ps1"));

        Assert.Contains("schtasks /end /tn CehoProxy", script);
        Assert.Contains("cmd /c", script);
        Assert.DoesNotContain("& schtasks /end", script);
    }

    [Fact]
    public void Iex_must_not_exit_the_host_and_must_reject_a_truncated_download()
    {
        var script = File.ReadAllText(RepoFile("scripts/install.ps1"));

        Assert.DoesNotContain("exit 1", script);
        Assert.DoesNotContain("$ErrorActionPreference = 'Stop'", script);
        Assert.Contains("$ProgressPreference = 'SilentlyContinue'", script);
        Assert.Contains("curl.exe", script);
        Assert.Contains("10MB", script);
        Assert.Contains("[Console]::IsInputRedirected", script);
    }

    private static string RepoFile(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException(relative);
    }
}
