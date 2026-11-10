using System.Text;

namespace ProxyCage.Core;

/// <summary>CehoProxy Light: та же панель в оформлении Android-клиента, на отдельном локальном порту.</summary>
public static class LightUi
{
    public const string Css = """
    html.light{
      --bg:#f6f2e9; --surface:#fffcf7; --text:#303a33; --subtext:#4b5650; --muted:#5f6558;
      --brand-ink:#52654c; --brand-strong:#2f3d2b; --button-bg:#52654c; --button-text:#fffcf7;
      --ok-ink:#52654c; --warn-ink:#79602c; --danger-ink:#a3362d; --info-ink:#3f6f86;
      --line:#e0dfd2; --panel:#fbf8f0; --panel2:#efebdf; --radius:20px;
      --serif:"Noto Serif","Iowan Old Style",Georgia,"Times New Roman",serif;
      --rail:232px;
    }
    @media (prefers-color-scheme:dark){
      html.light:not([data-theme=light]){
        --bg:#1b1a16; --surface:#24231e; --text:#ece7da; --subtext:#cfcab8; --muted:#b3b09f;
        --brand-ink:#a9c29a; --brand-strong:#c4d7b8; --button-bg:#a9c29a; --button-text:#1f2a1b;
        --ok-ink:#a9c29a; --warn-ink:#e8b657; --danger-ink:#f2857a; --info-ink:#8fc1d9;
        --line:#3a3932; --panel:#211f1b; --panel2:#2e2d26;
      }
    }
    html.light[data-theme=dark]{
      --bg:#1b1a16; --surface:#24231e; --text:#ece7da; --subtext:#cfcab8; --muted:#b3b09f;
      --brand-ink:#a9c29a; --brand-strong:#c4d7b8; --button-bg:#a9c29a; --button-text:#1f2a1b;
      --ok-ink:#a9c29a; --warn-ink:#e8b657; --danger-ink:#f2857a; --info-ink:#8fc1d9;
      --line:#3a3932; --panel:#211f1b; --panel2:#2e2d26;
    }
    .light body{background:var(--bg);color:var(--text)}
    .light h1,.light h2,.light .hero h1,.light .tunnel-panel h2{font-family:var(--serif);font-weight:400;letter-spacing:0}
    .light h2{font-size:26px;line-height:1.25;margin-bottom:12px}

    .light .wrap{max-width:1180px;display:grid;grid-template-columns:var(--rail) minmax(0,1fr);column-gap:36px;align-items:start;padding:22px 28px 56px}
    .light .wrap>*{grid-column:2;min-width:0}
    .light .wrap>header{grid-column:1/-1;border-bottom:0;padding-bottom:6px;margin-bottom:6px}
    .light .wrap:has(#tunnel-workspace){max-width:1180px}
    .light .mark{font-family:var(--serif);font-weight:400;font-size:24px;letter-spacing:0}
    .light header .product-logo{width:38px;height:38px}

    .light nav.tabs{grid-column:1;grid-row:2/span 60;position:sticky;top:18px;flex-direction:column;flex-wrap:nowrap;gap:2px;margin:0;padding:0}
    .light nav.tabs a,.light nav.tabs details.more summary{min-height:46px;padding:10px 16px;border-radius:16px;font-size:16px;color:var(--subtext);width:100%}
    .light nav.tabs a:hover,.light nav.tabs details.more summary:hover{background:var(--panel2);color:var(--text)}
    .light nav.tabs a.on,.light nav.tabs details.more summary.on{background:var(--panel2);color:var(--text);font-weight:600;box-shadow:none}
    .light nav.tabs details.more{width:100%}
    .light nav.tabs details.more>div{position:static;box-shadow:none;border:0;background:transparent;padding:0 0 0 10px}

    .light section{margin:0 0 26px}
    .light .hero,.light section.wizard,.light .tunnel-panel,.light .tunnel-catalog,.light .protection-summary{
      border-radius:var(--radius);border:1px solid var(--line);background:var(--surface)}
    .light button,.light .button,.light a.button{border-radius:16px;font-weight:600}
    .light button.ghost,.light a.ghost{border:1px solid var(--line);background:transparent;color:var(--text)}
    .light input,.light select,.light textarea{border-radius:14px;border:1px solid var(--line);background:var(--surface);color:var(--text)}
    .light .flash{border-radius:16px}
    .light table{border-radius:var(--radius)}
    .light footer{grid-column:1/-1}

    .light .hero{display:flex;flex-direction:column;align-items:center;text-align:center;gap:8px;padding:12px 20px 26px;border:0;background:transparent}
    .light .hero>.dot{display:none}
    .light .hero>div:not(.light-vortex){flex:0 0 auto;width:100%}
    .light .hero h1{font-size:clamp(34px,5vw,48px);line-height:1.15;margin-top:6px}
    .light .hero p{font-size:16px}
    .light .hero p.scope-note{font-size:13px;max-width:56ch;margin:10px auto 0}
    .light .hero form{justify-content:center;margin-top:10px}
    .light .hero button.big{min-height:52px;padding:12px 34px;border-radius:999px}
    .light .consequences{max-width:560px;margin:8px auto 0;text-align:left}

    .light-vortex{position:relative;width:min(100%,360px);aspect-ratio:1;margin:0 auto}
    .light-vortex .tunnel-portal{position:absolute;inset:0;left:0;transform:none;overflow:visible}
    .light-vortex .tunnel-portal svg{overflow:visible;transform-origin:50% 50%}
    .light-vortex .tunnel-rim{stroke:var(--line);stroke-width:1.2}
    .light-vortex .tunnel-arm{stroke:color-mix(in srgb,var(--brand-ink) 62%,var(--bg));stroke-width:1.8}
    .light-vortex.on .tunnel-portal svg{animation:light-spin 36s linear infinite}
    .light-vortex .tunnel-orbit{width:21%}
    .light-vortex .tunnel-orbit .ico{background:var(--surface);border:1px solid var(--line);box-shadow:0 2px 10px #0002}
    @keyframes light-spin{to{transform:rotate(360deg)}}
    @media (prefers-reduced-motion:reduce){.light-vortex.on .tunnel-portal svg{animation:none}}

    .light-speed{display:grid;grid-template-columns:1fr auto;align-items:end;gap:6px 18px;max-width:640px;margin:0 auto 26px;padding:0 4px}
    .light-speed .label{color:var(--muted);font-size:14px}
    .light-speed .rate{font-variant-numeric:tabular-nums;font-size:20px;color:var(--text);text-align:right}
    .light-speed .rate small{font-size:13px;color:var(--muted);margin-left:4px}
    .light-speed svg{grid-column:1/-1;width:100%;height:56px;display:block}
    .light-speed path{fill:none;stroke:var(--brand-ink);stroke-width:1.6;stroke-linejoin:round}
    .light-speed .base{stroke:var(--line);stroke-width:1}

    @media (max-width:820px){
      .light .wrap{display:block;padding:16px 16px 92px}
      .light header .mark{margin-right:auto}
      .light header form.mode{order:9;flex:1 0 100%;justify-content:center;margin:4px 0 0}
      .light nav.tabs{position:fixed;left:0;right:0;bottom:0;z-index:20;top:auto;flex-direction:row;overflow-x:auto;gap:2px;padding:8px 10px calc(8px + env(safe-area-inset-bottom));background:var(--surface);border-top:1px solid var(--line)}
      .light nav.tabs a,.light nav.tabs details.more summary{width:auto;flex:0 0 auto;min-height:44px;padding:8px 14px;font-size:14px;white-space:nowrap}
      .light nav.tabs details.more>div{position:absolute;bottom:58px;right:8px;background:var(--surface);border:1px solid var(--line);border-radius:16px;padding:8px;box-shadow:0 10px 30px #0003}
    }
    .light .light-add{margin:0 0 22px;padding:20px 22px;border:1px solid var(--line);border-radius:var(--radius);background:var(--panel)}
    .light .light-add h3{margin:0 0 6px;font-family:Georgia,"Times New Roman",serif;font-size:22px;font-weight:600}
    .light .light-add .lede{margin:0 0 14px;color:var(--subtext)}
    .light .light-add-actions{display:flex;flex-wrap:wrap;gap:10px;align-items:center}
    .light .light-add-actions>button.primary,.light .light-more>button{min-height:48px;padding:0 22px}
    .light .light-more{position:relative}
    .light .light-menu{position:absolute;z-index:20;top:calc(100% + 6px);left:0;min-width:260px;padding:6px;border:1px solid var(--line);border-radius:16px;background:var(--surface);box-shadow:0 10px 30px rgba(0,0,0,.18)}
    .light .light-menu button{display:block;width:100%;text-align:left;min-height:44px;padding:0 14px;border:0;border-radius:12px;background:transparent;color:var(--text);font-weight:500}
    .light .light-menu button:hover,.light .light-menu button:focus-visible{background:var(--panel2)}
    .light .light-add .hint.bad{color:#b3412f}
    .light .light-offer{display:flex;flex-wrap:wrap;gap:10px;align-items:center;margin-top:12px;padding:12px 14px;border:1px solid var(--line);border-radius:16px;background:var(--panel2)}
    .light .light-offer span{flex:1 1 220px}
    .light .light-offer[hidden],.light .light-menu[hidden]{display:none}
    .light .light-manual{margin:0 0 22px}
    .light .light-manual>summary{cursor:pointer;color:var(--subtext);padding:8px 2px}
    .light a.quiet{color:var(--subtext)}
    .light button.light-vortex{display:block;background:none;border:0;padding:0;color:inherit;cursor:pointer;border-radius:50%;font:inherit}
    .light button.light-vortex:disabled{cursor:default;opacity:.7}
    .light button.light-vortex:focus-visible{outline:3px solid var(--brand-ink);outline-offset:4px}
    .light-vortex .tunnel-orbit[hidden]{display:none}
    .light-vortex.moving .tunnel-portal svg{animation:none}
    .light .light-tap{margin:2px 0 0;color:var(--subtext);font-size:16px}
    .light button.light-plus{display:inline-flex;align-items:center;justify-content:center;flex:0 0 auto;width:40px;height:40px;min-height:0;padding:0;border-radius:50%;border:1px solid var(--line);background:var(--panel);color:var(--brand-ink);cursor:pointer;margin-left:0}
    .light header form.mode{margin-left:auto}
    .light button.light-plus svg{display:block;width:18px;height:18px}
    .light button.light-plus+button.theme{margin-left:0}
    .light dialog.ls-sheet{width:min(560px,calc(100% - 28px));max-height:calc(100dvh - 32px);overflow:auto;padding:24px;border:1px solid var(--line);border-radius:24px;background:var(--bg);color:var(--text);box-shadow:0 20px 80px #0004}
    .light dialog.ls-sheet::backdrop{background:#0c191a77}
    .ls-head{display:flex;align-items:center;justify-content:space-between;gap:12px}
    .ls-head h2{margin:0}
    .ls-sheet textarea{width:100%;min-height:84px;padding:12px 14px;box-sizing:border-box;font:inherit;resize:vertical}
    .ls-sheet .ls-submit{width:100%;min-height:48px;margin-top:10px}
    .ls-sheet .ls-cap{margin:22px 0 4px;font-size:13px;letter-spacing:.04em;text-transform:uppercase;color:var(--muted);font-family:inherit}
    .ls-sheet .ls-rows{border-top:1px solid var(--line)}
    .ls-sheet button.ls-row{display:flex;align-items:center;gap:14px;width:100%;min-height:60px;padding:8px 4px;border:0;border-bottom:1px solid var(--line);border-radius:0;background:transparent;color:var(--text);text-align:left;font-weight:500;cursor:pointer}
    .ls-sheet button.ls-row:hover,.ls-sheet button.ls-row:focus-visible{background:var(--panel2)}
    .ls-sheet .ls-ico{display:grid;place-items:center;flex:0 0 40px;width:40px;height:40px;border-radius:50%;background:var(--panel2);color:var(--brand-ink);font-size:19px}
    .ls-sheet .ls-row small{display:block;color:var(--muted);font-weight:400;font-size:13px}
    .ls-sheet video{display:block;width:100%;max-height:280px;margin-top:12px;border-radius:16px;background:#000}
    .ls-sheet video[hidden],.ls-sheet #ls-manual-form[hidden],.ls-sheet .ls-recv [hidden]{display:none}
    .ls-sheet .ls-recv{margin-top:16px;padding-top:14px;border-top:1px solid var(--line)}
    .ls-sheet .ls-recv[hidden]{display:none}
    .ls-sheet .ls-qr{display:grid;place-items:center;width:min(240px,100%);aspect-ratio:1;margin:8px auto;padding:10px;border-radius:16px;background:#fff}
    .ls-sheet .ls-qr svg{width:100%;height:100%;display:block}
    .ls-sheet .ls-url{text-align:center;font-size:13px;color:var(--muted);overflow-wrap:anywhere;margin:4px 0}
    .ls-sheet #ls-recv-items{margin:6px 0 12px;padding-left:20px}
    .ls-sheet #ls-manual-form{display:grid;gap:10px;margin-top:14px;padding-top:14px;border-top:1px solid var(--line)}
    .ls-sheet #ls-manual-form label{display:grid;gap:4px;font-size:14px;color:var(--subtext)}
    .ls-sheet #ls-manual-form input,.ls-sheet #ls-manual-form select{min-height:44px;padding:0 12px;font:inherit}
    .ls-sheet #ls-manual-form [hidden]{display:none}
    """;

