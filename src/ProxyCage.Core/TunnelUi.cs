using System.Net;
using System.Text;

namespace ProxyCage.Core;

/// <summary>Progressive enhancement for the desktop tunnel. A visual transition is never traffic evidence.</summary>
public static partial class TunnelUi
{
    private static string E(string text) => WebUtility.HtmlEncode(text);

    internal static void Render(StringBuilder sb, CehoConfig cfg, IReadOnlyList<InstalledAppCatalog.Entry> catalog,
        string tab, bool running, bool pending, bool busy, string? pickedPath = null)
    {
        string T(string ru, string en) => cfg.Language == "ru" ? ru : en;
        sb.Append("<section id=tunnel-workspace class=tunnel-workspace data-tab=\"").Append(E(tab))
          .Append("\" data-running=").Append(running ? "true" : "false").Append(" data-pending=").Append(pending ? "true" : "false")
          .Append(" data-busy=").Append(busy ? "true" : "false").Append('>');
        sb.Append("<div class=tunnel-panel><h2>").Append(E(T("Добавьте программу в тоннель", "Add an app to the tunnel")))
          .Append("</h2><p class=lede>").Append(E(T("Перетащите её из каталога или выберите вручную", "Drag it from the catalog or choose it manually"))).Append("</p>");
        sb.Append("<div id=tunnel-stage class=tunnel-stage role=group aria-label=\"")
          .Append(E(T("Область добавления программы", "App drop area"))).Append("\" aria-describedby=tunnel-drop-help data-phase=idle>")
          .Append("<div class=tunnel-portal aria-hidden=true><svg viewBox=\"0 0 420 300\" focusable=false>")
          .Append("<defs><linearGradient id=tunnel-shell x1=0 y1=0 x2=1 y2=1><stop stop-color=\"#34434b\"/><stop offset=1 stop-color=\"#14212b\"/></linearGradient>")
          .Append("<linearGradient id=tunnel-edge x1=0 y1=0 x2=1 y2=1><stop stop-color=\"#0bc48c\"/><stop offset=1 stop-color=\"#13825d\"/></linearGradient></defs>")
          .Append("<ellipse class=tunnel-shadow cx=221 cy=269 rx=123 ry=15 fill=\"#172d2510\"/>")
          .Append("<path class=tunnel-rim d=\"M250 35 A111 111 0 1 1 135 221 A111 111 0 0 1 250 35Z\" fill=\"url(#tunnel-edge)\"/>")
          .Append("<path d=\"M239 36 A109 109 0 1 1 126 222 A109 109 0 0 1 239 36Z\" fill=\"url(#tunnel-shell)\"/>")
          .Append("<ellipse cx=201 cy=151 rx=58 ry=65 fill=\"#f6fbf8\"/>")
          .Append("<path d=\"M224 89 C139 91 138 215 224 214 C174 204 165 104 224 89Z\" fill=\"#152832\"/>")
          .Append("<path d=\"M180 98 C145 123 151 193 188 207\" fill=none stroke=\"#0caa79\" stroke-width=4 opacity=.6/>")
          .Append("<path class=tunnel-trail d=\"M273 158 Q342 144 397 177\" fill=none stroke=\"#08b982\" stroke-width=12 stroke-linecap=round opacity=0/>")
          .Append("</svg></div><div id=tunnel-token class=tunnel-token hidden aria-hidden=true><span class=tunnel-token-icon></span><span class=tunnel-token-name></span></div></div>");
        sb.Append("<p id=tunnel-status class=tunnel-status role=status aria-live=polite aria-atomic=true>")
          .Append(E(T("Выберите программу для добавления", "Choose an app to add"))).Append("</p>")
          .Append("<div class=tunnel-choose><button type=button id=tunnel-choose class=big hidden>").Append(E(T("Выбрать программу", "Choose an app")))
          .Append("</button><a class=\"button native-pick\" href=\"/apps/pick\">").Append(E(T("Выбрать файл на компьютере", "Choose a file on this computer")))
          .Append("</a></div><p id=tunnel-drop-help class=\"hint tunnel-drop-help\">")
          .Append(E(T("Сохранение не включает VPN. Файл с рабочего стола подсказывает только имя: подтвердите программу из каталога или используйте выбор файла.",
              "Saving does not turn on VPN. A desktop file supplies only a name hint: confirm a catalog app or use the file picker."))).Append("</p>")
          .Append("<ol class=tunnel-steps aria-label=\"").Append(E(T("Этапы добавления", "Adding steps"))).Append("\"><li data-tunnel-step=save>")
          .Append(E(T("Сохранение", "Save"))).Append("</li><li data-tunnel-step=apply>").Append(E(T("Применение", "Apply")))
          .Append("</li><li data-tunnel-step=verify>").Append(E(T("Проверка трафика", "Check traffic"))).Append("</li></ol>")
          .Append("<div id=tunnel-next class=tunnel-next hidden><p id=tunnel-next-copy></p><form id=tunnel-apply-form method=post action=/apply>")
          .Append("<input type=hidden name=tab value=\"").Append(E(tab)).Append("\"><input type=hidden name=confirm_apply value=1>")
          .Append("<button>").Append(E(T("Применить и переподключить", "Apply and reconnect"))).Append("</button></form>")
          .Append("<a href=\"/?tab=state\" id=tunnel-start-link>").Append(E(T("Перейти к запуску VPN", "Go to VPN start"))).Append("</a></div>")
          .Append("<div class=tunnel-recovery><button id=tunnel-retry type=button class=ghost hidden>").Append(E(T("Проверить состояние", "Check status")))
          .Append("</button><button id=tunnel-retry-save type=button class=ghost hidden>").Append(E(T("Повторить сохранение", "Retry saving")))
          .Append("</button><a id=tunnel-existing-link href=\"#added-apps\" hidden>").Append(E(T("Показать программу", "Show app"))).Append("</a></div></div>");

        sb.Append("<div class=tunnel-catalog><div class=tunnel-catalog-head><h2>").Append(E(T("Каталог программ", "App catalog")))
          .Append("</h2><a href=\"/?tab=").Append(E(tab)).Append("\" class=small>").Append(E(T("Обновить", "Refresh"))).Append("</a></div>")
          .Append("<form id=tunnel-catalog-form class=app-pick method=post action=/apps/installed><input type=hidden name=tab value=\"").Append(E(tab))
          .Append("\"><input type=hidden name=intent value=tunnel><input type=hidden name=confirm_add value=1>")
          .Append("<label class=sr-only for=tunnel-search>").Append(E(T("Найти программу", "Find an app"))).Append("</label>")
          .Append("<input id=tunnel-search class=app-filter type=search autocomplete=off placeholder=\"").Append(E(T("Найти программу", "Find an app"))).Append("\">")
          .Append("<div class=app-groups><div class=\"app-grid tunnel-catalog-grid\">");
        foreach (var app in catalog)
        {
            var existing = AppIdentity.Find(cfg.Apps, app.Path);
            sb.Append("<button class=app-card type=submit name=path value=\"").Append(E(app.Path))
              .Append("\" data-tunnel-source");
            if (existing is not null) sb.Append(" data-existing-id=\"").Append(AppIdentity.Id(existing)).Append("\"");
            sb.Append(" data-path=\"").Append(E(app.Path)).Append("\" data-label=\"").Append(E(app.Name))
              .Append("\" data-name=\"").Append(E(app.Name.ToLowerInvariant())).Append("\" aria-label=\"")
              .Append(E((existing is null ? T("Добавить в VPN: ", "Add to VPN: ") : T("Уже добавлено: ", "Already added: ")) + app.Name)).Append("\" title=\"").Append(E(app.Path)).Append("\">");
            Icon(sb, app.Path, app.Name);
            sb.Append("<span class=app-name>").Append(E(app.Name)).Append("</span><span class=tunnel-grip aria-hidden=true>⠿</span><span class=tunnel-plus aria-hidden=true>")
              .Append(existing is null ? "+" : "✓").Append("</span></button>");
        }
        sb.Append("</div></div><p id=tunnel-no-results class=hint hidden>").Append(E(T("Совпадений нет. Выберите файл или укажите путь ниже.", "No matches. Choose a file or enter its path below.")))
          .Append("</p></form>");
        if (catalog.Count == 0)
            sb.Append("<p class=empty>").Append(E(T("Установленные программы не найдены. Используйте выбор файла или путь.", "No installed apps found. Use the file picker or a path."))).Append("</p>");
        sb.Append("<details class=tunnel-manual><summary>").Append(E(T("По пути или файлу", "By path or file"))).Append("</summary>")
          .Append("<div class=app-entry-grid><div class=app-entry><h3>").Append(E(T("Из установленных", "Installed apps"))).Append("</h3><p class=hint>")
          .Append(E(T("Выберите программу в каталоге выше. Добавленные программы остаются в списке.", "Choose an app in the catalog above. Added apps remain in the list."))).Append("</p></div>")
          .Append("<div class=app-entry><form class=\"row app-add\" id=tunnel-manual-form method=post action=/apps/add>")
          .Append("<input type=hidden name=tab value=\"").Append(E(tab)).Append("\"><input type=hidden name=intent value=tunnel><input type=hidden name=confirm_add value=1>")
          .Append("<label class=sr-only for=manual-app-path>").Append(E(T("Полный путь к программе", "Full app path"))).Append("</label>")
          .Append("<input id=manual-app-path name=path type=text required autocomplete=off placeholder=\"")
          .Append(E(Os.IsWindows ? @"C:\Apps\Program\program.exe" : Os.IsMac ? "/Applications/Program.app" : "/opt/program/program")).Append("\">")
          .Append("<button>").Append(E(T("Добавить в VPN", "Add to VPN"))).Append("</button><a class=\"ghost pick\" href=/apps/pick>")
          .Append(E(T("Выбрать файл", "Choose file"))).Append("</a></form><p class=hint>")
          .Append(E(T("Правило может включать папку программы и вспомогательные процессы. Область правила будет показана после сохранения.",
              "The rule may include the app folder and helper processes. Its scope is shown after saving."))).Append("</p></div></div></details></div></section>");
        RenderConfirmation(sb, cfg, tab);
        if (!string.IsNullOrWhiteSpace(pickedPath))
        {
            sb.Append("<section id=tunnel-picked class=tunnel-picked data-path=\"").Append(E(pickedPath)).Append("\"><h2>")
              .Append(E(T("Добавить выбранную программу?", "Add the selected app?"))).Append("</h2><p class=path>").Append(E(pickedPath)).Append("</p><p>")
              .Append(E(T("Программа будет сохранена в правилах. Запущенный VPN нужно применить отдельно; это переподключит выбранные программы.",
                  "The app will be saved in the rules. Apply separately if VPN is running; this reconnects selected apps."))).Append("</p>")
              .Append("<form id=tunnel-picked-form method=post action=/apps/add><input type=hidden name=tab value=\"").Append(E(tab))
              .Append("\"><input type=hidden name=path value=\"").Append(E(pickedPath)).Append("\"><input type=hidden name=intent value=tunnel><input type=hidden name=confirm_add value=1>")
              .Append("<button>").Append(E(T("Добавить в VPN", "Add to VPN"))).Append("</button> <a href=\"/?tab=").Append(E(tab)).Append("\">")
              .Append(E(T("Отмена", "Cancel"))).Append("</a></form></section>");
        }
    }

