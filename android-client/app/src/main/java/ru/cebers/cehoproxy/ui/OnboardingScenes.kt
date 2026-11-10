package ru.cebers.cehoproxy.ui

import android.content.Context
import android.provider.Settings
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.widthIn
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.PathEffect
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.drawscope.clipRect
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.TextMeasurer
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.drawText
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.rememberTextMeasurer
import androidx.compose.ui.unit.dp

internal enum class OnboardingScene(val description: String, val still: Float) {
    Add("Ссылка подписки вставляется из буфера, и появляется число серверов.", .85f),
    Country("Сайты вашей страны идут напрямую, остальное идёт через VPN.", .3f),
    Permission("Нажатие на «OK» в системном запросе включает VPN.", .4f),
}

@Composable
internal fun OnboardingSceneArt(scene: OnboardingScene, modifier: Modifier = Modifier) {
    val context = LocalContext.current
    val animated = remember { animationsOn(context) }
    val loop = if (animated) {
        rememberInfiniteTransition(label = "onboarding").animateFloat(
            initialValue = 0f,
            targetValue = 1f,
            animationSpec = infiniteRepeatable(tween(8000, easing = LinearEasing), RepeatMode.Restart),
            label = "onboarding-phase",
        ).value
    } else scene.still
    val colors = MaterialTheme.colorScheme
    val measurer = rememberTextMeasurer()
    val palette = ScenePalette(
        surface = colors.surface,
        field = colors.background,
        line = colors.outlineVariant,
        text = colors.onSurface,
        quiet = colors.onSurfaceVariant,
        accent = colors.primary,
        onAccent = colors.onPrimary,
        chip = colors.surfaceVariant,
    )
    Canvas(
        modifier.fillMaxWidth().widthIn(max = 360.dp).aspectRatio(320f / 180f)
            .semantics { contentDescription = scene.description },
    ) {
        val u = size.width / 320f
        val fade = if (animated) ease(span(loop, 0f, .05f)) * (1f - ease(span(loop, .9f, .97f))) else 1f
        when (scene) {
            OnboardingScene.Add -> drawAdd(loop, u, fade, palette, measurer)
            OnboardingScene.Country -> drawCountry(loop, u, fade, palette, measurer)
            OnboardingScene.Permission -> drawPermission(loop, u, fade, palette, measurer)
        }
    }
}

private class ScenePalette(
    val surface: Color,
    val field: Color,
    val line: Color,
    val text: Color,
    val quiet: Color,
    val accent: Color,
    val onAccent: Color,
    val chip: Color,
)

private fun animationsOn(context: Context): Boolean = runCatching {
    Settings.Global.getFloat(context.contentResolver, Settings.Global.ANIMATOR_DURATION_SCALE, 1f) > 0f
}.getOrDefault(true)

private fun span(t: Float, from: Float, to: Float): Float = ((t - from) / (to - from)).coerceIn(0f, 1f)

private fun ease(x: Float): Float = x * x * (3f - 2f * x)

private fun lerp(a: Float, b: Float, k: Float): Float = a + (b - a) * k

private fun Color.fade(a: Float): Color = copy(alpha = alpha * a.coerceIn(0f, 1f))

private fun DrawScope.label(
    measurer: TextMeasurer,
    text: String,
    x: Float,
    y: Float,
    sizePx: Float,
    color: Color,
    bold: Boolean = false,
    centered: Boolean = false,
    mono: Boolean = false,
) {
    val result = measurer.measure(
        text,
        TextStyle(
            color = color,
            fontSize = sizePx.toSp(),
            fontWeight = if (bold) FontWeight.SemiBold else FontWeight.Normal,
            fontFamily = if (mono) FontFamily.Monospace else FontFamily.Default,
        ),
    )
    val left = if (centered) x - result.size.width / 2f else x
    drawText(result, topLeft = Offset(left, y - result.size.height / 2f))
}

private fun DrawScope.finger(at: Offset, u: Float, a: Float, p: ScenePalette) {
    if (a <= 0f) return
    drawCircle(p.surface.fade(a), 9f * u, at)
    drawCircle(p.accent.fade(a), 7f * u, at)
}