    public const string SpeedScript = """
    <script>
    (function(){
      var box=document.getElementById('light-speed');if(!box)return;
      var down=box.querySelector('[data-down]'),up=box.querySelector('[data-up]'),line=box.querySelector('[data-line]');
      var samples=[],max=60,busy=false,reduce=matchMedia('(prefers-reduced-motion:reduce)').matches;
      function fmt(b){var m=b*8/1e6;return m>=10?m.toFixed(0):m.toFixed(1)}
      function draw(){
        var top=Math.max.apply(null,samples.concat([125000])),w=300,h=56,pts=[];
        samples.forEach(function(v,i){pts.push((i/(max-1)*w).toFixed(1)+' '+(h-2-v/top*(h-6)).toFixed(1))});
        line.setAttribute('d',pts.length?'M'+pts.join('L'):'');
      }
      function tick(){
        if(document.hidden||busy)return;busy=true;
        fetch('/speed',{cache:'no-store'}).then(function(r){if(r.status===401){location.reload();throw 0}return r.json()}).then(function(s){
          down.textContent=fmt(s.down);up.textContent=fmt(s.up);
          samples.push(s.down);if(samples.length>max)samples.shift();draw();
        }).catch(function(){}).finally(function(){busy=false});
      }
      for(var i=0;i<max;i++)samples.push(0);
      tick();setInterval(tick,2000);
    })();
    </script>
    """;

