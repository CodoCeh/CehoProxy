using System.Net;
using System.Text;

namespace ProxyCage.Core;

/// <summary>Анимированные сцены первых шагов: SVG и CSS, без скриптов и картинок.</summary>
public static class OnboardingUi
{
    public const string Css = """
    .ob-scene{margin:14px 0 6px}
    .ob-svg{display:block;width:100%;max-width:360px;height:auto}
    .ob-win{fill:var(--surface);stroke:var(--line);stroke-width:1.5}
    .ob-field{fill:var(--bg);stroke:var(--line);stroke-width:1.2}
    .ob-link{fill:var(--subtext);font-size:11px;font-family:ui-monospace,Menlo,Consolas,monospace}
    .ob-cover{fill:var(--bg);transform-box:fill-box;transform-origin:100% 50%;transform:scaleX(0)}
    .ob-btn rect{fill:var(--button-bg)}
    .ob-btn text{fill:var(--button-text);font-size:13px;font-weight:600}
    .ob-btn,.ob-chip,.ob-tile,.ob-badge{transform-box:fill-box;transform-origin:50% 50%}
    .ob-chip rect{fill:var(--panel2);stroke:var(--line)}
    .ob-chip text,.ob-badge text{fill:var(--text);font-size:12px;font-weight:600}
    .ob-chip path,.ob-badge path.ob-tick{fill:none;stroke:var(--ok-ink);stroke-width:2.4;stroke-linecap:round;stroke-linejoin:round}
    .ob-ring{fill:none;stroke:var(--brand-ink);stroke-width:2;opacity:0;transform-box:fill-box;transform-origin:50% 50%}
    .ob-hand{fill:var(--brand-ink);stroke:var(--surface);stroke-width:2;opacity:0}
    .ob-rim{fill:none;stroke:var(--line);stroke-width:1.5}
    .ob-arm{fill:none;stroke:var(--muted);stroke-width:2;stroke-linecap:round;opacity:.55}
    .ob-arm.on{stroke:var(--brand-ink);opacity:1}
    .ob-core{fill:var(--panel2)}
    .ob-spin{transform-origin:160px 86px;transform-box:view-box}
    .ob-spin2{transform-origin:236px 90px;transform-box:view-box}
    .ob-tile rect,.ob-tile path,.ob-tile circle{fill:var(--brand-ink)}
    .ob-tile.t2 rect,.ob-tile.t2 path,.ob-tile.t2 circle{fill:var(--info-ink)}
    .ob-tile.t3 rect,.ob-tile.t3 path,.ob-tile.t3 circle{fill:var(--warn-ink)}
    .ob-tile .ob-glyph{fill:var(--surface)}
    .ob-state{fill:var(--subtext);font-size:13px;font-weight:600;text-anchor:middle}
    .ob-state.on{fill:var(--ok-ink);opacity:1}
    .ob-state.off{opacity:0}
    .ob-badge rect{fill:var(--panel2);stroke:var(--ok-ink);stroke-width:1.5}
    .ob-wire{fill:none;stroke:var(--brand-ink);stroke-width:2;stroke-dasharray:6 5;stroke-linecap:round}
    .ob-redo{fill:none;stroke:var(--brand-ink);stroke-width:2.4;stroke-linecap:round;transform-box:fill-box;transform-origin:50% 50%}
    .ob-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,360px),1fr));gap:16px;margin-top:14px}
    figure.ob-card{margin:0;padding:12px;border:1px solid var(--line);border-radius:var(--radius);background:var(--panel)}
    figure.ob-card figcaption{margin-top:8px;color:var(--subtext);font-size:var(--fs-s)}
    figure.ob-card figcaption b{display:block;color:var(--text)}
    @media (prefers-reduced-motion:no-preference){
      .ob-all{animation:obFade 8s ease-in-out infinite}
      .ob-cover{animation:obType 8s ease-in-out infinite}
      .ob-sub .ob-btn{animation:obPress 8s ease-in-out infinite}
      .ob-sub .ob-ring{animation:obRing 8s ease-out infinite}
      .ob-hand{animation:obHand 8s ease-in-out infinite}
      .ob-sub .ob-chip{animation:obChip 8s ease-out infinite}
      .ob-apps .ob-spin2{animation:obSpin 7s linear infinite}
      .ob-apps .ob-tile.t1{animation:obFly1 8s ease-in infinite}
      .ob-apps .ob-tile.t2{animation:obFly2 8s ease-in infinite}
      .ob-apps .ob-tile.t3{animation:obFly3 8s ease-in infinite}
      .ob-power .ob-hand{animation:obHandC 8s ease-in-out infinite}
      .ob-power .ob-ring{animation:obRingC 8s ease-out infinite}
      .ob-power .ob-arm.on{animation:obArmOn 8s ease-in-out infinite}
      .ob-power .ob-spin{animation:obSpin 6s linear infinite}
      .ob-power .ob-state.on{animation:obOn 8s ease-in-out infinite}
      .ob-power .ob-state.off{animation:obOff 8s ease-in-out infinite}
      .ob-check .ob-tile{animation:obRedo 8s ease-in-out infinite}
      .ob-check .ob-redo{animation:obTurn 8s ease-in-out infinite}
      .ob-check .ob-wire{animation:obWire 8s ease-in-out infinite}
      .ob-check .ob-badge{animation:obBadge 8s ease-out infinite}
    }
    @keyframes obFade{0%{opacity:0}5%,90%{opacity:1}97%,100%{opacity:0}}
    @keyframes obType{0%,8%{transform:scaleX(1)}30%,100%{transform:scaleX(0)}}
    @keyframes obPress{0%,44%{transform:scale(1)}48%{transform:scale(.96)}54%,100%{transform:scale(1)}}
    @keyframes obRing{0%,45%{opacity:0;transform:scale(.3)}47%{opacity:.9}58%,100%{opacity:0;transform:scale(3.2)}}
    @keyframes obHand{0%,28%{opacity:0;transform:translate(110px,50px)}34%{opacity:1;transform:translate(110px,50px)}44%,52%{opacity:1;transform:translate(0,0)}60%,100%{opacity:0;transform:translate(0,0)}}
    @keyframes obChip{0%,58%{opacity:0;transform:translateY(8px)}68%,100%{opacity:1;transform:translateY(0)}}
    @keyframes obSpin{to{transform:rotate(360deg)}}
    @keyframes obFly1{0%,12%{opacity:1;transform:translate(0,0) scale(1)}28%{opacity:0;transform:translate(192px,38px) scale(.2)}29%,100%{opacity:0;transform:translate(192px,38px) scale(.2)}}
    @keyframes obFly2{0%,26%{opacity:1;transform:translate(0,0) scale(1)}42%{opacity:0;transform:translate(192px,-4px) scale(.2)}43%,100%{opacity:0;transform:translate(192px,-4px) scale(.2)}}
    @keyframes obFly3{0%,40%{opacity:1;transform:translate(0,0) scale(1)}56%{opacity:0;transform:translate(192px,-46px) scale(.2)}57%,100%{opacity:0;transform:translate(192px,-46px) scale(.2)}}
    @keyframes obHandC{0%,18%{opacity:0;transform:translate(70px,60px)}24%{opacity:1;transform:translate(70px,60px)}32%,38%{opacity:1;transform:translate(0,0)}46%,100%{opacity:0;transform:translate(0,0)}}
    @keyframes obRingC{0%,31%{opacity:0;transform:scale(.3)}33%{opacity:.9}46%,100%{opacity:0;transform:scale(3.4)}}
    @keyframes obArmOn{0%,33%{opacity:0}42%,100%{opacity:1}}
    @keyframes obOn{0%,33%{opacity:0}42%,100%{opacity:1}}
    @keyframes obOff{0%,30%{opacity:1}38%,100%{opacity:0}}
    @keyframes obRedo{0%,12%{transform:scale(1)}20%{transform:scale(.88)}28%,100%{transform:scale(1)}}
    @keyframes obTurn{0%,12%{transform:rotate(0)}34%,100%{transform:rotate(360deg)}}
    @keyframes obWire{0%,38%{stroke-dashoffset:0;opacity:0}44%{opacity:1}60%,100%{stroke-dashoffset:-44;opacity:1}}
    @keyframes obBadge{0%,54%{opacity:0;transform:scale(.8)}64%,100%{opacity:1;transform:scale(1)}}
    """;

