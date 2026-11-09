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
      .light nav.tabs{position:fixed;left:0;right:0;bottom:0;z-index:20;top:auto;flex-direction:row;overflow-x:auto;gap:2px;padding:8px 10px calc(8px + env(safe-area-inset-bottom));background:var(--surface);border-top:1px solid var(--line)}
      .light nav.tabs a,.light nav.tabs details.more summary{width:auto;flex:0 0 auto;min-height:44px;padding:8px 14px;font-size:14px;white-space:nowrap}
      .light nav.tabs details.more>div{position:absolute;bottom:58px;right:8px;background:var(--surface);border:1px solid var(--line);border-radius:16px;padding:8px;box-shadow:0 10px 30px #0003}
    }
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
}
