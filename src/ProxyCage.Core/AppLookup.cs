namespace ProxyCage.Core;

public static class AppLookup
{
    public static IReadOnlyList<AppEntry> ByName(IEnumerable<AppEntry> apps, string name) =>
        apps.Where(a => a.Label.Equals(name, StringComparison.OrdinalIgnoreCase)
                     || a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                     || Path.GetFileName(a.Folder.TrimEnd('/', '\\')).Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
}
