using System.Text;
using System.Security.AccessControl;
using System.Security.Principal;

namespace ProxyCage.Core;

/// <summary>Replace a secret-bearing local file without exposing a partially written file.</summary>
internal static class PrivateFile
{
    internal enum WriteStage { Created, PartiallyWritten, Flushed, Replaced }

    internal static void Write(string path, string text) => Write(path, text, null);

    // Per-call seam for fault injection; never shared mutable process state.
    internal static void Write(string path, string text, Action<WriteStage, string>? observe)
    {
        path = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        RejectLink(path);
        var temporary = Path.Combine(dir, ".ceho-private-" + Guid.NewGuid().ToString("N"));
        var ownsTemporary = false;
        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
                Options = FileOptions.WriteThrough,
            };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = OperatingSystem.IsWindows()
                ? CreatePrivateWindowsFile(temporary)
                : new FileStream(temporary, options))
            {
                ownsTemporary = true;
                observe?.Invoke(WriteStage.Created, temporary);
                var bytes = Encoding.UTF8.GetBytes(text);
                var halfway = bytes.Length / 2;
                stream.Write(bytes.AsSpan(0, halfway));
                observe?.Invoke(WriteStage.PartiallyWritten, temporary);
                stream.Write(bytes.AsSpan(halfway));
                stream.Flush(flushToDisk: true);
                observe?.Invoke(WriteStage.Flushed, temporary);
            }
            RejectLink(path);
            File.Move(temporary, path, overwrite: true);
            ownsTemporary = false;
            // The replacement inherits the private temporary's mode/DACL. Do not sweep
            // similarly named files or change directory permissions: those are not ours.
            observe?.Invoke(WriteStage.Replaced, path);
        }
        finally { if (ownsTemporary) try { File.Delete(temporary); } catch { } }
    }

    internal static void RejectLink(string path)
    {
        if (new FileInfo(path).LinkTarget is not null ||
            (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0))
            throw new IOException("A configuration file cannot be a symbolic link or reparse point.");
    }

    internal static string ReadText(string path, int maxBytes)
    {
        RejectLink(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (stream.Length > maxBytes) throw new InvalidDataException("Configuration file is too large.");
        using var content = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, maxBytes - content.Length + 1))) > 0)
        {
            if (content.Length + read > maxBytes) throw new InvalidDataException("Configuration file is too large.");
            content.Write(buffer, 0, read);
        }
        content.Position = 0;
        using var reader = new StreamReader(content, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static FileStream CreatePrivateWindowsFile(string path)
    {
        // Apply the DACL during creation, so no reader can open an unrestricted temporary file.
        var acl = new FileSecurity();
        acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var identities = new[]
        {
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            WindowsIdentity.GetCurrent().User,
        };
        foreach (var identity in identities.Where(s => s is not null).Distinct())
            acl.AddAccessRule(new FileSystemAccessRule(identity!, FileSystemRights.FullControl, AccessControlType.Allow));
        return new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.Write | FileSystemRights.ReadPermissions,
            FileShare.None, 4096, FileOptions.WriteThrough, acl);
    }
}
