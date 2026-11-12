using System.Text;

namespace ProxyCage.Core;

/// <summary>Окно «Что нового» после обновления: что изменилось и где это включить. Показывается один раз на версию.</summary>
public static class WhatsNew
{
    public sealed record Item(string TitleRu, string TitleEn, string TextRu, string TextEn, string? FocusTab = null);

    public sealed record Release(string Version, Item[] Items);

    public static readonly Release[] Releases =
    {
        new("1.2.181", new[]
        {
            new Item("CehoProxy Light", "CehoProxy Light",
                "Второе окно CehoProxy в оформлении Android-приложения: те же функции и те же настройки, что в веб-панели. Открывается из значка в трее.",
                "A second CehoProxy window styled like the Android app: same features and settings as the web panel. Opens from the tray icon.",
                "access"),
            new Item("Что открывает ярлык", "What the shortcut opens",
                "Двойной щелчок по ярлыку открывает веб-панель или Light, как выберете. Выбор можно запомнить и поменять в настройках.",
                "Double-clicking the shortcut opens the web panel or Light, as you choose. You can remember the choice and change it in settings.",
                "access"),
            new Item("Подписки как на телефоне", "Subscriptions like on the phone",
                "В Light подписку добавляют кнопкой «Вставить из буфера»; ссылка из буфера предлагается сама.",
                "In Light you add a subscription with the Paste button; a link on the clipboard is offered automatically.",
                "subs"),
            new Item("Быстрее запуск и смена сети", "Faster start and network change",
                "Проверка стран больше не задерживает подключение: перезапуск и переподключение после смены сети занимают секунды, а не минуты.",
                "Country checks no longer delay connecting: restart and reconnect after a network change take seconds, not minutes."),
        }),
        new("1.2.186", new[]
        {
            new Item("Ваша страна", "Your country",
                "На вкладке «Программы» можно выбрать свою страну, как в приложении для Android: её сайты открываются напрямую, остальное идёт через VPN.",
                "On the Apps tab you can pick your country, as in the Android app: its sites open directly, everything else uses the VPN."),
            new Item("Кнопка обновления", "Update button",
                "Когда выходит новая версия, в шапке рядом с переключателем «Простой / Для профи» появляется кнопка «Обновить до …».",
                "When a new version is out, an Update to … button appears in the header next to the Simple / Pro switch."),
            new Item("Программа, которой ещё нет", "An app that is not installed yet",
                "Программу можно добавить по полному пути заранее: правило начнёт работать, когда она появится.",
                "You can add an app by its full path in advance: the rule starts working once the app is there."),
            new Item("Подписка с телефона без приложения", "Subscription from a phone without the app",
                "QR-код приёма открывает в браузере телефона страницу, где можно вставить ссылку на подписку. Подходит и для iPhone. Понимает ссылки happ://add и karing://install-config.",
                "The receive QR code opens a page in the phone browser where you can paste a subscription link. Works on iPhone too. Understands happ://add and karing://install-config links.",
                "subs"),
        }),
        new("1.2.187", new[]
        {
            new Item("Два способа удаления", "Two ways to uninstall",
                "В настройках можно удалить CehoProxy полностью или сохранить подписки и программы. После повторной установки панель предложит вернуть их.",
                "In settings you can remove CehoProxy completely or keep subscriptions and apps. After reinstalling, the panel offers to bring them back."),
            new Item("Исправления на Mac", "Mac fixes",
                "Кнопка «Удалить» убирает программу целиком, ошибка «CehoProxy.Engine» больше не появляется, значок в «Программах» называется CehoProxy.",
                "The Uninstall button removes the app completely, the CehoProxy.Engine error no longer appears, and the icon in Applications is named CehoProxy."),
        }),
        new("1.2.189", new[]
        {
            new Item("Все российские сайты напрямую", "All Russian sites directly",
                "Если на вкладке «Программы» выбрана страна «Россия», напрямую идут все сайты в зонах .ru, .рф, .su и других российских, включая региональные порталы.",
                "If Russia is selected on the Apps tab, all sites in the .ru, .рф, .su and other Russian zones go directly, including regional portals."),
        }),
    };

    public static bool IsNewer(string candidate, string current) => Updater.IsNewerVersion(candidate, current);

    /// <summary>Пункты между просмотренной и установленной версией, свежее сверху.</summary>
    public static List<Item> Since(string seen, string current, IEnumerable<Release>? releases = null)
    {
        var items = new List<Item>();
        foreach (var release in (releases ?? Releases).Reverse())
            if (IsNewer(release.Version, seen) && !IsNewer(release.Version, current))
                items.AddRange(release.Items);
        return items;
    }

