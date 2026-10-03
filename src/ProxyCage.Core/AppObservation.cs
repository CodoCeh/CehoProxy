namespace ProxyCage.Core;

/// <summary>
/// A bounded observation of an application's connections, never a promise about all traffic.
/// Kept independent of process/network inspection so the UI's safety claims can be tested with fixtures.
/// </summary>
internal static class AppObservation
{
    internal static readonly TimeSpan FreshFor = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan SubscriptionFreshFor = TimeSpan.FromHours(24);

    internal sealed record Result(string Kind, string Css, string Title, string Advice, bool Verified = false,
        bool Leak = false, bool Fresh = false);

    internal static string Text(CehoConfig cfg, string ru, string en) => cfg.Language == "en" ? en : ru;

    internal static bool IsFresh(DateTime observedAtUtc, DateTime nowUtc, bool refreshFailed = false) =>
        !refreshFailed && observedAtUtc != default && observedAtUtc <= nowUtc
        && nowUtc - observedAtUtc <= FreshFor;

    internal static bool SubscriptionVerified(SubscriptionEntry sub, DateTime nowUtc) =>
        sub.Enabled && sub.LastCheckOk == true && sub.LastNodes > 0
        && (sub.ExpiresUtc is null || sub.ExpiresUtc > nowUtc)
        && DateTime.TryParse(sub.LastCheckedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var checkedAt)
        && checkedAt.ToUniversalTime() <= nowUtc && nowUtc - checkedAt.ToUniversalTime() <= SubscriptionFreshFor;

    internal static Result Evaluate(CehoConfig cfg, AppEntry app, WebServer.AppLive? live,
        bool running, bool guarded, DateTime observedAtUtc, DateTime nowUtc, bool refreshFailed = false, bool rulesPending = false)
    {
        string T(string ru, string en) => Text(cfg, ru, en);
        if (!app.Enabled)
            return new("disabled", "off", T("Не выбрана для VPN", "Not selected for VPN"),
                T("Правило выключено. Программа может подключаться напрямую.", "This rule is disabled. The app may connect directly."));
        if (!running)
            return new("stopped", guarded ? "warn" : "off", T("Туннель выключен", "Tunnel is off"),
                guarded
                    ? T("Защитный барьер отмечен как включённый. Включите туннель на главной и проверьте соединения программы.", "The guard is recorded as enabled. This is not a check of the app's connections. Start the tunnel on Home and recheck.")
                    : T("Активный защитный барьер не подтверждён. Программа может подключаться напрямую. Включите туннель на главной и проверьте снова.", "An active guard is not confirmed. The app may connect directly. Start the tunnel on Home and recheck."));
        if (live is null)
            return new("unknown", "off", T("Ожидает проверки", "Awaiting a check"),
                T("Нет данных о соединениях. Откройте программу, создайте трафик и проверьте снова.", "No connection data. Open the app, generate traffic, then check again."));
        if (!IsFresh(observedAtUtc, nowUtc, refreshFailed))
            return new("stale", "warn", T("Данные устарели", "Observation is stale"),
                live.Direct > 0
                    ? T("В последней проверке был прямой трафик. Сейчас результат не подтверждён: проверьте снова.", "Direct traffic was found in the last check. The current result is unknown: check again.")
                    : T("Последний результат не подтверждает текущий маршрут. Проверьте снова.", "The last result does not verify the current route. Check again."));
        // Positive evidence of a leak wins over a missing process count or successful tunnel probe.
        if (live.Direct > 0)
            return new("leak", "bad", T($"Прямой трафик: {live.Direct}", $"Direct traffic: {live.Direct}"),
                T("Обнаружены соединения вне туннеля. Закройте и снова откройте программу, затем перепроверьте. Проверьте путь и вспомогательные процессы.",
                    "Connections outside the tunnel were observed. Close and reopen the app, then recheck. Review its path and helper processes."), Leak: true, Fresh: true);
        if (rulesPending)
            return new("pending-rules", "warn", T("Изменения ещё применяются", "Routing changes are pending"),
                T("Сохранённая настройка ещё не подтверждена работающим туннелем. Примените изменения, дождитесь завершения и проверьте снова.",
                    "The running tunnel has not confirmed the saved settings. Apply changes, wait for completion, then recheck."), Fresh: true);
        if (app.NoInternet)
            return live.Tunneled > 0 || live.EngineVpn > 0 || live.EngineDirect > 0
                ? new("blocked-traffic", "bad", T("Трафик при запрете интернета", "Traffic while Internet is blocked"),
                    T("Настройка запрещает интернет, но обнаружены соединения. Откройте диагностику и проверьте снова.", "The rule blocks Internet, but connections were observed. Open diagnostics and recheck."), Leak: true, Fresh: true)
                : new("blocked", "off", T("Трафик не замечен", "No traffic observed"),
                    T("Интернет запрещён настройкой. Отсутствие соединений само по себе не доказывает блокировку.", "Internet is blocked by configuration. No connections alone does not prove the block is effective."), Fresh: true);
        if (live.EngineVpn > 0)
            return new("vpn", live.EngineDirect > 0 ? "warn" : "on",
                live.EngineDirect > 0 ? T("VPN и прямые исключения", "VPN and direct exceptions") : T("VPN-трафик замечен", "VPN traffic observed"),
                live.EngineDirect > 0
                    ? T($"Через VPN: {live.EngineVpn}; напрямую по правилам: {live.EngineDirect}. Проверьте список сайтов.", $"Via VPN: {live.EngineVpn}; direct by rules: {live.EngineDirect}. Review the site list.")
                    : T("В этой проверке обнаружен VPN-трафик; прямые соединения не обнаружены.", "This sample contains VPN traffic; no direct connections were observed."), Verified: true, Fresh: true);
        if (live.EngineDirect > 0)
            return new("direct-rule", "warn", T("Напрямую по правилам", "Direct by rules"),
                T($"Прямых исключений: {live.EngineDirect}. VPN-трафик ещё не замечен; проверьте список сайтов.", $"Direct exceptions: {live.EngineDirect}. VPN traffic has not been observed; review the site list."), Fresh: true);
        if (live.Tunneled > 0)
            return new("tunnel", "warn", T("Трафик входит в туннель", "Traffic enters the tunnel"),
                T("Соединения с туннелем видны, но выход программы через VPN ещё не подтверждён. Создайте трафик и проверьте снова.",
                    "Tunnel connections are visible, but the app's VPN route is not yet confirmed. Generate traffic and recheck."), Fresh: true);
        return live.Processes == 0
            ? new("idle", "off", T("Откройте программу", "Open the app"),
                T("Процесс не найден. Откройте программу и выполните в ней сетевое действие, затем проверьте снова.", "No matching process was found. Open the app and use a network feature, then check again."), Fresh: true)
            : new("quiet", "off", T("Нет трафика для проверки", "No traffic to verify"),
                T("Программа открыта, но соединений не видно. Откройте сайт или обновите данные в программе, затем проверьте снова.", "The app is open, but no connections are visible. Open a site or refresh data in the app, then check again."), Fresh: true);
    }

