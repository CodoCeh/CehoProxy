using System.Net;
using System.Text;

namespace ProxyCage.Core;

/// <summary>Local support/recovery UI, with no remote support destination or telemetry.</summary>
public static class SafetyPanel
{
    private static string E(string value) => WebUtility.HtmlEncode(value);
    internal static string T(string language, string ru, string en) => language == "ru" ? ru : en;

    public static void RenderReport(StringBuilder sb, CehoConfig cfg)
    {
        string T(string ru, string en) => SafetyPanel.T(cfg.Language, ru, en);
        sb.Append("<section id=support-report><h2>").Append(E(T("Отчёт для поддержки", "Support report"))).Append("</h2>");
        sb.Append("<p class=hint>").Append(E(T(
            "Создаётся только на этом устройстве и никуда не отправляется. Пароли, ссылки подписок, адреса, имена и пути скрыты; вместо текста ошибок и журнала включаются категории проблем. Перед передачей проверьте предпросмотр.",
            "Created only on this device and never sent automatically. Passwords, subscription links, addresses, names and paths are hidden; errors and logs include issue categories only. Review the preview before sharing."))).Append("</p>");
        sb.Append("<form method=post action=/support/preview class=stack>");
        foreach (var (field, ru, en, selected) in new[] {
            ("report_environment", "Версия и платформа", "Version and platform", true),
            ("report_configuration", "Сводка настроек", "Configuration summary", true),
            ("report_diagnostics", "Последняя диагностика", "Last diagnostics", true),
            ("report_logs", "Сводка последних событий", "Recent event summary", false) })
            sb.Append("<label class=check><input type=checkbox name=").Append(field).Append(" value=1")
              .Append(selected ? " checked" : "").Append("> ").Append(E(T(ru, en))).Append("</label>");
        sb.Append("<button>").Append(E(T("Предпросмотр отчёта", "Preview report"))).Append("</button></form></section>");
    }

    public static string RenderReportPreview(CehoConfig cfg, string report, SupportReport.Parts parts)
    {
        string T(string ru, string en) => SafetyPanel.T(cfg.Language, ru, en);
        var sb = new StringBuilder("<!doctype html><html lang=\"").Append(E(cfg.Language == "ru" ? "ru" : "en"))
            .Append("\"><meta charset=utf-8><meta name=viewport content=\"width=device-width,initial-scale=1\"><title>")
            .Append(E(T("Предпросмотр отчёта", "Report preview"))).Append("</title>")
            .Append(WebUi.ThemeEarlyScript).Append("<style>").Append(WebUi.Css)
            .Append("pre{white-space:pre-wrap;overflow-wrap:anywhere;background:var(--panel);border-radius:12px;padding:20px}</style><body><main class=wrap><header><span class=mark>CehoProxy</span><button type=button class=theme id=theme data-light=\"")
            .Append(E(T("Светлая тема", "Light theme"))).Append("\" data-dark=\"")
            .Append(E(T("Тёмная тема", "Dark theme"))).Append("\">").Append(WebUi.ThemeIcons).Append("</button></header><h1>")
            .Append(E(T("Предпросмотр отчёта", "Report preview"))).Append("</h1><p>")
            .Append(E(T("Ничего не отправлено. Скачивание сохраняет только показанный ниже текст в файл на вашем устройстве.",
                "Nothing has been sent. Download saves only the text shown below to a file on your device."))).Append("</p><pre>")
            .Append(E(report)).Append("</pre><form method=post action=/support/download>")
            // Download this exact preview instead of collecting fresh content after review.
            .Append("<input type=hidden name=report value=\"").Append(E(report)).Append("\"><button>")
            .Append(E(T("Скачать этот отчёт", "Download this report"))).Append("</button></form><a href=\"/?tab=help#support-report\">")
            .Append(E(T("Назад к выбору разделов", "Back to section selection"))).Append("</a></main>").Append(WebUi.ThemeScript).Append("</body></html>");
        return sb.ToString();
    }

    public static void RenderRecovery(StringBuilder sb, CehoConfig cfg, string root, string tab = "access")
    {
        string T(string ru, string en) => SafetyPanel.T(cfg.Language, ru, en);
        var status = VerifiedConfigStore.ReadStatus(root);
        sb.Append("<section id=config-recovery><h2>").Append(E(T("Рабочие настройки", "Last working configuration"))).Append("</h2><p class=hint>")
            .Append(E(T("Контрольная копия сохраняется после успешного запуска и проверки готовности движка. Возврат настроек отличается от отката версии и зашифрованного экспорта.",
                "A checkpoint is saved after the engine starts successfully and passes its readiness check. Configuration recovery is separate from version rollback and encrypted export."))).Append("</p>");
        if (!status.Available)
        {
            sb.Append("<p>").Append(E(T(status.Error is null ? "Проверенной копии пока нет. Она появится после успешного запуска." : "Проверенная копия недоступна или повреждена.",
                status.Error is null ? "No verified checkpoint yet. It will be created after a successful start." : "The verified checkpoint is unavailable or damaged."))).Append("</p></section>");
            return;
        }
        sb.Append("<p>").Append(E(T("Проверено: ", "Verified: "))).Append(E(status.VerifiedUtc!.Value.ToString("yyyy-MM-dd HH:mm 'UTC'")))
            .Append(" · ").Append(status.Apps).Append(E(T(" программ · ", " apps · "))).Append(status.Subscriptions)
            .Append(E(T(" подписок", " subscriptions"))).Append("</p><p class=hint>")
            .Append(E(T("Будут заменены правила программ, подписок и сайтов, параметры сети и сохранённые правила движка. Пароль панели, её порт, язык и настройки обновления сохранятся. Применение может прервать соединения.",
                "This replaces app, subscription and site rules, network settings and saved engine rules. The panel password, panel port, language and update preferences are kept. Applying may interrupt connections."))).Append("</p>");
        sb.Append("<form method=post action=/settings/restore class=stack><input type=hidden name=tab value=\"").Append(E(tab)).Append("\">")
          .Append("<input type=hidden name=verified_utc value=\"").Append(E(status.VerifiedUtc.Value.ToString("O"))).Append("\">");
        sb.Append("<label class=check><input type=checkbox name=confirm_restore value=1 required> ")
            .Append(E(T("Вернуть эту проверенную конфигурацию и применить её", "Restore this verified configuration and apply it"))).Append("</label>");
        if (status.RequiresSecurityAcknowledgement)
            sb.Append("<p class=\"flash err\">").Append(E(T("Внимание: в копии отключена проверка TLS-сертификата для другого сервера или ослаблена блокировка утечек. Это снижает защиту.",
                "Warning: the checkpoint disables TLS certificate verification for another server or weakens leak blocking. This reduces protection.")))
              .Append("</p><label class=check><input type=checkbox name=acknowledge_security value=1 required> ")
              .Append(E(T("Я проверил и разрешаю эти изменения защиты", "I reviewed and authorize these protection changes"))).Append("</label>");
        sb.Append("<button class=ghost>").Append(E(T("Вернуть рабочие настройки", "Restore working configuration"))).Append("</button></form></section>");
    }
}
