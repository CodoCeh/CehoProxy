namespace ProxyCage.Core.Tests;

/// <summary>
/// Установщик Windows качается с GitHub и крутится в PowerShell с Stop.
/// Чистая машина не должна падать на отсутствии задачи планировщика.
/// </summary>
public class InstallScriptTests
{
    [Fact]
    public void Fresh_install_does_not_call_schtasks_in_a_way_that_stops_the_script()
    {
        var script = File.ReadAllText(RepoFile("scripts/install.ps1"));

        Assert.Contains("$ErrorActionPreference = 'Stop'", script);
        Assert.Contains("schtasks /end /tn CehoProxy", script);
        Assert.Contains("cmd /c", script);
        Assert.DoesNotContain("& schtasks /end", script);
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