    internal static void Speed(StringBuilder sb, bool ru)
    {
        sb.Append("<section id=light-speed class=light-speed aria-label=\"").Append(ru ? "Скорость" : "Speed").Append("\">")
          .Append("<span class=label>").Append(ru ? "Сейчас" : "Now").Append("</span>")
          .Append("<span class=rate>↓ <b data-down>0,0</b> ↑ <b data-up>0,0</b><small>").Append(ru ? "Мбит/с" : "Mbit/s").Append("</small></span>")
          .Append("<svg viewBox=\"0 0 300 56\" preserveAspectRatio=none aria-hidden=true><path class=base d=\"M0 54H300\"/><path data-line d=\"\"/></svg></section>");
    }

    public const string ChooserCss = """
    :root{--bg:#f6f2e9;--card:#fffdf7;--ink:#26231c;--muted:#6d675a;--line:#d9d2c2;--accent:#52654c;--on:#fff}
    @media (prefers-color-scheme:dark){:root{--bg:#1b1a16;--card:#24221d;--ink:#ece7da;--muted:#a59f90;--line:#38352c;--accent:#a9c29a;--on:#1b1a16}}
    *{box-sizing:border-box}
    body{margin:0;background:var(--bg);color:var(--ink);font:16px/1.45 system-ui,-apple-system,"Segoe UI",sans-serif;display:grid;min-height:100vh;place-items:center}
    main{width:min(420px,100% - 32px);padding:24px 0}
    h1{font:600 26px/1.2 Georgia,"Times New Roman",serif;margin:0 0 18px}
    .choice{display:block;width:100%;text-align:left;margin:0 0 12px;padding:16px 18px;border:1px solid var(--line);border-radius:16px;background:var(--card);color:var(--ink);cursor:pointer;font:inherit}
    .choice b{display:block;font-size:18px}
    .choice span{display:block;color:var(--muted);font-size:14px;margin-top:2px}
    .choice:hover,.choice:focus-visible{border-color:var(--accent);outline:2px solid var(--accent);outline-offset:0}
    .remember{display:flex;gap:10px;align-items:center;margin:16px 2px 6px;cursor:pointer}
    .remember input{width:18px;height:18px;accent-color:var(--accent)}
    .note{color:var(--muted);font-size:13px;margin:6px 2px 0}
    #done{font-size:18px;margin:12px 2px}
    main.busy .choice,main.busy .remember,main.busy .note{display:none}
    """;

