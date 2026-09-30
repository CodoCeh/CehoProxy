using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class UpdateRollbackTests
{
    private static (string Root, string Exe) Setup(bool protectedBefore, bool backup, string state = "verified", string version = "2.0.0")
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var exe = Path.Combine(root, "cehoproxy");
        File.WriteAllText(exe, "new");
        if (backup) File.WriteAllText(UpdateRollback.BackupOf(exe), "old");
        UpdateHandoff.Write(root, new UpdateHandoff.Status("job", state, version, ""));
        UpdateRollback.RememberProtection(root, protectedBefore);
        return (root, exe);
    }

    [Fact]
    public void Rolls_back_only_a_fresh_verified_update_that_was_protected_before()
    {
        var (root, exe) = Setup(protectedBefore: true, backup: true);
        try
        {
            Assert.True(UpdateRollback.ShouldRollBack(root, "2.0.0", exe, DateTime.UtcNow));
            Assert.False(UpdateRollback.ShouldRollBack(root, "2.0.1", exe, DateTime.UtcNow));
            Assert.False(UpdateRollback.ShouldRollBack(root, "2.0.0", exe, DateTime.UtcNow.AddMinutes(30)));

            UpdateRollback.Mark(root, "2.0.0");
            Assert.True(UpdateRollback.WasRolledBack(root, "2.0.0"));
            Assert.False(UpdateRollback.ShouldRollBack(root, "2.0.0", exe, DateTime.UtcNow));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false, true, "verified")]
    [InlineData(true, false, "verified")]
    [InlineData(true, true, "pending")]
    [InlineData(true, true, "failed")]
    public void Keeps_the_new_version_otherwise(bool protectedBefore, bool backup, string state)
    {
        var (root, exe) = Setup(protectedBefore, backup, state);
        try { Assert.False(UpdateRollback.ShouldRollBack(root, "2.0.0", exe, DateTime.UtcNow)); }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Candidate_is_a_copy_of_the_backup()
    {
        var (root, exe) = Setup(protectedBefore: true, backup: true);
        try
        {
            var candidate = UpdateRollback.PrepareCandidate(exe)!;
            Assert.Equal("old", File.ReadAllText(candidate));
            Assert.True(File.Exists(UpdateRollback.BackupOf(exe)));
        }
        finally { Directory.Delete(root, true); }
    }
}
