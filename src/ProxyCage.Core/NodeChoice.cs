namespace ProxyCage.Core;

/// <summary>Подбор отдельной ноды для программы, которой общая нода не подошла.</summary>
public static class NodeChoice
{
    public const int MinWordLength = 2;

    /// <summary>Ноды, в названии которых есть слово. Слово задаёт пользователь: пометки у провайдеров разные и работу не гарантируют.</summary>
    public static List<ProxyNode> ByWord(IEnumerable<ProxyNode> pool, string? word)
    {
        var w = (word ?? "").Trim();
        if (w.Length < MinWordLength) return [];
        return pool.Where(n => !n.IsMeta
            && ((n.Remark ?? "").Contains(w, StringComparison.OrdinalIgnoreCase) || (n.Tag ?? "").Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
    }

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