    public const string ChooserScript = """
    (function(){
      var main=document.querySelector('main');
      function pick(target){
        if(main.classList.contains('busy'))return;
        main.classList.add('busy');
        document.getElementById('done').hidden=false;
        var body='n='+encodeURIComponent(n)+'&target='+target+(document.getElementById('remember').checked?'&remember=1':'');
        fetch('/open-pick',{method:'POST',headers:{'Content-Type':'application/x-www-form-urlencoded'},body:body})
          .then(function(){setTimeout(function(){try{window.close()}catch(e){}},1500)})
          .catch(function(){main.classList.remove('busy');document.getElementById('done').hidden=true});
      }
      document.querySelectorAll('.choice').forEach(function(b){b.addEventListener('click',function(){pick(b.dataset.target)})});
    })();
    """;

    public const string SubscriptionAddScript = """
    (function(){
      var root=document.getElementById('light-add');if(!root)return;
      var T=LIGHT_ADD_TEXT,note=document.getElementById('light-add-note'),form=document.getElementById('light-add-form'),
          field=document.getElementById('light-add-url'),menu=document.getElementById('light-other-menu'),
          other=document.getElementById('light-other'),offer=document.getElementById('light-offer'),
          existing=[],seenKey='light-clip-seen';
      try{existing=JSON.parse(root.dataset.existing||'[]')}catch(e){}
      var schemes=/^(vless|vmess|trojan|ss|ssr|hysteria2?|hy2|tuic|socks5?|naive\+https?|naive\+quic|wireguard|wg|anytls|juicity):\/\//i;
      function say(text,bad){note.textContent=text||'';note.className='hint'+(bad?' bad':'')}
      function norm(u){u=(u||'').trim();var b=u.split('#')[0].replace(/\/+$/,''),i=b.indexOf('://');if(i<0)return b;
        var rest=b.slice(i+3),a=rest.split('/')[0];return b.slice(0,i).toLowerCase()+'://'+a.toLowerCase()+rest.slice(a.length)}
      function looksLikeSub(u){var x;try{x=new URL(u)}catch(e){return false}
        var h=x.hostname.toLowerCase(),segs=x.pathname.split('/').filter(Boolean),
            q=x.search.replace(/^\?/,'').toLowerCase().split('&').map(function(p){return p.split('=')[0]});
        return /^(sub|subs)\./.test(h)||segs.some(function(s){return /^(sub|subs|subscription|subscriptions|getsub|link|links|s|client|config|configs)$/i.test(s)||/^[A-Za-z0-9_-]{16,}$/.test(s)})||
          q.some(function(k){return ['token','sub','subscription','key'].indexOf(k)>=0})}
      function candidate(text,strict){var t=(text||'').trim();if(!t||t.length>8000)return null;
        var lines=t.split(/\r?\n/).map(function(l){return l.trim()}).filter(Boolean);
        if(lines.length>1)return lines.every(function(l){return schemes.test(l)})?lines.join('\n'):null;
        if(schemes.test(t))return t;
        if(/^https?:\/\/\S+$/i.test(t))return(!strict||looksLikeSub(t))?t:null;
        return null}
      function known(c){var k=norm(c);return existing.some(function(u){return norm(u)===k})}
      function submit(c){say(T.adding);field.value=c;form.submit()}
      function openManual(){var box=document.getElementById('light-manual-box');if(box){box.open=true;var i=box.querySelector('input[name=url]');if(i){i.scrollIntoView({block:'center'});i.focus()}}}
      function closeMenu(){menu.hidden=true;other.setAttribute('aria-expanded','false')}
      document.getElementById('light-paste').addEventListener('click',function(){
        if(!navigator.clipboard||!navigator.clipboard.readText){say(T.denied,true);openManual();return}
        navigator.clipboard.readText().then(function(text){var c=candidate(text,false);if(!c){say(T.emptyClip,true);return}submit(c)})
          .catch(function(){say(T.denied,true);openManual()})});
      other.addEventListener('click',function(e){e.stopPropagation();var open=menu.hidden;menu.hidden=!open;other.setAttribute('aria-expanded',String(open));if(open)menu.querySelector('button').focus()});
      document.addEventListener('click',function(e){if(!menu.contains(e.target))closeMenu()});
      document.addEventListener('keydown',function(e){if(e.key==='Escape')closeMenu()});
      document.getElementById('light-manual').addEventListener('click',function(){closeMenu();openManual()});
      var picker=document.getElementById('light-file-input');
      document.getElementById('light-file').addEventListener('click',function(){closeMenu();picker.value='';picker.click()});
      picker.addEventListener('change',function(){var f=picker.files&&picker.files[0];if(!f)return;
        if(f.size>8*1024*1024){say(T.big,true);return}
        var r=new FileReader();r.onload=function(){var c=candidate(String(r.result),false);if(!c){say(T.emptyClip,true);return}submit(c)};r.readAsText(f)});
      function checkOffer(){
        if(!navigator.clipboard||!navigator.clipboard.readText||!navigator.permissions||!document.hasFocus())return;
        navigator.permissions.query({name:'clipboard-read'}).then(function(p){if(p.state!=='granted')return;
          return navigator.clipboard.readText().then(function(text){
            var c=candidate(text,true);if(!c||known(c))return;
            var seen='';try{seen=localStorage.getItem(seenKey)||''}catch(e){}
            if(seen===norm(c))return;
            offer.hidden=false;offer.dataset.link=c})}).catch(function(){})}
      document.getElementById('light-offer-add').addEventListener('click',function(){var c=offer.dataset.link;if(c)submit(c)});
      document.getElementById('light-offer-skip').addEventListener('click',function(){
        try{localStorage.setItem(seenKey,norm(offer.dataset.link||''))}catch(e){}offer.hidden=true});
      checkOffer();window.addEventListener('focus',checkOffer);
    })();
    """;
    internal static void AddSheet(StringBuilder sb, string tab, Func<string, object[], string> S)
    {
        string E(string v) => System.Net.WebUtility.HtmlEncode(v);
        string Row(string id, string glyph, string title, string sub) =>
            $"<button type=button id={id} class=ls-row><span class=ls-ico aria-hidden=true>{glyph}</span><span>{E(S(title, []))}<small>{E(S(sub, []))}</small></span></button>";
        sb.Append("<dialog id=ls-sheet class=ls-sheet aria-labelledby=ls-title><div class=ls-head><h2 id=ls-title>")
          .Append(E(S("light_add_title", []))).Append("</h2><button type=button id=ls-close class=ghost aria-label=\"")
          .Append(E(S("light_add_close", []))).Append("\">×</button></div><p class=lede>").Append(E(S("light_add_hint", []))).Append("</p>");
        sb.Append("<form id=ls-form method=post action=/subs/add><input type=hidden name=tab value=\"").Append(E(tab))
          .Append("\"><input type=hidden name=kind value=sub><label class=sr-only for=ls-url>").Append(E(S("light_add_field", [])))
          .Append("</label><textarea id=ls-url name=url rows=3 autocomplete=off spellcheck=false placeholder=\"")
          .Append(E(S("light_add_field", []))).Append("\"></textarea><button id=ls-submit class=\"primary ls-submit\" disabled>")
          .Append(E(S("light_add_submit", []))).Append("</button></form><p id=ls-note class=hint role=status aria-live=polite></p>");
        sb.Append("<h3 class=ls-cap>").Append(E(S("light_add_others", []))).Append("</h3><div class=ls-rows>")
          .Append(Row("ls-paste", "⎘", "light_add_paste", "light_add_paste_sub"))
          .Append(Row("ls-camera", "◉", "light_add_camera", "light_add_camera_sub"))
          .Append(Row("ls-qrimg", "▦", "light_add_qrimg", "light_add_qrimg_sub"))
          .Append(Row("ls-manual", "⚙", "light_add_manual", "light_add_manual_sub"))
          .Append(Row("ls-device", "⇄", "light_add_device", "light_add_device_sub"))
          .Append(Row("ls-file", "▤", "light_add_file", "light_add_file_sub"))
          .Append("</div><video id=ls-video hidden playsinline muted></video>");
        sb.Append("<section id=ls-recv class=ls-recv hidden aria-live=polite><h3>").Append(E(S("light_recv_title", []))).Append("</h3>")
          .Append("<p class=lede>").Append(E(S("light_recv_steps", []))).Append("</p>")
          .Append("<div id=ls-recv-wait hidden><div id=ls-qr class=ls-qr role=img aria-label=QR></div><p class=ls-url id=ls-recv-url></p>")
          .Append("<p class=hint><span id=ls-recv-state>").Append(E(S("light_recv_waiting", []))).Append("</span> <span id=ls-recv-left></span></p></div>")
          .Append("<div id=ls-recv-got hidden><p><b>").Append(E(S("light_recv_got", []))).Append("</b></p><ul id=ls-recv-items></ul>")
          .Append("<form id=ls-recv-form method=post action=/receive/confirm><input type=hidden name=tab value=\"").Append(E(tab)).Append("\">")
          .Append("<button class=\"primary ls-submit\">").Append(E(S("light_recv_confirm", []))).Append("</button>")
          .Append("<button type=button id=ls-recv-cancel class=\"ghost ls-submit\">").Append(E(S("light_recv_cancel", []))).Append("</button></form></div>")
          .Append("<p id=ls-recv-msg class=hint hidden></p></section>");
        sb.Append("<form id=ls-manual-form method=post action=/subs/add hidden><input type=hidden name=tab value=\"").Append(E(tab))
          .Append("\"><input type=hidden name=kind value=sub><input type=hidden name=name id=lm-name-out><input type=hidden name=url id=lm-url>")
          .Append("<label>").Append(E(S("light_man_protocol", []))).Append("<select id=lm-proto><option value=vless>VLESS</option><option value=trojan>Trojan</option><option value=ss>Shadowsocks</option><option value=socks5>SOCKS5</option></select></label>")
          .Append("<label>").Append(E(S("light_man_name", []))).Append("<input id=lm-name autocomplete=off></label>")
          .Append("<label>").Append(E(S("light_man_host", []))).Append("<input id=lm-host autocomplete=off required></label>")
          .Append("<label>").Append(E(S("light_man_port", []))).Append("<input id=lm-port inputmode=numeric autocomplete=off required></label>")
          .Append("<label id=lm-user-l>").Append(E(S("light_man_user", []))).Append("<input id=lm-user autocomplete=off></label>")
          .Append("<label id=lm-pass-l>").Append(E(S("light_man_pass", []))).Append("<input id=lm-pass type=password autocomplete=off></label>")
          .Append("<label id=lm-sni-l>").Append(E(S("light_man_sni", []))).Append("<input id=lm-sni autocomplete=off></label>")
          .Append("<label id=lm-method-l hidden>").Append(E(S("light_man_method", []))).Append("<select id=lm-method><option>chacha20-ietf-poly1305</option><option>aes-256-gcm</option><option>aes-128-gcm</option><option>2022-blake3-aes-256-gcm</option></select></label>")
          .Append("<button class=\"primary ls-submit\">").Append(E(S("light_man_submit", []))).Append("</button></form>");
        sb.Append("<input type=file id=ls-file-input hidden accept=\".txt,.json,.yaml,.yml,.conf,text/plain\">")
          .Append("<input type=file id=ls-qr-input hidden accept=\"image/*\">");
        sb.Append("<script>var LS_TEXT=").Append(System.Text.Json.JsonSerializer.Serialize(new
        {
            bad = S("light_add_bad", []),
            emptyClip = S("subs_light_empty_clip", []),
            denied = S("subs_light_clip_denied", []),
            big = S("subs_light_file_big", []),
            adding = S("subs_light_adding", []),
            qrNone = S("light_qr_none", []),
            qrNoDecoder = S("light_qr_nodecoder", []),
            scanning = S("light_qr_scanning", []),
            camDenied = S("light_cam_denied", []),
            manBad = S("light_man_bad", []),
            recvLeft = S("light_recv_left", ["{0}"]),
            recvNoLan = S("light_recv_nolan", []),
            recvExpired = S("light_recv_expired", []),
        })).Append(";").Append(AddSheetScript).Append("</script></dialog>");
    }