    private static string E(string s) => WebUtility.HtmlEncode(s);

    /// <summary>Сцена первого шага: ссылка вставляется, кнопка нажимается, появляется число серверов.</summary>
    private static string Subscription(Func<string, object[], string> S, string alt) => $$"""
        <svg class="ob-svg ob-sub" viewBox="0 0 320 180" role="img" aria-label="{{E(alt)}}"><g class="ob-all">
        <rect class="ob-win" x="14" y="14" width="292" height="152" rx="14"/>
        <rect class="ob-field" x="30" y="34" width="260" height="34" rx="9"/>
        <text class="ob-link" x="42" y="55">https://vpn.example/sub/8f3k2q</text>
        <rect class="ob-cover" x="36" y="38" width="248" height="26"/>
        <g class="ob-btn"><rect x="30" y="82" width="260" height="32" rx="10"/><text x="160" y="103" text-anchor="middle">{{E(S("ob_t_add", []))}}</text></g>
        <g class="ob-chip"><rect x="30" y="126" width="140" height="28" rx="14"/><path d="M44 140l5 5 9-10"/><text x="66" y="144">{{E(S("ob_t_servers", []))}}</text></g>
        <circle class="ob-ring" cx="160" cy="98" r="8"/>
        <circle class="ob-hand" cx="160" cy="104" r="7"/>
        </g></svg>
        """;

