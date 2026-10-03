namespace ProxyCage.Core;

public static class WebUi
{
    public const string Css = """
    *,*::before,*::after{box-sizing:border-box}
    :root{
      --bg:#f4f7f5; --surface:#ffffff; --text:#17251e; --subtext:#3c4f46; --muted:#5c6a61;
      --brand-ink:#267354; --brand-strong:#195e43; --button-bg:#176b49; --button-text:#ffffff;
      --ok-ink:#1f7a5a; --warn-ink:#836609; --danger-ink:#b33a31; --info-ink:#1c6f9b;
      --line:rgba(23,37,30,.14); --panel:rgba(23,37,30,.04); --panel2:rgba(23,37,30,.07);
      --radius:14px;
      --fs-xs:12px; --fs-s:14px; --fs-m:16px; --fs-l:20px; --fs-xl:28px;
    }
    @media (prefers-color-scheme:dark){
      :root:not([data-theme=light]){
        --bg:#101512; --surface:#161d19; --text:#e8ede9; --subtext:#c7d6cd; --muted:#83978b;
        --brand-ink:#65c99b; --brand-strong:#85d9b4; --button-bg:#65c99b; --button-text:#10291e;
        --ok-ink:#4fc39a; --warn-ink:#e0a82e; --danger-ink:#e8695e; --info-ink:#5cb8e8;
        --line:rgba(232,237,233,.16); --panel:rgba(232,237,233,.05); --panel2:rgba(232,237,233,.09);
      }
    }
    :root[data-theme=dark]{
        --bg:#101512; --surface:#161d19; --text:#e8ede9; --subtext:#c7d6cd; --muted:#83978b;
        --brand-ink:#65c99b; --brand-strong:#85d9b4; --button-bg:#65c99b; --button-text:#10291e;
        --ok-ink:#4fc39a; --warn-ink:#e0a82e; --danger-ink:#e8695e; --info-ink:#5cb8e8;
        --line:rgba(232,237,233,.16); --panel:rgba(232,237,233,.05); --panel2:rgba(232,237,233,.09);
    }
    html{-webkit-text-size-adjust:100%}
    body{
      margin:0; background:var(--bg); color:var(--text);
      font-family:"Segoe UI",system-ui,-apple-system,"Helvetica Neue",sans-serif;
      font-size:var(--fs-m); line-height:1.55;
    }
    /* Unstyled body links must remain readable in both themes; component rules below retain their colors. */
    a{color:var(--brand-ink);text-underline-offset:3px}
    a:hover{color:var(--brand-strong)}
    .wrap{max-width:900px;margin:0 auto;padding:28px 20px 72px}

    /* Появление только прозрачностью. Страница перерисовывается сама, пока идёт операция,
       и любой сдвиг на этих перерисовках выглядит как пляска строк. */
    @keyframes rise{from{opacity:0}to{opacity:1}}
    section,header,footer{animation:rise .3s ease both}
    body.busy section,body.busy header,body.busy footer{animation:none}
    @media (prefers-reduced-motion:reduce){
      section,header,footer{animation:none}
      *{transition:none!important}
    }

    header{display:flex;align-items:center;gap:12px;flex-wrap:wrap;
      padding-bottom:18px;border-bottom:1px solid var(--line);margin-bottom:8px}
    .logo{width:34px;height:34px;border-radius:10px;flex:none;display:block;object-fit:cover;
      background:var(--panel);box-shadow:inset 0 0 0 1px var(--line)}
    .product-logo{display:inline-block;width:44px;height:44px;flex:none}
    .product-logo .logo{width:100%;height:100%;background:none;box-shadow:none;object-fit:contain;border-radius:0}
    .product-logo .logo-dark{display:none}
    :root[data-theme=dark] .product-logo .logo-light{display:none}
    :root[data-theme=dark] .product-logo .logo-dark{display:block}
    @media(prefers-color-scheme:dark){:root:not([data-theme=light]) .product-logo .logo-light{display:none}:root:not([data-theme=light]) .product-logo .logo-dark{display:block}}
    .gate .product-logo{width:52px;height:52px;margin-bottom:14px}
    @media(max-width:600px){.product-logo{width:36px;height:36px}}
    .mark{font-size:var(--fs-l);font-weight:680;letter-spacing:-.015em}
    .mark span{color:var(--brand-ink)}
    .where{color:var(--muted);font-size:var(--fs-s);margin-left:auto;font-variant-numeric:tabular-nums}
    header form.mode{margin:0 0 0 auto;display:inline-flex;align-items:center;gap:9px;font-size:var(--fs-s);color:var(--subtext)}
    header form.mode .mode-toggle{position:relative;display:inline-block;width:44px;height:26px;min-height:26px;
      padding:0;border:1px solid var(--line);border-radius:99px;background:var(--panel2);flex:none}
    header form.mode .mode-toggle::after{content:"";position:absolute;width:20px;height:20px;top:2px;left:2px;
      background:var(--surface);border-radius:50%;box-shadow:0 1px 3px #0003;transition:transform .18s ease}
    header form.mode .mode-toggle[aria-checked=true]{background:var(--brand-ink)}
    header form.mode .mode-toggle[aria-checked=true]::after{transform:translateX(18px)}
    header form.mode .selected{color:var(--text);font-weight:600}
    header button.theme{min-height:34px;min-width:34px;padding:6px 12px;border:1px solid var(--line);border-radius:999px;
      background:var(--panel);color:var(--subtext);font-size:var(--fs-s);font-weight:500}
    header button.theme{display:inline-flex;align-items:center;justify-content:center;padding:6px;width:34px}
    header button.theme svg{display:none}
    header button.theme[data-now=dark] .ico-sun,header button.theme[data-now=light] .ico-moon{display:block}
    header button.theme:hover{background:var(--panel2);color:var(--text)}
    nav.tabs{display:flex;gap:2px;flex-wrap:wrap;margin:0 0 20px;padding-top:14px}
    nav.tabs a{display:inline-flex;align-items:center;min-height:44px;padding:9px 13px;border-radius:9px;color:var(--subtext);text-decoration:none;
      font-size:var(--fs-s);transition:background .18s ease,color .18s ease}
    nav.tabs a:hover{background:var(--panel2);color:var(--text)}
    nav.tabs a.on{background:color-mix(in srgb,var(--brand-ink) 12%,var(--surface));color:var(--brand-ink);font-weight:600}
    nav.tabs details.more{position:relative}
    nav.tabs details.more summary{display:inline-flex;align-items:center;min-height:44px;padding:9px 13px;border-radius:9px;
      color:var(--subtext);cursor:pointer;list-style:none}
    nav.tabs details.more summary::-webkit-details-marker{display:none}
    nav.tabs details.more summary::after{content:"▾";margin-left:6px;font-size:var(--fs-xs)}
    nav.tabs details.more summary.on{background:var(--button-bg);color:var(--button-text);font-weight:600}
    nav.tabs details.more > div{position:absolute;left:0;z-index:5;display:flex;flex-direction:column;min-width:160px;
      margin-top:4px;padding:6px;border:1px solid var(--line);border-radius:var(--radius);background:var(--bg)}

    h2{font-size:var(--fs-l);font-weight:660;margin:0 0 10px;letter-spacing:-.01em}
    h3{font-size:var(--fs-m);font-weight:660;margin:0 0 4px;letter-spacing:-.005em}
    *+h2{margin-top:26px}
    section{margin:0 0 30px}
    p{margin:0 0 12px}
    .lede{color:var(--subtext);max-width:66ch}
    .hint{color:var(--muted);font-size:var(--fs-s);margin:6px 0 0;max-width:70ch}
    .status{display:flex;align-items:center;gap:12px;padding:16px 18px;border:1px solid var(--line);
      border-radius:var(--radius);background:var(--surface);margin-bottom:12px}
    .dot{width:10px;height:10px;border-radius:50%;flex:none;position:relative}
    .on .dot{background:var(--ok-ink)} .off .dot{background:var(--muted)}
    .bad .dot{background:var(--danger-ink)} .wait .dot{background:var(--warn-ink)}
    @keyframes halo{0%{box-shadow:0 0 0 0 rgba(224,168,46,.55)}70%{box-shadow:0 0 0 9px rgba(224,168,46,0)}100%{box-shadow:0 0 0 0 rgba(224,168,46,0)}}
    .wait .dot{animation:halo 1.8s ease-out infinite}
    .status b{font-weight:640}
    .status .detail{color:var(--muted);font-size:var(--fs-s);margin-left:auto;text-align:right;
      font-variant-numeric:tabular-nums}
    .warn .dot{background:var(--warn-ink)}
    .hero{display:flex;align-items:center;gap:16px;flex-wrap:wrap;padding:22px 24px;border:1px solid var(--line);
      border-radius:var(--radius);background:var(--surface)}
    .hero > .dot{width:14px;height:14px}
    .hero > div{flex:1 1 240px;min-width:0}
    .hero h1{margin:0;font-size:var(--fs-xl);line-height:1.3;font-weight:650}
    .hero p{margin:4px 0 0;color:var(--muted);font-size:var(--fs-s)}
    .hero form{display:flex;gap:8px;flex-wrap:wrap}
    .hero.on{border-color:color-mix(in srgb,var(--ok-ink) 45%,var(--line))}
    .hero.warn{border-color:color-mix(in srgb,var(--warn-ink) 45%,var(--line))}
    .hero.bad{border-color:color-mix(in srgb,var(--danger-ink) 45%,var(--line))}
    section.wizard{padding:22px 24px;border:1px solid color-mix(in srgb,var(--brand-ink) 45%,var(--line));
      border-radius:var(--radius);background:var(--surface)}
    section.wizard h2{margin-top:6px}
    ol.wizard-steps{display:flex;gap:8px;flex-wrap:wrap;list-style:none;margin:0 0 12px;padding:0;counter-reset:step}
    ol.wizard-steps li{counter-increment:step;display:inline-flex;align-items:center;gap:6px;color:var(--muted);font-size:var(--fs-s)}
    ol.wizard-steps li::before{content:counter(step);display:inline-flex;align-items:center;justify-content:center;
      width:24px;height:24px;border-radius:50%;border:1px solid var(--line);font-size:var(--fs-xs)}
    ol.wizard-steps li.now{color:var(--text);font-weight:600}
    ol.wizard-steps li.now::before{background:var(--brand-ink);border-color:var(--brand-ink);color:#fff}
    ol.wizard-steps li.done::before{content:"✓";color:var(--ok-ink);border-color:var(--ok-ink)}
    ol.wizard-steps li + li::after{content:none}
    .save-bar{position:sticky;bottom:0;padding:12px 0;background:var(--bg);border-top:1px solid var(--line);margin-top:18px}
    button.big{min-height:48px;padding:12px 28px;font-size:var(--fs-m);font-weight:600}
    ul.live-apps,ul.findings{list-style:none;margin:0;padding:0;border:1px solid var(--line);border-radius:var(--radius);
      background:var(--surface)}
    ul.live-apps li,ul.findings li{display:flex;align-items:center;gap:12px;flex-wrap:wrap;padding:12px 16px;
      border-bottom:1px solid var(--line)}
    ul.findings li{display:block;font-size:var(--fs-s);color:var(--subtext)}
    ul.live-apps li:last-child,ul.findings li:last-child{border-bottom:0}
    ul.live-apps .detail{margin-left:auto;color:var(--muted);font-size:var(--fs-s)}
    ul.live-apps li.bad .detail{color:var(--danger-ink)}
    ul.live-apps li.warn .detail{color:var(--warn-ink)}
    ul.live-apps .ico{margin-right:0}
    ul.findings{margin-top:10px}
    details.more-findings > summary{margin-top:10px;cursor:pointer;color:var(--subtext);font-size:var(--fs-s)}
    .lines{display:flex;flex-direction:column;gap:8px}
    .line{display:flex;align-items:center;gap:10px;flex-wrap:wrap;padding:10px 16px;border:1px solid var(--line);
      border-radius:var(--radius);background:var(--surface)}
    .line > span:first-child{flex:1 1 200px;display:inline-flex;align-items:center;gap:8px}
    .line form{margin:0}
    .line button{min-height:36px;padding:7px 14px;white-space:nowrap}
    a.button{display:inline-flex;align-items:center;min-height:44px;padding:10px 18px;border-radius:10px;
      background:var(--button-bg);color:var(--button-text);text-decoration:none;font-weight:600;margin-top:4px}
    .flash a{color:inherit;text-decoration:underline;text-underline-offset:2px;font-weight:600}
    details.settings{margin:0 0 30px;border:1px solid var(--line);border-radius:var(--radius);background:var(--surface)}
    details.settings > summary{padding:14px 18px;cursor:pointer;font-weight:600}
    details.settings > section{margin:0;padding:0 18px 18px}

    /* Ширины столбцов задаём сами: иначе браузер считает их от содержимого,
       и на каждом обновлении страницы черты столбцов уезжают в сторону. */
    table{width:100%;border-collapse:collapse;margin:6px 0 10px;font-size:var(--fs-s);table-layout:fixed}
    th,td{text-align:left;padding:10px 10px;border-bottom:1px solid var(--line);vertical-align:middle;
      overflow-wrap:anywhere}
    /* Заголовки переносим только по пробелу: разорванное посередине слово читается как опечатка. */
    th{font-size:var(--fs-xs);font-weight:600;color:var(--muted);overflow-wrap:normal}
    table.t-apps th:nth-child(1){width:25%}
    table.t-apps th:nth-child(3){width:198px}
    table.t-own th:nth-child(1){width:auto}
    table.t-own th:nth-child(2){width:300px}
    table.t-apps td,table.t-own td{vertical-align:top}
    table.t-subs th:nth-child(1){width:118px}
    table.t-subs th:nth-child(3){width:108px}
    table.t-subs th:nth-child(4){width:112px}
    table.t-subs th:nth-child(5){width:21%}
    table.t-subs th:nth-child(6){width:124px}
    table.t-countries th:nth-child(1){width:118px}
    table.t-countries th:nth-child(3){width:74px}
    table.t-countries th:nth-child(4){width:88px}
    table.t-countries th:nth-child(5){width:116px}
    table.t-countries th:nth-child(6){width:24%}
    table.t-nodes th:nth-child(1){width:118px}
    table.t-nodes th:nth-child(3){width:26%}
    table.t-nodes th:nth-child(4){width:98px}
    table.t-nodes th:nth-child(5){width:116px}
    table.t-nodes th:nth-child(6){width:15%}
    /* На узком экране заданные ширины не влезают. Тогда таблица едет вбок внутри своей
       обёртки, а не сминает столбцы до переноса по буквам. */
    .scroll{overflow-x:auto}
    @media (max-width:760px){table{min-width:720px}}
    @media (max-width:700px){
      table.t-apps,table.t-own{min-width:0}
      table.t-apps tr:has(th),table.t-own tr:has(th){display:none}
      table.t-apps tr,table.t-own tr{display:block;padding:12px 0;border-bottom:1px solid var(--line)}
      table.t-apps td,table.t-own td{display:block;border:0;padding:2px 0;width:auto}
      table.t-apps td.actions,table.t-own td.actions{text-align:left;padding-top:8px}
      table.t-apps td.actions form:first-child,table.t-own td.actions form:first-child{margin-left:0}
    }
    @media (max-width:900px){
      table.t-subs{min-width:0}
      table.t-subs tr:has(th){display:none}
      table.t-subs tr{display:grid;grid-template-columns:auto 1fr;gap:6px 14px;padding:12px 0;border-bottom:1px solid var(--line)}
      table.t-subs td{display:block;border:0;padding:0}
      table.t-subs td:nth-child(5),table.t-subs td.actions{grid-column:1/-1}
    }
    tbody tr{transition:background .15s ease}
    tbody tr:hover{background:var(--panel)}
    td.path{font-family:ui-monospace,Consolas,"SF Mono",monospace;font-size:var(--fs-xs);
      color:var(--subtext);word-break:break-all}
    td.num{font-variant-numeric:tabular-nums}
    .tag{font-size:var(--fs-xs);color:var(--info-ink)}
    .flag{font-size:var(--fs-m)}
    .empty{padding:20px;border:1px dashed var(--line);border-radius:var(--radius);
      color:var(--muted);background:var(--panel)}

    form.row{display:flex;gap:8px;flex-wrap:wrap;align-items:flex-start;margin:10px 0 4px}
    form.app-add input[type=text]{flex:1;min-width:220px}
    .app-entry-grid{display:grid;grid-template-columns:minmax(0,1fr) minmax(0,1fr);gap:28px;
      margin-top:24px;padding-top:22px;border-top:1px solid var(--line)}
    .app-entry{min-width:0}
    .app-entry h3{display:flex;align-items:center;justify-content:space-between;gap:10px}
    .app-entry h3 a.small{padding:4px 12px;border:1px solid var(--line);border-radius:10px;color:var(--text);
      text-decoration:none;font-weight:500;font-size:var(--fs-s)}
    .app-entry h3 a.small:hover{background:var(--panel2)}
    .app-entry .hint{min-height:40px;margin-top:0}
    form.app-add{align-items:stretch}
    form.app-pick{margin:10px 0 4px}
    form.app-pick input.app-filter{width:100%;min-width:0;padding:10px 12px;border:1px solid var(--line);
      border-radius:10px;background:var(--surface);color:var(--text);font:inherit;font-size:var(--fs-s)}
    .app-groups{max-height:420px;overflow:auto;margin-top:10px;padding:2px}
    .app-group+.app-group{margin-top:14px}
    .app-group[hidden]{display:none}
    .app-group-title{margin:0 0 6px;font-size:var(--fs-xs);font-weight:600;color:var(--muted);letter-spacing:.02em}
    .app-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(176px,1fr));gap:8px}
    button.app-card{display:flex;align-items:center;gap:10px;min-height:52px;padding:8px 10px;text-align:left;
      background:var(--surface);color:var(--text);border:1px solid var(--line);border-radius:10px;
      font-weight:500;overflow:hidden}
    button.app-card:hover{background:var(--panel2)}
    button.app-card[hidden]{display:none}
    .app-name{min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
    .ico{position:relative;flex:0 0 auto;width:28px;height:28px;margin-right:9px;border-radius:7px;
      background:var(--panel2);display:inline-flex;align-items:center;justify-content:center;
      vertical-align:middle;overflow:hidden}
    .ico::before{content:attr(data-letter);font-size:var(--fs-xs);font-weight:600;color:var(--muted)}
    .ico img{position:absolute;inset:0;width:100%;height:100%;object-fit:contain}
    td.named>span:first-of-type{font-weight:500;vertical-align:middle}
    .sr-only{position:absolute;width:1px;height:1px;padding:0;margin:-1px;overflow:hidden;
      clip:rect(0,0,0,0);white-space:nowrap;border:0}
    details.rename{margin-top:7px}
    details.rename summary{width:max-content;color:var(--brand-ink);font-size:var(--fs-xs);cursor:pointer}
    details.rename form.row{display:grid;grid-template-columns:minmax(0,1fr) auto;margin:7px 0 0}
    details.rename input[type=text]{min-width:0;width:100%;padding:7px 9px}
    details.rename button{min-height:34px;padding:7px 11px}
    a.pick{display:inline-flex;align-items:center;min-height:40px;padding:10px 18px;
      border:1px solid var(--line);border-radius:10px;color:var(--text);text-decoration:none;
      font-size:var(--fs-s);font-weight:500;white-space:nowrap;box-sizing:border-box}
    a.pick:hover{background:var(--panel2)}
    input[type=text],input[type=password],input[type=number],select{
      flex:1;min-width:200px;padding:10px 12px;border:1px solid var(--line);border-radius:10px;
      background:var(--surface);color:var(--text);font:inherit;font-size:var(--fs-s);
      transition:border-color .18s ease}
    input:hover,select:hover{border-color:var(--brand-ink)}
    input:focus-visible,select:focus-visible,button:focus-visible,a:focus-visible{
      outline:2px solid var(--brand-ink);outline-offset:2px}
    button{padding:10px 18px;border:1px solid var(--brand-ink);border-radius:10px;
      background:var(--button-bg);color:var(--button-text);font:inherit;font-size:var(--fs-s);font-weight:600;
      cursor:pointer;min-height:40px;transition:transform .12s ease,background .18s ease}
    button:hover{filter:brightness(.94)}
    button:active{transform:translateY(1px)}
    button.ghost{background:transparent;color:var(--text);border-color:var(--line);font-weight:500}
    button.ghost:hover{background:var(--panel2)}
    .filepick{position:relative;display:inline-flex;align-items:center;gap:10px;cursor:pointer;min-width:0}
    .filepick input[type=file]{position:absolute;inset:0;width:100%;height:100%;opacity:0;cursor:pointer}
    .filepick .fp-btn{display:inline-flex;align-items:center;min-height:40px;padding:10px 18px;border:1px solid var(--line);
      border-radius:10px;color:var(--text);font-size:var(--fs-s);font-weight:500;white-space:nowrap;transition:background .18s ease}
    .filepick:hover .fp-btn{background:var(--panel2)}
    .filepick:focus-within .fp-btn{outline:2px solid var(--brand-ink);outline-offset:2px}
    .filepick .fp-name{color:var(--muted);font-size:var(--fs-s);overflow:hidden;text-overflow:ellipsis;white-space:nowrap;max-width:240px}
    button.danger{background:transparent;color:var(--danger-ink);border-color:var(--line);font-weight:500}
    button.danger:hover{background:rgba(179,58,49,.09)}
    form.stack,div.stack{display:flex;flex-direction:column;align-items:flex-start;gap:10px;margin:12px 0}
    form.stack .field,div.stack .field{width:100%;max-width:560px}
    form.stack .hint,div.stack .hint{margin:0}
    .kind-switch{display:inline-flex;border:1px solid var(--line);border-radius:10px;
      overflow:hidden;background:var(--panel);margin:2px 0 4px}
    .kind-switch input{position:absolute;opacity:0;width:0;height:0;pointer-events:none}
    .kind-switch label{padding:9px 16px;font-size:var(--fs-s);font-weight:600;color:var(--muted);
      cursor:pointer;user-select:none;transition:background .15s ease,color .15s ease}
    .kind-switch input:checked+label{background:var(--button-bg);color:var(--button-text)}
    .kind-switch input:focus-visible+label{outline:2px solid var(--brand-ink);outline-offset:-2px}
    form.sub-add .sub-add-panel{display:none;width:100%;max-width:560px}
    form.sub-add:has(input[name=kind][value=sub]:checked) .sub-add-url{display:flex;flex-direction:column;gap:8px}
    form.sub-add:has(input[name=kind][value=naive]:checked) .sub-add-naive{display:flex;flex-direction:column;gap:8px}
    .tag.kind-naive{color:var(--brand-ink)}
    .tag.kind-sub{color:var(--info-ink)}
    dialog.sub-modal{border:1px solid var(--line);border-radius:var(--radius);padding:0;
      max-width:580px;width:calc(100% - 32px);background:var(--surface);color:var(--text);
      box-shadow:0 18px 48px rgba(15,20,17,.22)}
    dialog.sub-modal::backdrop{background:rgba(15,20,17,.42)}
    dialog.sub-modal .modal-title{margin:0;padding:18px 22px 0;font-size:var(--fs-l);font-weight:660}
    dialog.sub-modal form.stack{margin:12px 22px 20px}
    .modal-actions{display:flex;gap:8px;flex-wrap:wrap;margin-top:4px}
    .sub-modals{display:contents}
    button[disabled]{background:transparent;color:var(--muted);border-color:var(--line);
      font-weight:500;cursor:not-allowed}
    button[disabled]:hover{background:transparent}
    button[disabled]:active{transform:none}
    label.check{display:inline-flex;align-items:center;gap:8px;min-height:40px;cursor:pointer}
    label.field{display:inline-flex;align-items:center;gap:8px;min-height:40px;max-width:100%}
    label.field span{color:var(--muted);font-size:var(--fs-s);white-space:nowrap}
    @media (max-width:600px){label.field{flex-wrap:wrap}label.field span{white-space:normal}}
    /* Поле обязано ужиматься вместе с окном: минимальная ширина из общего правила
       вылезала за край страницы и тянула за собой полосу прокрутки. */
    label.field input{flex:1;min-width:0}
    label.field input[inputmode=numeric]{flex:none;width:9ch}
    input[type=checkbox]{accent-color:var(--brand-ink);width:17px;height:17px;cursor:pointer}
    td:last-child{white-space:nowrap;text-align:right}
    /* Ячейку с кнопками нельзя делать flex-контейнером: тогда она выпадает из табличной
       раскладки, перестаёт тянуться на высоту строки — и её черта висит выше соседних. */
    td.actions{text-align:right;white-space:nowrap}
    td.actions form{display:inline-block;margin:0 0 0 6px;vertical-align:middle}
    td.actions button{min-height:34px;padding:7px 14px}
    table.t-subs td.actions{white-space:normal}
    table.t-subs td.actions > *{display:block;width:100%;margin:0 0 6px}
    table.t-subs td.actions form button{width:100%}
    td.actions a.ghost{display:inline-block;margin:0 0 0 6px;vertical-align:middle;
      min-height:34px;padding:7px 14px;border:1px solid var(--line);border-radius:10px;
      color:var(--text);text-decoration:none;font-weight:500;font-size:var(--fs-s);line-height:18px;
      box-sizing:border-box}
    td.actions a.ghost:hover{background:var(--panel2)}
    a.btnlink{display:inline-flex;align-items:center;min-height:36px;padding:7px 14px;border:1px solid var(--line);
      border-radius:10px;color:var(--text);text-decoration:none;font-weight:500;font-size:var(--fs-s);box-sizing:border-box}
    a.btnlink:hover{background:var(--panel2)}
    .flash{padding:12px 15px;border:1px solid var(--line);border-radius:var(--radius);
      margin-bottom:16px;background:var(--surface)}
    .flash.err{border-color:var(--danger-ink);color:var(--danger-ink)}
    .flash.ok{border-color:var(--ok-ink);color:var(--ok-ink)}
    .pending{position:sticky;top:0;z-index:5;display:flex;gap:12px;align-items:center;justify-content:space-between;
      flex-wrap:wrap;padding:12px 15px;margin:0 0 12px;border:1px solid var(--warn-ink);border-radius:var(--radius);
      background:var(--bg);color:var(--warn-ink)}
    .toast{position:fixed;left:50%;bottom:16px;transform:translateX(-50%);z-index:20;display:flex;gap:12px;
      align-items:center;max-width:min(640px,calc(100vw - 32px));padding:12px 14px;border:1px solid var(--warn-ink);
      border-radius:var(--radius);background:var(--surface);color:var(--text);box-shadow:0 6px 24px rgba(0,0,0,.25);
      animation:toast-in .25s ease-out}
    .toast[hidden]{display:none}
    @keyframes toast-in{from{opacity:0;transform:translate(-50%,12px)}to{opacity:1;transform:translate(-50%,0)}}
    @media (prefers-reduced-motion:reduce){.toast{animation:none}}
    .flash.warn{border-color:var(--warn-ink);color:var(--warn-ink)}
    .flash b{display:block;margin-bottom:2px}
    .flash form{margin:10px 0 0}

    ol.steps,ul.steps{margin:0;padding-left:20px;color:var(--subtext);max-width:70ch}
    ol.steps li,ul.steps li{margin-bottom:8px}
    code,.mono{font-family:ui-monospace,Consolas,"SF Mono",monospace;font-size:var(--fs-xs);
      background:var(--panel2);padding:2px 6px;border-radius:6px}
    .kv{display:grid;grid-template-columns:190px 1fr;gap:8px 16px;margin:10px 0;
      font-family:ui-monospace,Consolas,"SF Mono",monospace;font-size:var(--fs-s)}
    .kv dt{color:var(--muted)} .kv dd{margin:0;overflow-wrap:anywhere}
    @media (max-width:520px){.kv{grid-template-columns:1fr;gap:2px 0}
      .kv dd{margin-bottom:8px}}
    @media (max-width:700px){.app-entry-grid{grid-template-columns:1fr;gap:22px}
      .app-entry .hint{min-height:0}}

    footer{margin-top:44px;padding-top:18px;border-top:1px solid var(--line);
      color:var(--muted);font-size:var(--fs-s);display:flex;gap:14px;align-items:center;
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
    .gate h1{font-size:var(--fs-l);margin:0 0 6px;font-weight:660}

    .job{padding:14px 16px;border:1px solid var(--line);border-radius:var(--radius);
      background:var(--surface);margin-bottom:18px}
    .job.ok{border-color:var(--ok-ink)} .job.err{border-color:var(--danger-ink)}
    .job-head{display:flex;align-items:baseline;gap:10px}
    .job-head b{font-weight:640}
    .job-num{margin-left:auto;color:var(--muted);font-size:var(--fs-s);font-variant-numeric:tabular-nums}
    .bar{height:8px;border-radius:99px;background:var(--panel2);overflow:hidden;margin:10px 0 8px}
    .bar>span{display:block;height:100%;border-radius:99px;background:var(--brand-ink)}
    .bar>#jf{width:100%;transform-origin:left;transition:transform .35s cubic-bezier(.2,.7,.3,1)}
    .job.run .bar>span{background:linear-gradient(90deg,var(--brand-ink),var(--brand-strong))}
    .job.err .bar>span{background:var(--danger-ink)}
    .bar.thin{height:5px;margin:5px 0 0;max-width:170px}
    /* Этап меняется каждые полсекунды. Разрешить ему перенос — значит дёргать вверх-вниз
       всё, что ниже, поэтому строка ровно одна, а длинное имя прячется за многоточие. */
    .job-stage{font-size:var(--fs-s);color:var(--subtext);min-height:21px;
      white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
    .job-foot{font-size:var(--fs-xs);color:var(--muted);margin-top:4px;min-height:19px}
    .job-foot a{color:var(--muted)}
    .job-steps{margin-top:8px;font-size:var(--fs-xs);color:var(--muted)}
    .job-steps summary{cursor:pointer}
    .job-steps ol{margin:6px 0 0;padding-left:22px}

    tr.dim td{opacity:.55}
    .tag.bad{color:var(--danger-ink)}
    .modes{display:flex;gap:8px;flex-wrap:wrap;align-items:center;margin:4px 0 12px}
    .modes form{margin:0}
    .modes span.mode-on{display:inline-flex;align-items:center;min-height:40px;padding:10px 18px;
      border:1px solid var(--brand-ink);border-radius:10px;background:var(--button-bg);
      color:var(--button-text);font-weight:600}
    button.pill{min-height:28px;padding:3px 12px;font-size:var(--fs-xs);font-weight:600;border-radius:99px}
    button.pill.no{background:transparent;color:var(--muted);border-color:var(--line);font-weight:500}
    button.pill.no:hover{background:var(--panel2)}
    .until{font-variant-numeric:tabular-nums}
    .until.warn{color:var(--warn-ink);font-weight:600}
    .until.danger{color:var(--danger-ink);font-weight:600}
    a.pill{display:inline-flex;align-items:center;min-height:28px;padding:3px 13px;font-size:var(--fs-xs);
      border:1px solid var(--line);border-radius:99px;color:var(--subtext);text-decoration:none;
      transition:background .18s ease,color .18s ease}
    a.pill:hover{background:var(--panel2);color:var(--text)}
    a.pill.on{background:var(--brand-ink);border-color:var(--brand-ink);color:#f4fbf7;font-weight:600}

    details.nodes{border:1px solid var(--line);border-radius:var(--radius);background:var(--surface);
      margin:0 0 8px;padding:0 14px}
    details.nodes>summary{cursor:pointer;padding:11px 0;font-size:var(--fs-s);font-weight:600}
    details.nodes>summary::marker{color:var(--muted)}
    details.nodes[open]>summary{border-bottom:1px solid var(--line)}
    details.nodes table{margin:0 0 6px}
    tr.off td{opacity:.5}

    ul.checks{list-style:none;margin:12px 0 0;padding:0;border:1px solid var(--line);
      border-radius:var(--radius);background:var(--surface)}
    ul.checks li{display:flex;gap:11px;padding:12px 15px;border-bottom:1px solid var(--line)}
    ul.checks li:last-child{border-bottom:0}
    ul.checks .mk{flex:none;width:16px;text-align:center;font-weight:700;line-height:1.5}
    ul.checks li.ok .mk{color:var(--ok-ink)}
    ul.checks li.warn .mk{color:var(--warn-ink)}
    ul.checks li.stop .mk{color:var(--danger-ink)}
    ul.checks b{font-weight:620;display:block}
    ul.checks .why{display:block;color:var(--muted);font-size:var(--fs-s)}
    ul.checks .why.can{color:var(--brand-ink)}
    ul.did{list-style:none;margin:10px 0 0;padding:0;color:var(--subtext);font-size:var(--fs-s)}
    ul.did li{padding:5px 0 5px 20px;position:relative}
    ul.did li::before{content:"✓";position:absolute;left:0;color:var(--ok-ink);font-weight:700}

    .ping-card{background:var(--surface);border:1px solid var(--line);border-radius:var(--radius);
      padding:16px;margin:16px 0}
    .ping-card h3{margin:0 0 6px}
    .ping-card>.hint{margin:0 0 12px}
    .ping-pairs{display:flex;gap:16px;flex-wrap:wrap}
    .ping-result{flex:1;min-width:240px;padding:9px 12px;background:var(--panel);
      border:1px solid var(--line);border-radius:10px}
    .ping-result.ok{border-color:var(--ok-ink)}
    .ping-result.bad{border-color:var(--danger-ink)}

    pre.logbox{margin:6px 0 4px;padding:12px 14px;border:1px solid var(--line);border-radius:var(--radius);
      background:var(--panel);color:var(--subtext);font-family:ui-monospace,Consolas,"SF Mono",monospace;
      font-size:var(--fs-xs);line-height:1.5;max-height:340px;overflow:auto;white-space:pre-wrap;word-break:break-word}
    .live-apps li.app-observation{display:grid;grid-template-columns:minmax(130px,1fr) minmax(220px,2fr) auto;gap:14px;align-items:start}
    .live-apps .configured-route,.live-apps .app-observation-body>p.hint{display:none}
    .live-apps li.bad .app-observation-body>p.hint{display:block}
    body.panel-stale .observation-badge.on,.live-stale .observation-badge.on{color:var(--muted);background:var(--panel)}
    @media(max-width:650px){.live-apps li.app-observation{grid-template-columns:1fr}.live-apps .app-observation-body>p.hint{display:block}}
    .app-cards{display:grid;gap:14px;margin:20px 0}
    article.app-card{display:grid;grid-template-columns:minmax(150px,1fr) minmax(240px,1.8fr) auto;gap:18px;align-items:start;
      padding:20px;border:1px solid var(--line);border-radius:16px;background:var(--surface)}
    .app-proof{color:var(--ok-ink);font-weight:600;margin:6px 0 0;font-size:var(--fs-s)}.country-form{margin:8px 0 0}.country-form label{display:flex;align-items:center;gap:8px;color:var(--muted);font-size:var(--fs-s)}.country-form select{width:auto;max-width:100%}.recommend{background:var(--panel2);border:1px solid var(--line);border-radius:var(--radius);padding:18px 20px;margin:16px 0}.recommend h2{margin:0 0 6px}
    details.node-help{margin-top:8px}details.node-help summary{cursor:pointer;color:var(--brand-ink);font-size:var(--fs-s)}.node-help-actions{display:flex;gap:8px;flex-wrap:wrap;margin:8px 0}.node-help-actions form{margin:0}
    .app-identity .dot{width:12px;height:12px}.app-card.on .app-observation-body>p.hint{display:none}
    .app-identity{display:flex;gap:12px;align-items:center;min-width:0}.app-identity h3{overflow-wrap:anywhere}
    .app-identity .ico{width:38px;height:38px;object-fit:contain;filter:none!important}
    .app-card-actions{display:flex;gap:10px;flex-wrap:wrap;align-items:center;max-width:190px;font-size:var(--fs-s)}
    .app-card-actions form{margin:0}.app-card-actions>details{width:100%}.app-card-actions summary{cursor:pointer;color:var(--muted)}
    .app-observation-body{min-width:0}.configured-route{color:var(--muted);font-size:var(--fs-s);margin:3px 0 8px}
    .app-card.bad{border-color:var(--danger-ink)}.app-card.warn{border-color:color-mix(in srgb,var(--warn-ink) 55%,var(--line))}
    button[aria-disabled=true]{opacity:.65;cursor:wait}
    @media(max-width:760px){article.app-card{grid-template-columns:1fr}.app-card-actions{max-width:none}.app-card-actions>details{width:auto}}
    .job{background:var(--surface);border-radius:18px;padding:22px 24px;box-shadow:0 4px 18px #00000004}
    .startup-steps{display:flex;gap:12px;justify-content:space-between;list-style:none;padding:0;margin:0 0 20px}
    .startup-steps[hidden]{display:none}.startup-steps li{display:flex;gap:8px;align-items:center;color:var(--muted);font-size:var(--fs-s)}
    .startup-steps .step-number{display:grid;place-items:center;width:28px;height:28px;border:1px solid var(--line);border-radius:50%;font-variant-numeric:tabular-nums}
    .startup-steps .current{color:var(--text);font-weight:600}.startup-steps .current .step-number{border-color:var(--brand-ink);box-shadow:0 0 0 4px color-mix(in srgb,var(--brand-ink) 12%,transparent)}
    .startup-steps .complete{color:var(--ok-ink)}.startup-steps .complete .step-number{background:var(--button-bg);color:var(--button-text);border-color:transparent}
    .job-stage{font-size:var(--fs-m);font-weight:600;line-height:1.45;margin:16px 0 6px;overflow-wrap:anywhere}
    .job-safety{font-size:var(--fs-xs)}.job-head{align-items:center}.job .bar{height:6px;margin:16px 0}
    .submit-status{font-size:var(--fs-s);color:var(--muted);flex-basis:100%;margin:6px 0;max-width:62ch}
    @media(max-width:480px){.job{padding:18px}.startup-steps{gap:6px}.startup-steps li{font-size:12px;gap:5px}.startup-steps .step-number{width:23px;height:23px}}
    /* Status is an observation, never an assurance of anonymity. */
    .wrap{max-width:1000px;padding-top:24px}
    header{padding-bottom:20px}.logo{width:44px;height:44px}.mark{font-size:24px;color:var(--text)}
    .mark span{color:inherit}.hero{padding:26px;border-radius:18px;box-shadow:0 3px 14px #00000004}
    .hero h1{font-size:32px;letter-spacing:-.025em}.hero .danger{background:var(--button-bg);color:var(--button-text);border-color:var(--button-bg)}
    .freshness{font-size:var(--fs-xs);color:var(--muted);display:flex;align-items:center;gap:10px;flex-wrap:wrap;margin:8px 0 16px}
    .freshness button{min-height:30px;padding:4px 10px;font-size:var(--fs-xs)}
    .freshness.stale{color:var(--warn-ink)}
    body.panel-stale [data-live] .hero.on{border-color:var(--warn-ink)}
    body.panel-stale [data-live] .on .dot{background:var(--muted)}
    .protection-summary,.wizard-result{border:1px solid var(--line);padding:16px 20px;border-radius:14px;background:var(--surface);margin:12px 0}
    .protection-summary.warn,.wizard-result.warn{border-color:var(--warn-ink)}
    .protection-summary.bad,.wizard-result.bad{border-color:var(--danger-ink)}
    .summary-counts{display:flex;flex-wrap:wrap;gap:12px}.scope-note,.observation-meta{color:var(--muted);font-size:var(--fs-xs)}
    .observation-badge{display:inline-flex;align-items:center;gap:6px;border-radius:99px;background:var(--panel);padding:4px 9px;font-size:var(--fs-xs)}
    .observation-badge.on{color:var(--ok-ink);background:color-mix(in srgb,var(--ok-ink) 10%,var(--surface))}
    .observation-badge.warn{color:var(--warn-ink)}.observation-badge.bad{color:var(--danger-ink)}
    .app-observation{display:flex;gap:8px;flex-direction:column;align-items:flex-start}
    .route-details{font-size:var(--fs-s);margin:8px 0}.route-details summary{cursor:pointer;color:var(--subtext)}
    .app-actions{display:flex;gap:8px;flex-wrap:wrap}.verification-checklist{padding-left:22px}
    .consequences{font-size:var(--fs-s);padding:12px;border-radius:10px;background:var(--panel);color:var(--subtext);margin:10px 0}
    .job-status{min-height:20px;font-size:var(--fs-s);color:var(--muted);margin-top:8px}
    .job-status.warn{color:var(--warn-ink)}.job-status.err{color:var(--danger-ink)}
    .bar.indeterminate>#jf{width:32%;transform:none!important;animation:job-wait 1.7s ease-in-out infinite}
    @keyframes job-wait{0%{margin-left:-32%}100%{margin-left:100%}}
    @media(prefers-reduced-motion:reduce){.bar.indeterminate>#jf{animation:none;margin-left:34%}.wait .dot{animation:none}}
    @media(max-width:600px){.wrap{padding:18px 14px 40px}.hero{padding:20px}.hero h1{font-size:26px}
      .mark{font-size:20px}.logo{width:36px;height:36px}header form.mode{font-size:12px;gap:6px}header{gap:8px}}
    """;