    public const string AddSheetScript = """
    (function(){
      var dlg=document.getElementById('ls-sheet'),plus=document.getElementById('light-plus');if(!dlg)return;
      var T=LS_TEXT,$=function(i){return document.getElementById(i)};
      var note=$('ls-note'),area=$('ls-url'),btn=$('ls-submit'),form=$('ls-form'),video=$('ls-video'),stream=null,timer=0;
      var schemes=/^(vless|vmess|trojan|ss|ssr|hysteria2?|hy2|tuic|socks5?|naive\+https?|naive\+quic|wireguard|wg|anytls|juicity):\/\//i;
      function say(t,bad){note.textContent=t||'';note.className='hint'+(bad?' bad':'')}
      function candidate(text){var t=(text||'').trim();if(!t||t.length>8000)return null;
        var lines=t.split(/\r?\n/).map(function(l){return l.trim()}).filter(Boolean);
        if(lines.length>1)return lines.every(function(l){return schemes.test(l)})?lines.join('\n'):null;
        if(schemes.test(t)||/^https?:\/\/\S+$/i.test(t))return t;
        return null}
      function submit(c){say(T.adding);area.value=c;form.submit()}
      function stopCam(){clearInterval(timer);timer=0;if(stream){stream.getTracks().forEach(function(x){x.stop()});stream=null}video.hidden=true;video.srcObject=null}
      function open(){if(dlg.open)return;if(dlg.showModal)dlg.showModal();else dlg.setAttribute('open','');say('');area.focus()}
      function close(){stopCam();if(dlg.close)dlg.close();else dlg.removeAttribute('open')}
      if(plus)plus.addEventListener('click',open);
      if(location.hash==='#add'){history.replaceState(null,'',location.pathname+location.search);open()}
      $('ls-close').addEventListener('click',close);
      dlg.addEventListener('close',stopCam);
      area.addEventListener('input',function(){btn.disabled=!area.value.trim()});
      form.addEventListener('submit',function(e){var c=candidate(area.value);if(!c){e.preventDefault();say(T.bad,true);return}area.value=c;say(T.adding)});
      $('ls-paste').addEventListener('click',function(){
        if(!navigator.clipboard||!navigator.clipboard.readText){say(T.denied,true);area.focus();return}
        navigator.clipboard.readText().then(function(text){var c=candidate(text);if(!c){say(T.emptyClip,true);return}submit(c)})
          .catch(function(){say(T.denied,true);area.focus()})});
      var fileIn=$('ls-file-input');
      $('ls-file').addEventListener('click',function(){fileIn.value='';fileIn.click()});
      fileIn.addEventListener('change',function(){var f=fileIn.files&&fileIn.files[0];if(!f)return;
        if(f.size>8*1024*1024){say(T.big,true);return}
        var r=new FileReader();r.onload=function(){var c=candidate(String(r.result));if(!c){say(T.emptyClip,true);return}submit(c)};r.readAsText(f)});
      var libLoad=null;
      function lib(){if(window.jsQR)return Promise.resolve();
        if(!libLoad)libLoad=new Promise(function(ok,no){var el=document.createElement('script');el.src='/light-qr.js';
          el.onload=ok;el.onerror=function(){libLoad=null;no('nodecoder')};document.head.appendChild(el)});
        return libLoad}
      function viaLib(src,w,h){return lib().then(function(){
        var cv=document.createElement('canvas');cv.width=w;cv.height=h;var cx=cv.getContext('2d',{willReadFrequently:true});cx.drawImage(src,0,0,w,h);
        var d=cx.getImageData(0,0,w,h),r=window.jsQR(d.data,w,h);return r?r.data:null})}
      function decode(src,w,h){
        if(window.BarcodeDetector){
          return new BarcodeDetector({formats:['qr_code']}).detect(src).then(function(r){return r.length?r[0].rawValue:viaLib(src,w,h)}).catch(function(){return viaLib(src,w,h)})}
        return viaLib(src,w,h)}
      function fromQr(text){var c=candidate(text);if(!c){say(T.bad,true);return false}submit(c);return true}
      var qrIn=$('ls-qr-input');
      $('ls-qrimg').addEventListener('click',function(){qrIn.value='';qrIn.click()});
      qrIn.addEventListener('change',function(){var f=qrIn.files&&qrIn.files[0];if(!f)return;
        createImageBitmap(f).then(function(bmp){return decode(bmp,bmp.width,bmp.height)})
          .then(function(text){if(!text){say(T.qrNone,true);return}fromQr(text)})
          .catch(function(e){say(e==='nodecoder'?T.qrNoDecoder:T.qrNone,true)})});
      $('ls-camera').addEventListener('click',function(){
        stopCam();
        if(!navigator.mediaDevices||!navigator.mediaDevices.getUserMedia){say(T.camDenied,true);return}
        navigator.mediaDevices.getUserMedia({video:{facingMode:'environment'}}).then(function(s){
          stream=s;video.srcObject=s;video.hidden=false;video.play();say(T.scanning);
          var busy=false;timer=setInterval(function(){
            if(busy||!video.videoWidth)return;busy=true;
            decode(video,video.videoWidth,video.videoHeight).then(function(text){if(text){var ok=candidate(text);if(ok){stopCam();submit(ok)}}})
              .catch(function(){}).finally(function(){busy=false})},300)})
          .catch(function(){say(T.camDenied,true)})});
      var recv=$('ls-recv'),rWait=$('ls-recv-wait'),rGot=$('ls-recv-got'),rMsg=$('ls-recv-msg'),poll=0,asked=false,shownUrl='';
      function rShow(which,msg){rWait.hidden=which!=='wait';rGot.hidden=which!=='got';rMsg.hidden=!msg;rMsg.textContent=msg||''}
      function rStop(send){clearInterval(poll);poll=0;if(send&&asked){fetch('/receive/cancel',{method:'POST',credentials:'same-origin'}).catch(function(){})}asked=false;shownUrl=''}
      function drawQr(url){var box=$('ls-qr');box.textContent='';
        var go=function(){try{var q=qrcode(0,'M');q.addData(url);q.make();box.innerHTML=q.createSvgTag(5,2)}catch(e){}};
        if(window.qrcode)go();else{var el=document.createElement('script');el.src='/light-qrgen.js';el.onload=go;document.head.appendChild(el)}}
      function fmt(sec){var m=Math.floor(sec/60),s=sec%60;return m+':'+(s<10?'0':'')+s}
      function render(st){
        if(st.state==='waiting'){
          rShow('wait');$('ls-recv-url').textContent=st.url;
          if(shownUrl!==st.url){shownUrl=st.url;drawQr(st.url)}
          $('ls-recv-left').textContent=T.recvLeft.replace('{0}',fmt(st.seconds))}
        else if(st.state==='got'){
          rStop(false);rShow('got');var ul=$('ls-recv-items');ul.textContent='';
          st.items.forEach(function(x){var li=document.createElement('li');li.textContent=x;ul.appendChild(li)})}
        else if(st.state==='nolan'){rStop(false);rShow('',T.recvNoLan)}
        else{rStop(false);rShow('',T.recvExpired)}}
      $('ls-device').addEventListener('click',function(){
        stopCam();recv.hidden=false;rShow('');asked=true;
        fetch('/receive/start',{method:'POST',credentials:'same-origin'}).then(function(r){return r.json()}).then(function(st){
          render(st);setTimeout(function(){recv.scrollIntoView({block:'start',behavior:'smooth'})},50);
          if(st.state==='waiting')poll=setInterval(function(){
            fetch('/receive/status',{credentials:'same-origin',cache:'no-store'}).then(function(r){return r.json()}).then(render).catch(function(){})},1500)})
          .catch(function(){rShow('',T.recvNoLan)});
        recv.scrollIntoView({block:'nearest'})});
      $('ls-recv-cancel').addEventListener('click',function(){rStop(true);recv.hidden=true});
      dlg.addEventListener('close',function(){rStop(true);recv.hidden=true});
      var man=$('ls-manual-form'),proto=$('lm-proto');
      function layout(){var p=proto.value;
        $('lm-user-l').hidden=!(p==='vless'||p==='socks5');
        $('lm-pass-l').hidden=p==='vless';
        $('lm-sni-l').hidden=!(p==='vless'||p==='trojan');
        $('lm-method-l').hidden=p!=='ss'}
      proto.addEventListener('change',layout);layout();
      $('ls-manual').addEventListener('click',function(){man.hidden=!man.hidden;if(!man.hidden)$('lm-host').focus()});
      function build(){
        var p=proto.value,name=$('lm-name').value.trim(),host=$('lm-host').value.trim(),port=$('lm-port').value.trim(),
            user=$('lm-user').value.trim(),pass=$('lm-pass').value,sni=$('lm-sni').value.trim(),e=encodeURIComponent;
        if(!host||!/^\d{1,5}$/.test(port)||+port<1||+port>65535)return null;
        var h=host.indexOf(':')>=0&&host.charAt(0)!=='['?'['+host+']':host,tag=name?'#'+e(name):'';
        if(p==='vless')return user?'vless://'+e(user)+'@'+h+':'+port+'?encryption=none&security=tls'+(sni?'&sni='+e(sni):'')+tag:null;
        if(p==='trojan')return pass?'trojan://'+e(pass)+'@'+h+':'+port+'?security=tls'+(sni?'&sni='+e(sni):'')+tag:null;
        if(p==='ss'){if(!pass)return null;return 'ss://'+btoa($('lm-method').value+':'+pass).replace(/=+$/,'')+'@'+h+':'+port+tag}
        return 'socks5://'+(user?e(user)+(pass?':'+e(pass):'')+'@':'')+h+':'+port+tag}
      man.addEventListener('submit',function(ev){var u=build();if(!u){ev.preventDefault();say(T.manBad,true);return}
        $('lm-url').value=u;$('lm-name-out').value=$('lm-name').value.trim();say(T.adding)});
    })();
    """;

