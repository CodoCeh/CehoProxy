namespace ProxyCage.Core;

/// <summary>
/// Стили панели. Токены КодоЦеха, обе темы, без теней и кикеров, статусы только переменными.
/// Страница обязана работать без JavaScript — это панель управления сетью, и открываться
/// она должна как раз тогда, когда что-то сломано. Поэтому вся анимация на CSS.
///
/// Пояснения к решениям держим здесь, а не комментариями внутри CSS: он уходит клиенту
/// с каждой страницей, и в английской версии русские комментарии выглядели как мусор.
/// <list type="bullet">
/// <item>rise — появление секций: один спокойный жест сверху вниз, без кувырков.</item>
/// <item>halo — пульсация только у состояния «проверяю»: движение здесь означает ожидание,
/// а не украшение.</item>
/// <item>.logo и .forged — марка в скруглённом контейнере с подложкой и тонкой рамкой,
/// как на codoceh.ru: тёмная картинка на тёмном фоне иначе тонет.</item>
/// </list>
/// </summary>
public static class WebUi
{
    public const string Css = """
    *,*::before,*::after{box-sizing:border-box}
    :root{
      --bg:#f4f7f5; --surface:#ffffff; --text:#17251e; --subtext:#3c4f46; --muted:#5c6a61;
      --brand-ink:#2e8c66; --brand-strong:#267354;
      --ok-ink:#1f7a5a; --warn-ink:#836609; --danger-ink:#b33a31; --info-ink:#1c6f9b;
      --line:rgba(23,37,30,.14); --panel:rgba(23,37,30,.04); --panel2:rgba(23,37,30,.07);
      --radius:14px;
    }
    @media (prefers-color-scheme:dark){
      :root{
        --bg:#101512; --surface:#161d19; --text:#e8ede9; --subtext:#c7d6cd; --muted:#83978b;
        --brand-ink:#4bb98a; --brand-strong:#349f74;
        --ok-ink:#4fc39a; --warn-ink:#e0a82e; --danger-ink:#e8695e; --info-ink:#5cb8e8;
        --line:rgba(232,237,233,.16); --panel:rgba(232,237,233,.05); --panel2:rgba(232,237,233,.09);
      }
    }
    html{-webkit-text-size-adjust:100%}
    body{
      margin:0; background:var(--bg); color:var(--text);
      font-family:"Segoe UI",system-ui,-apple-system,"Helvetica Neue",sans-serif;
      font-size:15px; line-height:1.55;
    }
    .wrap{max-width:900px;margin:0 auto;padding:28px 20px 72px}

    @keyframes rise{from{opacity:0;transform:translateY(8px)}to{opacity:1;transform:none}}
    section,header,footer{animation:rise .45s cubic-bezier(.2,.7,.3,1) both}
    section:nth-of-type(1){animation-delay:.03s} section:nth-of-type(2){animation-delay:.06s}
    section:nth-of-type(3){animation-delay:.09s} section:nth-of-type(4){animation-delay:.12s}
    section:nth-of-type(5){animation-delay:.15s} section:nth-of-type(6){animation-delay:.18s}
    @media (prefers-reduced-motion:reduce){
      section,header,footer{animation:none}
      *{transition:none!important}
    }

    header{display:flex;align-items:center;gap:12px;flex-wrap:wrap;
      padding-bottom:18px;border-bottom:1px solid var(--line);margin-bottom:8px}
    .logo{width:34px;height:34px;border-radius:10px;flex:none;display:block;object-fit:cover;
      background:var(--panel);box-shadow:inset 0 0 0 1px var(--line)}
    .mark{font-size:19px;font-weight:680;letter-spacing:-.015em}
    .mark span{color:var(--brand-ink)}
    .where{color:var(--muted);font-size:13px;margin-left:auto;font-variant-numeric:tabular-nums}
    nav.tabs{display:flex;gap:2px;flex-wrap:wrap;margin:0 0 20px;padding-top:14px}
    nav.tabs a{padding:7px 13px;border-radius:9px;color:var(--subtext);text-decoration:none;
      font-size:14px;transition:background .18s ease,color .18s ease}
    nav.tabs a:hover{background:var(--panel2);color:var(--text)}
    nav.tabs a.on{background:var(--brand-ink);color:#f4fbf7;font-weight:600}

    h2{font-size:16px;font-weight:660;margin:0 0 10px;letter-spacing:-.01em}
    *+h2{margin-top:26px}
    section{margin:0 0 30px}
    p{margin:0 0 12px}
    .lede{color:var(--subtext);max-width:66ch}
    .hint{color:var(--muted);font-size:13px;margin:6px 0 0;max-width:70ch}

    .status{display:flex;align-items:center;gap:12px;padding:16px 18px;border:1px solid var(--line);
      border-radius:var(--radius);background:var(--surface);margin-bottom:12px}
    .dot{width:10px;height:10px;border-radius:50%;flex:none;position:relative}
    .on .dot{background:var(--ok-ink)} .off .dot{background:var(--muted)}
    .bad .dot{background:var(--danger-ink)} .wait .dot{background:var(--warn-ink)}
    @keyframes halo{0%{box-shadow:0 0 0 0 rgba(224,168,46,.55)}70%{box-shadow:0 0 0 9px rgba(224,168,46,0)}100%{box-shadow:0 0 0 0 rgba(224,168,46,0)}}
    .wait .dot{animation:halo 1.8s ease-out infinite}
    .status b{font-weight:640}
    .status .detail{color:var(--muted);font-size:13px;margin-left:auto;text-align:right;
      font-variant-numeric:tabular-nums}

    table{width:100%;border-collapse:collapse;margin:6px 0 10px;font-size:14px}
    th,td{text-align:left;padding:10px 10px;border-bottom:1px solid var(--line);vertical-align:middle}
    th{font-size:12px;font-weight:600;color:var(--muted)}
    tbody tr{transition:background .15s ease}
    tbody tr:hover{background:var(--panel)}
    td.path{font-family:ui-monospace,Consolas,"SF Mono",monospace;font-size:12.5px;
      color:var(--subtext);word-break:break-all}
    td.num{font-variant-numeric:tabular-nums}
    .tag{font-size:12px;color:var(--info-ink)}
    .flag{font-size:15px}
    .empty{padding:20px;border:1px dashed var(--line);border-radius:var(--radius);
      color:var(--muted);background:var(--panel)}

    form.row{display:flex;gap:8px;flex-wrap:wrap;align-items:flex-start;margin:10px 0 4px}
    input[type=text],input[type=password],input[type=number],select{
      flex:1;min-width:200px;padding:10px 12px;border:1px solid var(--line);border-radius:10px;
      background:var(--surface);color:var(--text);font:inherit;font-size:14px;
      transition:border-color .18s ease}
    input:hover,select:hover{border-color:var(--brand-ink)}
    input:focus-visible,select:focus-visible,button:focus-visible,a:focus-visible{
      outline:2px solid var(--brand-ink);outline-offset:2px}
    button{padding:10px 18px;border:1px solid var(--brand-ink);border-radius:10px;
      background:var(--brand-ink);color:#f4fbf7;font:inherit;font-size:14px;font-weight:600;
      cursor:pointer;min-height:40px;transition:transform .12s ease,background .18s ease}
    button:hover{background:var(--brand-strong)}
    button:active{transform:translateY(1px)}
    button.ghost{background:transparent;color:var(--text);border-color:var(--line);font-weight:500}
    button.ghost:hover{background:var(--panel2)}
    button.danger{background:transparent;color:var(--danger-ink);border-color:var(--line);font-weight:500}
    button.danger:hover{background:rgba(179,58,49,.09)}
    form.stack{display:flex;flex-direction:column;align-items:flex-start;gap:10px;margin:12px 0}
    form.stack .field{width:100%;max-width:560px}
    form.stack .hint{margin:0}
    button[disabled]{background:transparent;color:var(--muted);border-color:var(--line);
      font-weight:500;cursor:not-allowed}
    button[disabled]:hover{background:transparent}
    button[disabled]:active{transform:none}
    label.check{display:inline-flex;align-items:center;gap:8px;min-height:40px;cursor:pointer}
    label.field{display:inline-flex;align-items:center;gap:8px;min-height:40px}
    label.field span{color:var(--muted);font-size:13px;white-space:nowrap}
    label.field input{width:9ch}
    input[type=checkbox]{accent-color:var(--brand-ink);width:17px;height:17px;cursor:pointer}
    td:last-child{white-space:nowrap;text-align:right}
    .actions{display:flex;gap:6px;flex-wrap:wrap;justify-content:flex-end}
    .actions form{margin:0}
    .actions button{min-height:34px;padding:7px 14px}

    .flash{padding:12px 15px;border:1px solid var(--line);border-radius:var(--radius);
      margin-bottom:16px;background:var(--surface)}
    .flash.err{border-color:var(--danger-ink);color:var(--danger-ink)}
    .flash.ok{border-color:var(--ok-ink);color:var(--ok-ink)}
    .flash b{display:block;margin-bottom:2px}

    ol.steps{margin:0;padding-left:20px;color:var(--subtext);max-width:70ch}
    ol.steps li{margin-bottom:8px}
    code,.mono{font-family:ui-monospace,Consolas,"SF Mono",monospace;font-size:12.5px;
      background:var(--panel2);padding:2px 6px;border-radius:6px}
    .kv{display:grid;grid-template-columns:auto 1fr;gap:8px 16px;margin:10px 0;
      font-family:ui-monospace,Consolas,"SF Mono",monospace;font-size:13px}
    .kv dt{color:var(--muted)} .kv dd{margin:0}

    footer{margin-top:44px;padding-top:18px;border-top:1px solid var(--line);
      color:var(--muted);font-size:13px;display:flex;gap:14px;align-items:center;
      flex-wrap:wrap;justify-content:space-between}
    .forged{display:inline-flex;align-items:center;gap:9px;color:var(--brand-ink);
      text-decoration:none;font-weight:600;min-height:26px;transition:opacity .18s ease}
    .forged:hover{opacity:.78}
    .foot-links{display:inline-flex;gap:14px;align-items:center;flex-wrap:wrap}
    .foot-links a{color:var(--muted);text-decoration:none;border-bottom:1px solid var(--line)}
    .foot-links a:hover{color:var(--brand-ink)}
    .forged img{width:28px;height:28px;border-radius:10px;display:block;object-fit:cover;
      background:var(--panel);box-shadow:inset 0 0 0 1px var(--line)}

    .gate{max-width:380px;margin:14vh auto 0;padding:26px;border:1px solid var(--line);
      border-radius:var(--radius);background:var(--surface);animation:rise .4s ease both}
    .gate .logo{margin-bottom:14px}
    .gate h1{font-size:18px;margin:0 0 6px;font-weight:660}
    """;
}