    private static void Icon(StringBuilder sb, string path, string name) => sb.Append("<span class=ico data-letter=\"")
        .Append(E(WebServer.Initial(name))).Append("\"><img loading=lazy alt=\"\" src=\"/icon?path=")
        .Append(E(Uri.EscapeDataString(path))).Append("\" onerror=\"this.remove()\"></span>");

    private static void RenderConfirmation(StringBuilder sb, CehoConfig cfg, string tab)
    {
        string T(string ru, string en) => cfg.Language == "ru" ? ru : en;
        sb.Append("<dialog id=tunnel-confirm class=tunnel-confirm aria-labelledby=tunnel-confirm-title aria-describedby=tunnel-confirm-help>")
          .Append("<h2 id=tunnel-confirm-title>").Append(E(T("Добавить программу в VPN?", "Add this app to VPN?"))).Append("</h2>")
          .Append("<p id=tunnel-confirm-name></p><p id=tunnel-confirm-path class=path></p><p id=tunnel-confirm-help>")
          .Append(E(T("Сначала сохраним правило. Применение к запущенному VPN потребует отдельного действия и переподключит выбранные программы. Трафик проверяется после применения.",
              "First, save the rule. Applying it to a running VPN is a separate action and reconnects selected apps. Traffic is checked after applying."))).Append("</p>")
          .Append("<p id=tunnel-file-hint class=hint hidden></p><div id=tunnel-candidates></div>")
          .Append("<form id=tunnel-confirm-form method=post action=/apps/installed><input type=hidden name=tab value=\"").Append(E(tab))
          .Append("\"><input type=hidden name=intent value=tunnel><input type=hidden name=confirm_add value=1><input id=tunnel-confirm-path-input type=hidden name=path>")
          .Append("<div class=tunnel-dialog-actions><button id=tunnel-confirm-add>").Append(E(T("Добавить в VPN", "Add to VPN")))
          .Append("</button><button type=button class=ghost id=tunnel-cancel>").Append(E(T("Отмена", "Cancel")))
          .Append("</button><a class=pick href=/apps/pick>").Append(E(T("Выбрать другой файл", "Choose another file"))).Append("</a></div></form></dialog>");
    }