    /// <summary>
    /// Полоса двигается без перезагрузки страницы. Скриптов может не быть —
    /// тогда работает meta refresh, поэтому здесь только украшение, а не единственный путь.
    /// </summary>
    public const string SubModalScript = """
    <script>
    (function(){
      document.querySelectorAll('[data-sub-edit]').forEach(function(btn){
        btn.addEventListener('click',function(){
          var id=btn.getAttribute('data-sub-edit');
          var dlg=id&&document.getElementById(id);
          if(dlg&&typeof dlg.showModal==='function')dlg.showModal();
        });
      });
      document.querySelectorAll('dialog.sub-modal').forEach(function(dlg){
        dlg.querySelectorAll('[data-sub-close]').forEach(function(btn){
          btn.addEventListener('click',function(){dlg.close();});
        });
        dlg.addEventListener('click',function(e){if(e.target===dlg)dlg.close();});
      });
    })();
    </script>
    """;

    public const string ThemeEarlyScript =
        "<script>try{var t=localStorage.getItem('ceho-theme');if(t==='light'||t==='dark')document.documentElement.setAttribute('data-theme',t)}catch(e){}</script>";

    public const string ThemeIcons =
        "<svg class=ico-sun viewBox=\"0 0 24 24\" width=18 height=18 fill=none stroke=currentColor stroke-width=2 stroke-linecap=round aria-hidden=true>"
        + "<circle cx=12 cy=12 r=4 /><path d=\"M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4\" /></svg>"
        + "<svg class=ico-moon viewBox=\"0 0 24 24\" width=18 height=18 fill=none stroke=currentColor stroke-width=2 stroke-linecap=round stroke-linejoin=round aria-hidden=true>"
        + "<path d=\"M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z\" /></svg>";