private fun DrawScope.tapRing(at: Offset, u: Float, k: Float, p: ScenePalette, fade: Float) {
    if (k <= 0f || k >= 1f) return
    drawCircle(p.accent.fade((1f - k) * .9f * fade), lerp(6f, 28f, k) * u, at, style = Stroke(2f * u))
}

private fun DrawScope.drawAdd(t: Float, u: Float, fade: Float, p: ScenePalette, m: TextMeasurer) {
    drawRoundRect(p.surface.fade(fade), Offset(14f * u, 14f * u), Size(292f * u, 152f * u), CornerRadius(14f * u))
    drawRoundRect(p.line.fade(fade), Offset(14f * u, 14f * u), Size(292f * u, 152f * u), CornerRadius(14f * u), style = Stroke(1.5f * u))
    drawRoundRect(p.field.fade(fade), Offset(30f * u, 34f * u), Size(260f * u, 34f * u), CornerRadius(9f * u))
    drawRoundRect(p.line.fade(fade), Offset(30f * u, 34f * u), Size(260f * u, 34f * u), CornerRadius(9f * u), style = Stroke(1.2f * u))
    val typed = ease(span(t, .08f, .30f))
    clipRect(42f * u, 34f * u, 42f * u + 240f * u * typed, 68f * u) {
        label(m, "https://vpn.example/sub/8f3k2q", 42f * u, 51f * u, 11f * u, p.quiet.fade(fade), mono = true)
    }
    val press = 1f - .04f * (ease(span(t, .42f, .48f)) - ease(span(t, .50f, .56f)))
    val bw = 260f * u * press
    val bh = 32f * u * press
    drawRoundRect(p.accent.fade(fade), Offset(160f * u - bw / 2f, 98f * u - bh / 2f), Size(bw, bh), CornerRadius(10f * u))
    label(m, "Вставить из буфера", 160f * u, 98f * u, 13f * u, p.onAccent.fade(fade), bold = true, centered = true)
    val chip = ease(span(t, .60f, .70f))
    if (chip > 0f) {
        val dy = (1f - chip) * 8f * u
        drawRoundRect(p.chip.fade(chip * fade), Offset(30f * u, 126f * u + dy), Size(140f * u, 28f * u), CornerRadius(14f * u))
        val tick = Path().apply {
            moveTo(44f * u, 140f * u + dy)
            lineTo(49f * u, 145f * u + dy)
            lineTo(58f * u, 135f * u + dy)
        }
        drawPath(tick, p.accent.fade(chip * fade), style = Stroke(2.4f * u, cap = StrokeCap.Round))
        label(m, "14 серверов", 66f * u, 140f * u + dy, 12f * u, p.text.fade(chip * fade), bold = true)
    }
    val target = Offset(160f * u, 104f * u)
    val moving = ease(span(t, .30f, .44f))
    val at = Offset(lerp(270f * u, target.x, moving), lerp(160f * u, target.y, moving))
    finger(at, u, ease(span(t, .28f, .34f)) * (1f - ease(span(t, .56f, .64f))) * fade, p)
    tapRing(target, u, span(t, .46f, .58f), p, fade)
}