    public const string VortexScript = """
    <script>
    (function(){
      var ARMS=6,LIMIT=5,R=88,phase=0,batch=0,last=0,reduce=matchMedia('(prefers-reduced-motion:reduce)').matches;
      function pt(t,arm,turn){var a=t*Math.PI*4+arm*2*Math.PI/ARMS+turn,r=R*(.88-.70*t);return[100+Math.cos(a)*r,100+Math.sin(a)*r]}
      function place(v,moving){
        var all=[].slice.call(v.querySelectorAll('.tunnel-orbit')),n=all.length,svg=v.querySelector('svg'),
            turn=moving?phase*2*Math.PI:0;
        if(svg)svg.style.transform=moving?'rotate('+(turn*180/Math.PI)+'deg)':'';
        v.classList.toggle('moving',moving);
        all.forEach(function(o,i){
          var k=moving?((i-batch*LIMIT)%n+n)%n:i;
          if(k>=LIMIT||k>=n){o.hidden=true;return}
          o.hidden=false;
          var t=moving?phase:0,p=pt(t,k,turn);
          o.style.left=(p[0]/2)+'%';o.style.top=(p[1]/2)+'%';
          var a=moving?Math.min(t/.06,1)*Math.min((1-t)/.12,1):1;
          o.style.opacity=String(a);
          o.style.transform='translate(-50%,-50%) scale('+(moving?1-t*.6:1)+')'})}
      function frame(ts){
        var v=document.querySelector('.light-vortex');
        if(v){
          var on=v.classList.contains('on')&&!reduce&&!document.hidden;
          if(on){if(last){phase+=Math.min(ts-last,100)/32*.009}
            if(phase>=1){phase%=1;batch++}
          }else{phase=0;batch=0}
          place(v,on)}
        last=ts;requestAnimationFrame(frame)}
      requestAnimationFrame(frame);
    })();
    </script>
    """;
}