    public const string ThemeScript = """
    <script>
    (function(){
      var btn=document.getElementById('theme');
      if(!btn)return;
      var mq=window.matchMedia('(prefers-color-scheme: dark)');
      function now(){return document.documentElement.getAttribute('data-theme')||(mq.matches?'dark':'light')}
      function paint(){
        var dark=now()==='dark';
        btn.setAttribute('data-now',dark?'dark':'light');
        var label=btn.getAttribute(dark?'data-light':'data-dark');
        btn.setAttribute('aria-label',label);btn.title=label;
      }
      btn.addEventListener('click',function(){
        var next=now()==='dark'?'light':'dark';
        document.documentElement.setAttribute('data-theme',next);
        try{localStorage.setItem('ceho-theme',next)}catch(e){}
        paint();
      });
      if(mq.addEventListener)mq.addEventListener('change',paint);
      paint();
    })();
    </script>
    """;

    public const string SettingsImportScript = """
    <script>
    (function(){
      var form=document.getElementById('settings-import');
      if(!form)return;
      var file=document.getElementById('settings-file'),data=document.getElementById('settings-data');
      var shown=form.querySelector('.fp-name');
      file.addEventListener('change',function(){
        if(shown)shown.textContent=file.files&&file.files[0]?file.files[0].name:shown.getAttribute('data-none');
      });
      form.addEventListener('submit',function(e){
        if(data.value)return;
        e.preventDefault();
        var f=file.files&&file.files[0];
        if(!f)return;
        var r=new FileReader();
        r.onload=function(){data.value=r.result;form.submit();};
        r.readAsText(f);
      });
    })();
    </script>
    """;

