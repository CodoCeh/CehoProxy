using System.Text.RegularExpressions;

namespace ProxyCage.Core;

/// <summary>Подбор отдельной ноды для программы, которой общая нода не подошла.</summary>
public static class NodeChoice
{
    private static readonly Regex GoogleMark = new(@"(?<![\p{L}\p{N}])(G\+|Gemini|Antigravity)(?![\p{L}\p{N}])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    /// <summary>Провайдер пометил ноду как рабочую с сервисами Google (в названии «G+», «Gemini» или «Antigravity»).</summary>
    public static bool IsGoogleMarked(ProxyNode node) =>
        !node.IsMeta && GoogleMark.IsMatch((node.Remark ?? "") + " " + node.Tag);

    public static List<ProxyNode> GoogleMarked(IEnumerable<ProxyNode> pool) =>
        pool.Where(IsGoogleMarked).ToList();

    /// <summary>
    /// Следующая нода для программы: не выключенная вручную, не отмеченная как неподходящая, не текущая.
    /// Сначала ноды другой страны, чем у только что отвергнутых; внутри группы — с меньшей задержкой.
    /// </summary>
    public static ProxyNode? Next(AppEntry app, IReadOnlyList<ProxyNode> pool, CehoConfig cfg)
    {
        var rejected = pool.Where(n => app.UnsuitableNodes.Contains(n.Key, StringComparer.OrdinalIgnoreCase)
            || app.AllowedNodes.Contains(n.Key, StringComparer.OrdinalIgnoreCase)).ToList();
        var rejectedCountries = rejected.Select(n => n.CountryCode ?? "").Where(c => c.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return pool
            .Where(n => !n.IsMeta
                && !cfg.BlockedNodes.Contains(n.Key, StringComparer.OrdinalIgnoreCase)
                && !app.UnsuitableNodes.Contains(n.Key, StringComparer.OrdinalIgnoreCase)
                && !app.AllowedNodes.Contains(n.Key, StringComparer.OrdinalIgnoreCase))
            .OrderBy(n => rejectedCountries.Contains(n.CountryCode ?? "") ? 1 : 0)
            .ThenBy(n => cfg.NodeLatency.TryGetValue(n.Key, out var ms) ? ms : int.MaxValue)
            .ThenBy(n => n.Remark, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }
}