    public const string Css = """
    /* The approved desktop layout: tunnel left, catalog right, existing apps below. */
    .wrap:has(#tunnel-workspace){max-width:1480px}
    .tunnel-workspace{display:grid;grid-template-columns:minmax(0,1.35fr) minmax(320px,.9fr);gap:18px;align-items:stretch;margin:20px 0}
    .tunnel-panel,.tunnel-catalog{min-width:0;border:1px solid var(--line);border-radius:18px;background:var(--surface);padding:24px}
    .tunnel-panel{display:flex;flex-direction:column;align-items:center;text-align:center}
    .tunnel-panel h2{font-size:30px;line-height:1.2;letter-spacing:-.025em;align-self:flex-start;text-align:left;margin-bottom:8px}
    .tunnel-panel>.lede{align-self:flex-start;text-align:left;margin:0;color:var(--muted)}
    .tunnel-stage{position:relative;isolation:isolate;width:min(100%,500px);height:258px;margin:0 auto;border:2px solid transparent;border-radius:22px;transition:background .18s,border-color .18s}
    .tunnel-portal{position:absolute;inset:0;display:grid;place-items:center;pointer-events:none}
    .tunnel-portal svg{width:100%;height:100%;overflow:visible}
    .tunnel-rim{transition:filter .16s,opacity .16s;opacity:.75}
    .tunnel-stage[data-phase=hover]{border-color:color-mix(in srgb,var(--brand-ink) 30%,transparent);background:color-mix(in srgb,var(--brand-ink) 4%,transparent)}
    .tunnel-stage[data-phase=hover] .tunnel-rim{filter:drop-shadow(0 0 7px #08b98260);opacity:1}
    .tunnel-stage[data-phase=hover] .tunnel-trail{opacity:.09}
    .tunnel-token{position:absolute;top:48%;left:57%;width:160px;max-width:40%;min-height:54px;display:flex;align-items:center;gap:10px;padding:10px 12px;background:var(--surface);box-shadow:0 8px 20px #15283218;border:1px solid var(--line);border-radius:14px;z-index:1;text-align:left}
    .tunnel-token[hidden]{display:none}.tunnel-token .ico{margin:0;width:30px;height:30px}.tunnel-token-name{overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-size:14px;font-weight:600}
    .tunnel-token-icon:empty::before{content:'+';font-size:25px;color:var(--brand-ink)}
    .tunnel-stage[data-phase=applied] .tunnel-token{animation:tunnel-enter .55s ease-in both}
    @keyframes tunnel-enter{to{transform:translate(-90px,-16px) scale(.3);opacity:0}}
    .tunnel-status{font-weight:550;min-height:25px;max-width:56ch;margin:0 0 12px;font-size:14px}
    .tunnel-stage[data-phase=error]~.tunnel-status,.tunnel-stage[data-phase=unknown]~.tunnel-status{color:var(--warn-ink)}
    .tunnel-choose{display:flex;gap:10px;flex-wrap:wrap;justify-content:center}.tunnel-choose .button{margin:0;font-size:14px}
    .tunnel-choose:has(#tunnel-choose:not([hidden]))>.native-pick{background:transparent;border:1px solid var(--line);color:var(--subtext);font-weight:500}
    .tunnel-drop-help{font-size:12px;max-width:65ch;margin-top:12px}.tunnel-steps{display:flex;justify-content:center;gap:22px;padding:0;margin:20px 0 0;list-style:none;font-size:12px;color:var(--muted);counter-reset:tunnel-step}
    .tunnel-steps li{counter-increment:tunnel-step;display:flex;align-items:center;gap:7px}.tunnel-steps li::before{content:counter(tunnel-step);display:grid;place-items:center;border:1px solid var(--line);width:23px;height:23px;border-radius:50%;flex:none}
    .tunnel-steps li.current{color:var(--text);font-weight:600}.tunnel-steps li.current::before{border-color:var(--brand-ink)}.tunnel-steps li.complete::before{content:'✓';color:var(--brand-ink);border-color:var(--brand-ink)}
    .tunnel-next{border:1px solid var(--line);border-radius:12px;background:var(--panel);padding:12px;margin-top:16px;font-size:14px}.tunnel-next p{margin-bottom:8px}.tunnel-next form{margin:0}
    .tunnel-recovery{display:flex;gap:10px;flex-wrap:wrap;align-items:center;justify-content:center;font-size:14px;margin-top:8px}
    .tunnel-catalog-head{display:flex;align-items:center;justify-content:space-between;gap:12px}.tunnel-catalog-head h2{margin:0}.tunnel-catalog-head a{font-size:12px;color:var(--muted)}
    .tunnel-catalog .app-pick{margin-top:18px}.tunnel-catalog .app-groups{max-height:340px;margin-top:14px}.tunnel-catalog-grid{grid-template-columns:1fr;gap:10px}
    .tunnel-catalog button.app-card{min-height:70px;border-radius:12px;padding:12px 16px;gap:13px;font-size:16px}
    .tunnel-catalog .ico{width:38px;height:38px;margin:0;filter:none!important;background:transparent}
    .tunnel-catalog .app-name{flex:1}.tunnel-grip{color:var(--muted);font-size:23px}.tunnel-plus{display:grid;place-items:center;width:30px;height:30px;border:1px solid var(--brand-ink);border-radius:50%;color:var(--brand-ink);font-size:24px;line-height:1;flex:none}
    .tunnel-catalog button[draggable=true]{cursor:grab}.tunnel-catalog button[draggable=true]:active{cursor:grabbing}.tunnel-catalog button[data-existing-id] .tunnel-plus{font-size:16px}
    .tunnel-manual{font-size:14px;border-top:1px solid var(--line);padding-top:14px;margin-top:16px}.tunnel-manual summary{cursor:pointer;color:var(--subtext)}
    .tunnel-manual .app-entry-grid{grid-template-columns:1fr;gap:14px;margin-top:14px;padding-top:0;border:0}.tunnel-manual .app-entry .hint{min-height:0}.tunnel-manual input{min-width:0!important;width:100%}.tunnel-manual form{display:flex}
    .tunnel-confirm{width:min(560px,calc(100% - 28px));padding:26px;border:1px solid var(--line);border-radius:18px;background:var(--surface);color:var(--text);box-shadow:0 20px 80px #0003}
    .tunnel-confirm::backdrop{background:#0c191a77}.tunnel-confirm[open]{display:block}.tunnel-confirm .path{overflow-wrap:anywhere;color:var(--muted);font-size:13px}
    #tunnel-confirm-name{font-weight:650;font-size:20px}#tunnel-candidates{display:grid;gap:8px;margin:12px 0}#tunnel-candidates:empty{display:none}#tunnel-candidates button{text-align:left}
    .tunnel-dialog-actions{display:flex;align-items:center;gap:10px;flex-wrap:wrap}.tunnel-picked{border:1px solid var(--brand-ink);padding:20px;border-radius:14px;background:var(--surface)}
    .tunnel-added-title{margin-top:26px}article.app-card:focus{outline:2px solid var(--brand-ink);outline-offset:3px}.app-card.tunnel-highlight{box-shadow:0 0 0 3px color-mix(in srgb,var(--brand-ink) 24%,transparent)}
    body.panel-stale .tunnel-steps li.complete::before{color:var(--muted);border-color:var(--line)}
    [hidden]{display:none!important}
    @media(min-width:1200px){.wrap:has(#tunnel-workspace) header{margin-bottom:0}.tunnel-panel h2{font-size:34px}}
    @media(max-width:900px){.tunnel-workspace{grid-template-columns:1fr}.tunnel-stage{height:240px}.tunnel-catalog .app-groups{max-height:320px}.tunnel-catalog-grid{grid-template-columns:repeat(2,minmax(0,1fr))}.tunnel-panel h2{font-size:27px}}
    @media(max-width:540px){.tunnel-panel,.tunnel-catalog{padding:18px}.tunnel-panel h2{font-size:24px}.tunnel-catalog-grid{grid-template-columns:1fr}.tunnel-stage{height:220px}.tunnel-steps{gap:10px}.tunnel-steps li{gap:4px}.tunnel-token{left:55%;padding:8px;gap:6px;width:140px}.tunnel-choose{width:100%}.tunnel-choose>*{width:100%;justify-content:center}.tunnel-grip{display:none}}
    @media(prefers-reduced-motion:reduce){.tunnel-stage,.tunnel-rim{transition:none}.tunnel-stage[data-phase=applied] .tunnel-token{animation:none;opacity:0}.tunnel-trail{display:none}}
    """;
}