private fun DrawScope.drawCountry(t: Float, u: Float, fade: Float, p: ScenePalette, m: TextMeasurer) {
    val top = 50f * u
    val bottom = 130f * u
    drawRoundRect(p.line.fade(fade), Offset(22f * u, 62f * u), Size(34f * u, 56f * u), CornerRadius(8f * u), style = Stroke(2f * u))
    drawCircle(p.line.fade(fade), 2f * u, Offset(39f * u, 110f * u))
    val direct = Path().apply { moveTo(58f * u, 90f * u); lineTo(80f * u, 50f * u); lineTo(226f * u, 50f * u) }
    val tunnel = Path().apply { moveTo(58f * u, 90f * u); lineTo(80f * u, 130f * u); lineTo(226f * u, 130f * u) }
    drawPath(direct, p.quiet.fade(fade), style = Stroke(2f * u, cap = StrokeCap.Round, pathEffect = PathEffect.dashPathEffect(floatArrayOf(7f * u, 6f * u))))
    drawPath(tunnel, p.accent.fade(fade), style = Stroke(2.4f * u, cap = StrokeCap.Round))
    label(m, "Напрямую", 90f * u, 38f * u, 10.5f * u, p.quiet.fade(fade))
    label(m, "Через VPN", 90f * u, 118f * u, 10.5f * u, p.accent.fade(fade), bold = true)
    drawRoundRect(p.chip.fade(fade), Offset(228f * u, 34f * u), Size(76f * u, 32f * u), CornerRadius(10f * u))
    label(m, "Сайты страны", 266f * u, 50f * u, 10f * u, p.text.fade(fade), centered = true)
    val hub = Offset(266f * u, 130f * u)
    drawCircle(p.line.fade(fade), 24f * u, hub, style = Stroke(1.5f * u))
    listOf(18f, 12f, 6f).forEachIndexed { i, r ->
        drawArc(p.accent.fade(fade), startAngle = (t * 360f * 2f) + i * 70f, sweepAngle = 250f, useCenter = false,
            topLeft = Offset(hub.x - r * u, hub.y - r * u), size = Size(2f * r * u, 2f * r * u), style = Stroke(2f * u, cap = StrokeCap.Round))
    }
    repeat(3) { i ->
        val k = ((t * 3f + i / 3f) % 1f)
        val x = lerp(80f * u, 226f * u, k)
        val a = (1f - ease(span(k, .85f, 1f))) * fade
        drawCircle(p.quiet.fade(a), 4f * u, Offset(x, top))
        drawCircle(p.accent.fade(a), 4.5f * u, Offset(lerp(80f * u, 226f * u, ((t * 3f + i / 3f + .15f) % 1f)), bottom))
    }
}

private fun DrawScope.drawPermission(t: Float, u: Float, fade: Float, p: ScenePalette, m: TextMeasurer) {
    val dialog = 1f - ease(span(t, .58f, .66f))
    val a = dialog * fade
    if (a > 0f) {
        drawRoundRect(p.surface.fade(a), Offset(50f * u, 22f * u), Size(220f * u, 128f * u), CornerRadius(16f * u))
        drawRoundRect(p.line.fade(a), Offset(50f * u, 22f * u), Size(220f * u, 128f * u), CornerRadius(16f * u), style = Stroke(1.5f * u))
        drawKey(Offset(76f * u, 50f * u), u, p.accent.fade(a))
        label(m, "Запрос на подключение", 102f * u, 50f * u, 12f * u, p.text.fade(a), bold = true)
        label(m, "Разрешить CehoProxy создать VPN?", 160f * u, 80f * u, 10.5f * u, p.quiet.fade(a), centered = true)
        label(m, "Отмена", 168f * u, 126f * u, 12f * u, p.quiet.fade(a), centered = true)
        label(m, "OK", 236f * u, 126f * u, 13f * u, p.accent.fade(a), bold = true, centered = true)
    }
    val ok = Offset(236f * u, 126f * u)
    val moving = ease(span(t, .30f, .42f))
    val at = Offset(lerp(290f * u, ok.x, moving), lerp(172f * u, ok.y + 6f * u, moving))
    finger(at, u, ease(span(t, .26f, .32f)) * (1f - ease(span(t, .56f, .62f))) * fade, p)
    tapRing(ok, u, span(t, .44f, .56f), p, fade)
    val pill = ease(span(t, .66f, .76f))
    if (pill > 0f) {
        drawRoundRect(p.chip.fade(pill * fade), Offset(190f * u, 14f * u), Size(112f * u, 28f * u), CornerRadius(14f * u))
        drawKey(Offset(206f * u, 28f * u), u, p.accent.fade(pill * fade))
        label(m, "VPN включён", 226f * u, 28f * u, 11.5f * u, p.text.fade(pill * fade), bold = true)
    }
}

private fun DrawScope.drawKey(at: Offset, u: Float, color: Color) {
    drawCircle(color, 5f * u, at, style = Stroke(2f * u))
    drawLine(color, Offset(at.x + 5f * u, at.y), Offset(at.x + 15f * u, at.y), 2f * u, StrokeCap.Round)
    drawLine(color, Offset(at.x + 12f * u, at.y), Offset(at.x + 12f * u, at.y + 4f * u), 2f * u, StrokeCap.Round)
}