    internal static string ConfiguredRoute(CehoConfig cfg, AppEntry app) => !app.Enabled
        ? Text(cfg, "Правило выключено", "Rule disabled")
        : app.NoInternet ? Text(cfg, "Интернет запрещён", "Internet blocked")
        : app.AllowedNodes.Count > 0 ? Text(cfg, $"VPN · выбранных серверов: {app.AllowedNodes.Count}", $"VPN · selected servers: {app.AllowedNodes.Count}")
        : Text(cfg, "VPN · общий маршрут", "VPN · shared route");

    internal static string StopConsequence(CehoConfig cfg, bool windows, bool guarded) => windows && !guarded
        ? Text(cfg, "Выключение туннеля не удаляет правила защиты Windows. Сейчас наличие защитного барьера не подтверждено; нельзя обещать ни блокировку, ни прямой доступ.",
            "Turning the tunnel off does not remove Windows guard rules. A guard is not currently confirmed; neither blocking nor direct access can be promised.")
        : windows ? Text(cfg, "После выключения туннеля правила защиты Windows сохраняются для выбранных программ. Интернет у них может остаться заблокированным. Выключение VPN не включает прямой доступ.",
            "After the tunnel is turned off, Windows guard rules remain for selected apps. Their Internet access may stay blocked. Turning VPN off does not enable direct access.")
        : guarded || cfg.FailClosed
            ? Text(cfg, "После выключения VPN защитный барьер должен блокировать выбранные программы, пока служба работает. Проверьте его состояние после остановки.",
                "After VPN is turned off, the guard is intended to block selected apps while the service is running. Check its state after stopping.")
            : Text(cfg, "После выключения VPN защитный барьер не подтверждён. Выбранные программы могут подключаться напрямую.",
                "After VPN is turned off, a guard is not confirmed. Selected apps may connect directly.");
}