    public const string StateRefreshScript = """
    <script>
    (function(){
      var api=window.CehoPanel;if(!api)return;
      var last=Date.now(),busy=false,failed=false,queued=false,timer,stamp=document.getElementById('freshness-text'),retry=document.getElementById('refresh-retry');
      function paint(){
        var age=Math.max(0,Math.floor((Date.now()-last)/1000)),stale=failed||age>25;
        document.body.classList.toggle('panel-stale',stale);
        if(stamp){stamp.parentElement.classList.toggle('stale',stale);stamp.textContent=failed?api.text('Нет связи с панелью. Последние данные: ','Panel unreachable. Last data: ')+age+api.text(' с назад','s ago'):api.text('Данные обновлены ','Data updated ')+age+api.text(' с назад','s ago')}
        if(retry)retry.hidden=!stale;
      }
      function refresh(){
        if(!api.active())return;if(busy){queued=true;return}clearTimeout(timer);busy=true;
        api.request(location.pathname+location.search,6500).then(function(r){return r.text()}).then(function(html){
          if(!api.active())return;
          var doc=new DOMParser().parseFromString(html,'text/html');
          if(!doc.querySelector('[data-live]'))throw new Error('missing-state');
          document.querySelectorAll('[data-live]').forEach(function(now){
            var fresh=doc.querySelector('[data-live="'+now.dataset.live+'"]');
            if(!fresh||fresh.outerHTML===now.outerHTML)return;
            // Do not remove a keyboard user's current control or their unsubmitted edits.
            if((now.contains(document.activeElement)&&document.activeElement!==document.body)||now.querySelector('form[data-submitting="1"],form[data-dirty="1"]')){
              now.classList.add('live-stale');
              if(!now.querySelector('[data-deferred-notice]')){var notice=document.createElement('p');notice.dataset.deferredNotice='1';notice.className='hint';notice.textContent=api.text('Этот раздел не обновлён, чтобы сохранить ввод. Данные могут устареть.','This section was not refreshed to preserve your input. Its data may be stale.');now.appendChild(notice)}
              return;
            }
            var open=Array.from(now.querySelectorAll('details')).map(function(d){return d.open});
            fresh.querySelectorAll('details').forEach(function(d,i){if(open[i]!==undefined)d.open=open[i]});
            fresh.style.animation='none';now.replaceWith(fresh);
          });
          last=Date.now();failed=false;document.dispatchEvent(new CustomEvent('ceho:refreshed'));
        }).catch(function(){if(api.active())failed=true}).finally(function(){busy=false;paint();if(api.active()){timer=setTimeout(refresh,queued?0:10000);queued=false}});
      }
      if(retry)retry.addEventListener('click',refresh);
      document.addEventListener('ceho:refresh',refresh);
      function resume(){if(!api.active())return;failed=true;paint();refresh()}
      addEventListener('online',resume);addEventListener('offline',function(){failed=true;paint()});
      document.addEventListener('visibilitychange',function(){if(!document.hidden)resume()});
      setInterval(paint,1000);paint();timer=setTimeout(refresh,10000);
    })();
    </script>
    """;