    /// <summary>Сцена второго шага: программы по очереди улетают в водоворот.</summary>
    private static string Apps(string alt) => $$"""
        <svg class="ob-svg ob-apps" viewBox="0 0 320 180" role="img" aria-label="{{E(alt)}}"><g class="ob-all">
        <circle class="ob-rim" cx="236" cy="90" r="52"/>
        <g class="ob-spin2"><circle class="ob-arm on" cx="236" cy="90" r="40" stroke-dasharray="120 131"/><circle class="ob-arm on" cx="236" cy="90" r="30" stroke-dasharray="90 98"/><circle class="ob-arm on" cx="236" cy="90" r="20" stroke-dasharray="60 66"/><circle class="ob-arm on" cx="236" cy="90" r="10" stroke-dasharray="30 33"/></g>
        <circle class="ob-core" cx="236" cy="90" r="5"/>
        <g class="ob-tile t1"><rect x="26" y="34" width="36" height="36" rx="10"/><circle class="ob-glyph" cx="44" cy="52" r="8"/></g>
        <g class="ob-tile t2"><rect x="26" y="76" width="36" height="36" rx="10"/><path class="ob-glyph" d="M44 86l9 16H35z"/></g>
        <g class="ob-tile t3"><rect x="26" y="118" width="36" height="36" rx="10"/><rect class="ob-glyph" x="36" y="128" width="16" height="16" rx="3"/></g>
        </g></svg>
        """;

    /// <summary>Сцена третьего шага: нажатие на водоворот включает туннель.</summary>
    private static string Power(Func<string, object[], string> S, string alt) => $$"""
        <svg class="ob-svg ob-power" viewBox="0 0 320 180" role="img" aria-label="{{E(alt)}}"><g class="ob-all">
        <circle class="ob-rim" cx="160" cy="86" r="62"/>
        <circle class="ob-arm" cx="160" cy="86" r="48" stroke-dasharray="150 152"/><circle class="ob-arm" cx="160" cy="86" r="36" stroke-dasharray="110 116"/><circle class="ob-arm" cx="160" cy="86" r="24" stroke-dasharray="70 81"/>
        <g class="ob-spin"><circle class="ob-arm on" cx="160" cy="86" r="48" stroke-dasharray="150 152"/><circle class="ob-arm on" cx="160" cy="86" r="36" stroke-dasharray="110 116"/><circle class="ob-arm on" cx="160" cy="86" r="24" stroke-dasharray="70 81"/><circle class="ob-arm on" cx="160" cy="86" r="12" stroke-dasharray="35 40"/></g>
        <circle class="ob-core" cx="160" cy="86" r="6"/>
        <text class="ob-state off" x="160" y="170">{{E(S("ob_t_off", []))}}</text>
        <text class="ob-state on" x="160" y="170">{{E(S("ob_t_on", []))}}</text>
        <circle class="ob-ring" cx="160" cy="86" r="8"/>
        <circle class="ob-hand" cx="160" cy="92" r="7"/>
        </g></svg>
        """;

    /// <summary>Сцена четвёртого шага: программа перезапускается, и виден реальный адрес выхода.</summary>
    private static string Check(string alt) => $$"""
        <svg class="ob-svg ob-check" viewBox="0 0 320 180" role="img" aria-label="{{E(alt)}}"><g class="ob-all">
        <g class="ob-tile t1"><rect x="30" y="64" width="52" height="52" rx="14"/><circle class="ob-glyph" cx="56" cy="90" r="11"/></g>
        <path class="ob-redo" d="M44 44a22 22 0 0 1 24 -6M68 38l-6 -6M68 38l-7 5"/>
        <path class="ob-wire" d="M90 90H150"/>
        <g class="ob-badge"><rect x="152" y="70" width="152" height="40" rx="20"/><path class="ob-tick" d="M170 90l6 6 11-12"/><text x="196" y="95">NL · 203.0.113.7</text></g>
        </g></svg>
        """;

    /// <summary>Сцена для шага мастера: sub, apps, power или check.</summary>
    public static void Scene(StringBuilder sb, string kind, Func<string, object[], string> S)
    {
        var alt = S("ob_alt_" + kind, []);
        sb.Append("<div class=ob-scene>").Append(kind switch
        {
            "sub" => Subscription(S, alt),
            "apps" => Apps(alt),
            "power" => Power(S, alt),
            _ => Check(alt),
        }).Append("</div>");
    }

    /// <summary>Четыре сцены в ряд для вкладки «Помощь».</summary>
    public static void Tour(StringBuilder sb, Func<string, object[], string> S)
    {
        sb.Append("<section class=ob-tour><h2>").Append(E(S("ob_tour_title", []))).Append("</h2><div class=ob-grid>");
        foreach (var kind in new[] { "sub", "apps", "power", "check" })
        {
            sb.Append("<figure class=ob-card>");
            Scene(sb, kind, S);
            sb.Append("<figcaption><b>").Append(E(S("ob_cap_" + kind, []))).Append("</b>")
              .Append(E(S("ob_sub_" + kind, []))).Append("</figcaption></figure>");
        }
        sb.Append("</div></section>");
    }
}