    /// <summary>Что показать сейчас. Свежая установка (нет ни подписок, ни программ) окна не видит.</summary>
    public static List<Item> Pending(CehoConfig cfg, string current, IEnumerable<Release>? releases = null)
    {
        if (cfg.WhatsNewSeen is null)
        {
            if (cfg.Subscriptions.Count == 0 && cfg.Apps.Count == 0) return new List<Item>();
            return Since("0", current, releases);
        }
        return Since(cfg.WhatsNewSeen, current, releases);
    }

    public const string Css = """
    .whatsnew-back{position:fixed;inset:0;background:rgba(0,0,0,.45);display:flex;align-items:center;justify-content:center;padding:16px;z-index:50}
    .whatsnew-back[hidden]{display:none}
    .whatsnew{background:var(--surface);color:var(--text);border:1px solid var(--line);border-radius:18px;padding:22px 24px 18px;width:100%;max-width:480px;max-height:86vh;display:flex;flex-direction:column;box-shadow:0 18px 48px rgba(0,0,0,.28)}
    .whatsnew .wn-label{font-size:12px;letter-spacing:.12em;text-transform:uppercase;color:var(--brand-ink);font-weight:700}
    .whatsnew h2{margin:6px 0 0;font-size:22px}
    .whatsnew .wn-list{overflow:auto;margin:6px 0 0;padding:0;list-style:none}
    .whatsnew .wn-item{border-left:4px solid var(--brand-ink);padding-left:14px;margin-top:18px}
    .whatsnew .wn-item b{display:block;font-size:15px}
    .whatsnew .wn-item span{display:block;color:var(--muted);font-size:13px;margin-top:3px}
    .whatsnew .wn-item a{display:inline-block;margin-top:6px;font-size:13px;font-weight:600}
    .whatsnew .wn-actions{display:flex;justify-content:flex-end;gap:10px;margin-top:22px}
    """;

    public const string Script = """
    (function(){var b=document.getElementById('whatsnew');if(!b)return;
      function done(go){try{fetch('/whatsnew/seen',{method:'POST',headers:{'Content-Type':'application/x-www-form-urlencoded'},body:'x=1',credentials:'same-origin'}).catch(function(){})}catch(e){}
        b.hidden=true;if(go)location.href=go}
      document.getElementById('whatsnew-ok').addEventListener('click',function(){done()});
      var open=document.getElementById('whatsnew-open');if(open)open.addEventListener('click',function(){done(open.dataset.href)});
      document.querySelectorAll('#whatsnew .wn-item a').forEach(function(a){a.addEventListener('click',function(e){e.preventDefault();done(a.getAttribute('href'))})});
      document.addEventListener('keydown',function(e){if(e.key==='Escape'&&!b.hidden)done()});
    })();
    """;

    public static void Render(StringBuilder sb, CehoConfig cfg, List<Item> items, string current, Func<string, string> S, Func<string?, string> hrefFor)
    {
        if (items.Count == 0) return;
        var ru = cfg.Language != "en";
        string E(string s) => System.Net.WebUtility.HtmlEncode(s);
        sb.Append("<div id=whatsnew class=whatsnew-back role=dialog aria-modal=true aria-labelledby=whatsnew-title><div class=whatsnew>");
        sb.Append("<div class=wn-label>").Append(E(S("whatsnew_label"))).Append(' ').Append(E(current)).Append("</div>");
        sb.Append("<h2 id=whatsnew-title>").Append(E(S("whatsnew_title"))).Append("</h2><ul class=wn-list>");
        string? first = null;
        foreach (var item in items)
        {
            var title = ru ? item.TitleRu : item.TitleEn;
            sb.Append("<li class=wn-item><b>").Append(E(title)).Append("</b><span>").Append(E(ru ? item.TextRu : item.TextEn)).Append("</span>");
            if (item.FocusTab is not null)
            {
                var href = hrefFor(item.FocusTab);
                first ??= href;
                sb.Append("<a href=\"").Append(E(href)).Append("\">").Append(E(S("whatsnew_settings"))).Append(" → ").Append(E(title)).Append("</a>");
            }
            sb.Append("</li>");
        }
        sb.Append("</ul><div class=wn-actions><button type=button id=whatsnew-ok>").Append(E(S("whatsnew_ok"))).Append("</button>");
        if (first is not null)
            sb.Append("<button type=button id=whatsnew-open class=primary data-href=\"").Append(E(first)).Append("\">").Append(E(S("whatsnew_open"))).Append("</button>");
        sb.Append("</div></div></div><script>").Append(Script).Append("</script>");
    }
}
