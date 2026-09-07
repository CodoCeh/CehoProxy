namespace ProxyCage.Core;

/// <summary>
/// Разбор «1,3, 5» в индексы списка. Пустая строка — никого не выбрали.
/// </summary>
public static class IndexList
{
    public static bool TryParse(string? text, int count, out List<int> indexes)
    {
        indexes = new List<int>();
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (count < 1) return false;

        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, out var n) || n < 1 || n > count)
            {
                indexes.Clear();
                return false;
            }

            var i = n - 1;
            if (!indexes.Contains(i)) indexes.Add(i);
        }

        return true;
    }
}