    public const string TransferPartsScript = """
    <script>
    (function(){
      document.querySelectorAll('.transfer-parts').forEach(function(block,i){
        var key='ceho-parts-'+i,boxes=block.querySelectorAll('input[type=checkbox]');
        try{
          var saved=sessionStorage.getItem(key);
          if(saved)boxes.forEach(function(b,n){b.checked=saved.charAt(n)==='1'});
        }catch(e){}
        boxes.forEach(function(b){
          b.addEventListener('change',function(){
            try{sessionStorage.setItem(key,Array.prototype.map.call(boxes,function(x){return x.checked?'1':'0'}).join(''))}catch(e){}
          });
        });
      });
    })();
    </script>
    """;

    public const string AppFilterScript = """
    <script>
    (function(){
      document.addEventListener('input',function(e){
        var box=e.target;if(!box.matches('input.app-filter'))return;
        var owner=box.closest('form'),text=box.value.trim().toLowerCase(),shown=0;if(!owner)return;
        owner.querySelectorAll('button.app-card').forEach(function(card){card.hidden=card.hasAttribute('data-added')||(text.length>0&&(card.getAttribute('data-name')||'').indexOf(text)<0);if(!card.hidden)shown++});
        owner.querySelectorAll('.app-group').forEach(function(g){g.hidden=!g.querySelector('button.app-card:not([hidden])')});
        var none=owner.querySelector('#tunnel-no-results');if(none)none.hidden=shown>0;
      });
    })();
    </script>
    """;

    public const string ToastScript = """
    <script>
    (function(){
      if(window.CehoToastReady)return;window.CehoToastReady=true;
      function sync(){var t=document.getElementById('toast');if(!t)return;try{if(sessionStorage.getItem('ceho-toast-'+t.dataset.count))t.hidden=true}catch(e){}}
      document.addEventListener('click',function(e){var x=e.target.closest('#toast-x');if(!x)return;var t=x.closest('#toast');if(!t)return;t.hidden=true;try{sessionStorage.setItem('ceho-toast-'+t.dataset.count,'1')}catch(e){}});
      document.addEventListener('ceho:refreshed',sync);sync();
    })();
    </script>
    """;

    public const string JobScript = """
    <script>
    (function(){
      var api=window.CehoPanel,box=document.getElementById('jp');if(!api||!box||!box.dataset.job)return;
      var id=box.dataset.job,fill=document.getElementById('jf'),bar=fill&&fill.parentElement,
          stage=document.getElementById('js'),num=document.getElementById('jn'),time=document.getElementById('jt'),
          status=document.getElementById('job-status'),retry=document.getElementById('job-retry'),steps=document.getElementById('startup-steps'),confirmedStep=0,
          busy=false,terminal=false,relaunchStarted=0,sawPanelDown=false,lastReply=Date.now(),elapsed=Number(box.dataset.elapsed||0),lastStage=0,lastRevision=-1,timer,relaunch=false;
      function message(text,cls){if(status){status.textContent=text;status.className='job-status '+(cls||'')}}
      function clock(){
        if(terminal||!api.active())return;
        var seconds=elapsed+(Date.now()-lastReply)/1000;
        if(time)time.textContent=api.text('Прошло ','Elapsed ')+Math.floor(seconds)+api.text(' с','s');
        if(Date.now()-lastReply>9000)message(api.text('Ответ панели задерживается. Действие может продолжаться; не запускайте его повторно.','The panel is slow to respond. The operation may still be running; do not start it again.'),'warn');
      }
      function navigate(result,error){
        terminal=true;var q=new URLSearchParams(location.search);q.delete('job');
        if(result){q.set('m',result);q.set('e',error?'1':'0')}
        location.replace(location.pathname+'?'+q.toString());
      }
      function waitPanel(){
        relaunch=true;if(!relaunchStarted)relaunchStarted=Date.now();
        message(api.text('Панель перезапускается. Ждём подтверждения связи.','The panel is restarting. Waiting for it to respond.'),'warn');
        api.request('/?tab=state',5000).then(function(){
          if(sawPanelDown||Date.now()-relaunchStarted>=25000){navigate('',false);return}
          if(api.active())timer=setTimeout(waitPanel,1500);
        }).catch(function(){sawPanelDown=true;if(api.active())timer=setTimeout(waitPanel,1500)});
      }
      function tick(){
        if(busy||terminal||!api.active())return;clearTimeout(timer);busy=true;
        api.request('/job?id='+encodeURIComponent(id),6000).then(function(r){return r.json()}).then(function(j){
          if(!api.active())return;
          if(!j||!['running','done','failed','gone'].includes(j.state))throw new Error('invalid-state');
          if(j.state!=='gone'&&j.id!==id)throw new Error('wrong-job');
          if(j.id&&j.id!==id)throw new Error('wrong-job');
          var updated=j.lastUpdatedUtc?Date.parse(j.lastUpdatedUtc):0;
          if(Number(j.revision)>0){if(j.revision<lastRevision)return;lastRevision=j.revision}
          else if(updated&&updated<lastStage)return;
          if(updated)lastStage=updated;
          elapsed=Number(j.seconds||0);lastReply=Date.now();if(retry)retry.hidden=true;
          confirmedStep=Math.max(confirmedStep,Number(j.startupStep||0));
          if(steps&&confirmedStep>0){steps.hidden=false;steps.querySelectorAll('[data-step]').forEach(function(item){var n=Number(item.dataset.step),complete=n<confirmedStep||j.state==='done';item.classList.toggle('complete',complete);item.classList.toggle('current',n===confirmedStep&&j.state==='running');if(n===confirmedStep&&j.state==='running')item.setAttribute('aria-current','step');else item.removeAttribute('aria-current')})}
          var unknownDuration=!!j.indeterminate,indefinite=unknownDuration&&j.state==='running';
          if(bar){bar.classList.toggle('indeterminate',indefinite);bar.setAttribute('aria-valuetext',j.stage||'');if(unknownDuration)bar.removeAttribute('aria-valuenow');else bar.setAttribute('aria-valuenow',Math.max(0,Math.min(100,j.percent||0)))}
          if(fill&&!indefinite)fill.style.transform='scaleX('+Math.max(0,Math.min(100,j.percent||0))/100+')';
          if(num)num.textContent=unknownDuration?(j.state==='failed'||j.isError?api.text('Не удалось','Failed'):j.state==='done'?api.text('Готово','Done'):api.text('Выполняется','In progress')):(j.percent||0)+'%';
          if(stage)stage.textContent=j.stage||j.phase||'';
          if(j.state==='running'){
            message(j.isSlow?api.text('Этап ещё выполняется. Служба отвечает; ожидаем результат.','This phase is still running. The service is responding; waiting for its result.'):api.text('Можно перейти на другую вкладку. Операция продолжится в фоне.','You can use another tab. The operation continues in the background.'),j.isSlow?'warn':'');
            return;
          }
          if(j.state==='gone'){
            terminal=true;message(api.text('Сведения об операции больше недоступны. Проверьте текущее состояние перед повторным запуском.','Operation details are no longer available. Check the current state before retrying.'),'warn');
            if(num)num.textContent='';if(bar){bar.classList.remove('indeterminate');bar.hidden=true;bar.removeAttribute('aria-valuenow')}return;
          }
          if(j.state==='done'&&!j.isError){if(j.relaunch||box.dataset.relaunch){terminal=true;waitPanel();return}navigate(j.result||'',false);return}
          if(j.state==='failed'||j.isError){terminal=true;box.className='job err';var again=document.getElementById('job-retry-operation');if(again)again.hidden=false;message(j.result||api.text('Операция не завершена. Проверьте состояние и повторите действие.','The operation did not complete. Check its state and retry.'),'err');if(time)time.textContent=api.text('Завершено с ошибкой за ','Failed after ')+Math.floor(elapsed)+api.text(' с','s');if(bar)bar.classList.remove('indeterminate');}
        }).catch(function(){
          if(!api.active())return;
          message(api.text('Нет связи с панелью. Результат операции пока неизвестен; это не означает, что она отменена.','Panel unreachable. The operation result is unknown; this does not mean it was cancelled.'),'warn');if(retry)retry.hidden=false;
        }).finally(function(){busy=false;clock();if(!terminal&&api.active())timer=setTimeout(tick,900)});
      }
      if(retry)retry.addEventListener('click',function(){if(relaunch)waitPanel();else tick()});
      setInterval(clock,1000);tick();
    })();
    </script>
    """;
    public const string InteractionScript = """
    <script>
    (function(){
      'use strict';
      var ru=document.documentElement.lang==='ru';
      var alive=true, generation=0, pending=new Set();
      function request(url, timeout, options){
        var controller=new AbortController(), myGeneration=generation;
        pending.add(controller);
        var timer=setTimeout(function(){controller.abort()},timeout||6000);
        return fetch(url,Object.assign({},options||{},{credentials:'same-origin',cache:'no-store',signal:controller.signal})).then(function(r){
          if(!alive||myGeneration!==generation)throw new Error('superseded');
          if(!r.ok)throw new Error('HTTP '+r.status);
          if(r.redirected&&new URL(r.url).pathname==='/login')throw new Error('auth');
          // Keep timeout and lifecycle cancellation active until the entire body arrives.
          return r.text().then(function(body){
            if(!alive||myGeneration!==generation)throw new Error('superseded');
            return {ok:r.ok,text:function(){return Promise.resolve(body)},json:function(){return Promise.resolve().then(function(){return JSON.parse(body)})}};
          });
        }).finally(function(){clearTimeout(timer);pending.delete(controller)});
      }
      window.CehoPanel={request:request,text:function(a,b){return ru?a:b},active:function(){return alive},generation:function(){return generation},invalidate:function(){generation++;pending.forEach(function(c){c.abort()});pending.clear()}};
      addEventListener('pagehide',function(){alive=false;generation++;pending.forEach(function(c){c.abort()});pending.clear()});
      addEventListener('pageshow',function(e){if(e.persisted){alive=true;generation++;location.reload()}});
      document.addEventListener('input',function(e){if(e.target.form)e.target.form.dataset.dirty='1'});
      document.addEventListener('change',function(e){if(e.target.form)e.target.form.dataset.dirty='1'});
      document.addEventListener('submit',function(e){
        var form=e.target;if(!(form instanceof HTMLFormElement)||form.method.toLowerCase()!=='post')return;
        if(form.dataset.submitting==='1'){e.preventDefault();return}
        var button=e.submitter, action=(button&&button.hasAttribute('formaction')?button.formAction:form.action);
        var message=button&&button.dataset.confirm||form.dataset.confirm;
        if(!message&&/\/subs\/remove(?:$|\?)/.test(action))message=ru?'Удалить подписку? Её серверы больше не будут доступны для выбранных программ.':'Delete this subscription? Its servers will no longer be available for selected apps.';
        if(message&&!confirm(message)){e.preventDefault();return}
        if(e.defaultPrevented)return;
        form.dataset.submitting='1';
        var notice=document.createElement('p');notice.className='submit-status';notice.setAttribute('role','status');notice.textContent=ru?'Отправляем запрос…':'Sending request…';form.appendChild(notice);
        setTimeout(function(){if(form.isConnected&&form.dataset.submitting==='1')notice.textContent=ru?'Ответ задерживается. Ввод сохранён на странице; действие могло начаться. Не отправляйте его повторно.':'The response is delayed. Your input is still here; the operation may have started. Do not submit it again.'},8000);
        form.querySelectorAll('button[type=submit],button:not([type])').forEach(function(b){b.setAttribute('aria-disabled','true')});
        // Native submission keeps the clicked name/value; disabling it would lose that value.
      });
      document.querySelectorAll('dialog.sub-modal').forEach(function(dlg){
        var form=dlg.querySelector('form'),initial='';
        function values(){return form?JSON.stringify(Array.from(new FormData(form).entries())):''}
        function mayClose(){return values()===initial||confirm(dlg.dataset.unsavedConfirm||(ru?'Закрыть без сохранения изменений?':'Close without saving your changes?'))}
        document.querySelectorAll('[data-sub-edit="'+dlg.id+'"]').forEach(function(btn){btn.addEventListener('click',function(){initial=values()})});
        dlg.addEventListener('cancel',function(e){if(!mayClose())e.preventDefault()});
        dlg.addEventListener('click',function(e){if(e.target===dlg){if(mayClose())dlg.close();e.stopImmediatePropagation()}},true);
        dlg.querySelectorAll('[data-sub-close]').forEach(function(btn){btn.addEventListener('click',function(e){e.preventDefault();e.stopImmediatePropagation();if(mayClose())dlg.close()},true)});
      });
    })();
    </script>
    """;

}
