package ru.cebers.cehoproxy.ui

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxScope
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

@Composable internal fun QuietDivider(modifier: Modifier = Modifier) =
    HorizontalDivider(modifier, thickness = 1.dp, color = MaterialTheme.colorScheme.outlineVariant)

@Composable internal fun QuietBadge(modifier: Modifier = Modifier, size: Dp = 48.dp, background: Color = MaterialTheme.colorScheme.primaryContainer, content: @Composable BoxScope.() -> Unit) =
    Box(modifier.size(size).clip(CircleShape).background(background), contentAlignment = Alignment.Center, content = content)

/** A plain list row: optional leading badge, title with a quieter second line, and an arrow when it opens something. */
@Composable internal fun QuietRow(
    title: String,
    modifier: Modifier = Modifier,
    subtitle: String? = null,
    onClick: (() -> Unit)? = null,
    leading: (@Composable () -> Unit)? = null,
    trailing: (@Composable () -> Unit)? = null,
    chevron: Boolean = onClick != null && trailing == null,
    selected: Boolean = false,
    enabled: Boolean = true,
    titleColor: Color = MaterialTheme.colorScheme.onSurface,
) {
    val shape = RoundedCornerShape(16.dp)
    Row(
        modifier.fillMaxWidth()
            .let { if (selected) it.clip(shape).background(MaterialTheme.colorScheme.primaryContainer) else it }
            .let { if (onClick != null) it.clickable(enabled = enabled, role = Role.Button, onClick = onClick) else it }
            .alpha(if (enabled) 1f else .5f)
            .heightIn(min = 64.dp)
            .padding(horizontal = if (selected) 12.dp else 0.dp, vertical = 10.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        if (leading != null) { leading(); Spacer(Modifier.width(16.dp)) }
        Column(Modifier.weight(1f)) {
            Text(title, style = MaterialTheme.typography.bodyLarge, color = titleColor, maxLines = 2, overflow = TextOverflow.Ellipsis)
            if (subtitle != null) Text(subtitle, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 2.dp))
        }
        if (trailing != null) { Spacer(Modifier.width(12.dp)); trailing() }
        if (chevron) { Spacer(Modifier.width(8.dp)); ChevronGlyph(MaterialTheme.colorScheme.onSurfaceVariant) }
    }
}

/** Large serif headings stop growing past 1.3x system text scale so a single long word still fits a narrow screen. */
@Composable internal fun cappedStyle(style: TextStyle, maxScale: Float = 1.3f): TextStyle {
    val scale = LocalDensity.current.fontScale
    val factor = if (scale > maxScale) maxScale / scale else 1f
    return style.copy(fontSize = style.fontSize * factor, lineHeight = style.lineHeight * factor)
}

@Composable internal fun PageHeading(title: String, modifier: Modifier = Modifier, subtitle: String? = null) {
    Column(modifier.fillMaxWidth().padding(top = 8.dp, bottom = 12.dp)) {
        Text(title, style = cappedStyle(MaterialTheme.typography.headlineMedium), color = MaterialTheme.colorScheme.onSurface)
        if (subtitle != null) Text(subtitle, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 4.dp))
    }
}

@Composable internal fun CaptionLabel(text: String, modifier: Modifier = Modifier) =
    Text(text.uppercase(), style = MaterialTheme.typography.labelMedium.copy(letterSpacing = 1.4.sp), color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = modifier.fillMaxWidth().padding(top = 24.dp, bottom = 4.dp))

@Composable internal fun QuietButton(
    text: String,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    primary: Boolean = true,
    enabled: Boolean = true,
    leading: (@Composable () -> Unit)? = null,
) {
    val content: @Composable () -> Unit = {
        if (leading != null) { leading(); Spacer(Modifier.width(10.dp)) }
        Text(text, style = MaterialTheme.typography.labelLarge.copy(fontSize = 16.sp), maxLines = 2)
    }
    if (primary) Button(onClick, modifier.heightIn(min = 52.dp), enabled, shape = RoundedCornerShape(14.dp), content = { content() })
    else OutlinedButton(
        onClick, modifier.heightIn(min = 52.dp), enabled, shape = RoundedCornerShape(14.dp),
        border = BorderStroke(1.dp, MaterialTheme.colorScheme.outlineVariant),
        colors = ButtonDefaults.outlinedButtonColors(containerColor = MaterialTheme.colorScheme.surfaceContainer, contentColor = MaterialTheme.colorScheme.onSurface),
        content = { content() },
    )
}

@Composable internal fun QuietButtonRow(modifier: Modifier = Modifier, content: @Composable () -> Unit) =
    Row(modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.CenterVertically) { content() }

@Composable private fun Glyph(color: Color, modifier: Modifier = Modifier, size: Dp = 22.dp, draw: DrawScope.(Float, Stroke) -> Unit) =
    Canvas(modifier.size(size)) {
        val unit = this.size.minDimension / 24f
        draw(unit, Stroke(1.7f * unit * 1.1f, cap = StrokeCap.Round, join = StrokeJoin.Round))
    }

@Composable internal fun ChevronGlyph(color: Color, modifier: Modifier = Modifier) = Glyph(color, modifier, 20.dp) { u, stroke ->
    drawPath(Path().apply { moveTo(9 * u, 5 * u); lineTo(16 * u, 12 * u); lineTo(9 * u, 19 * u) }, color, style = stroke)
}

@Composable internal fun ArrowRightGlyph(color: Color) = Glyph(color) { u, stroke ->
    drawLine(color, Offset(4 * u, 12 * u), Offset(19 * u, 12 * u), stroke.width, StrokeCap.Round)
    drawPath(Path().apply { moveTo(13 * u, 6 * u); lineTo(19.5f * u, 12 * u); lineTo(13 * u, 18 * u) }, color, style = stroke)
}

@Composable internal fun ServersGlyph(color: Color) = Glyph(color) { u, stroke ->
    drawRoundRect(color, Offset(4 * u, 4.5f * u), Size(16 * u, 6 * u), CornerRadius(2 * u), style = stroke)
    drawRoundRect(color, Offset(4 * u, 13.5f * u), Size(16 * u, 6 * u), CornerRadius(2 * u), style = stroke)
    drawCircle(color, 0.9f * u, Offset(7.6f * u, 7.5f * u))
    drawCircle(color, 0.9f * u, Offset(7.6f * u, 16.5f * u))
}

@Composable internal fun SlidersGlyph(color: Color) = Glyph(color) { u, stroke ->
    drawLine(color, Offset(4 * u, 8 * u), Offset(20 * u, 8 * u), stroke.width, StrokeCap.Round)
    drawLine(color, Offset(4 * u, 16 * u), Offset(20 * u, 16 * u), stroke.width, StrokeCap.Round)
    drawCircle(color, 2.6f * u, Offset(9 * u, 8 * u), style = stroke)
    drawCircle(color, 2.6f * u, Offset(15 * u, 16 * u), style = stroke)
}

@Composable internal fun ShieldGlyph(color: Color) = Glyph(color) { u, stroke ->
    drawPath(Path().apply {
        moveTo(12 * u, 3.5f * u); lineTo(19 * u, 6.2f * u); lineTo(19 * u, 11.5f * u)
        cubicTo(19 * u, 16 * u, 16 * u, 19.3f * u, 12 * u, 20.7f * u)
        cubicTo(8 * u, 19.3f * u, 5 * u, 16 * u, 5 * u, 11.5f * u)
        lineTo(5 * u, 6.2f * u); close()
    }, color, style = stroke)
}

@Composable internal fun AppearanceGlyph(color: Color) = Glyph(color) { u, stroke ->
    drawCircle(color, 8 * u, Offset(12 * u, 12 * u), style = stroke)
    drawPath(Path().apply { moveTo(12 * u, 4 * u); arcTo(androidx.compose.ui.geometry.Rect(4 * u, 4 * u, 20 * u, 20 * u), -90f, 180f, false); close() }, color)
}

@Composable internal fun HelpGlyph(color: Color) = Glyph(color) { u, stroke ->
    drawCircle(color, 8.5f * u, Offset(12 * u, 12 * u), style = stroke)
    drawPath(Path().apply {
        moveTo(9.4f * u, 9.6f * u); cubicTo(9.6f * u, 7.4f * u, 14.4f * u, 7.4f * u, 14.6f * u, 9.8f * u)
        cubicTo(14.7f * u, 11.6f * u, 12 * u, 11.6f * u, 12 * u, 13.8f * u)
    }, color, style = stroke)
    drawCircle(color, 0.9f * u, Offset(12 * u, 16.6f * u))
}

@Composable internal fun DocumentGlyph(color: Color) = Glyph(color) { u, stroke ->
    drawPath(Path().apply {
        moveTo(7 * u, 3.5f * u); lineTo(14 * u, 3.5f * u); lineTo(18.5f * u, 8 * u); lineTo(18.5f * u, 20.5f * u); lineTo(7 * u, 20.5f * u); close()
        moveTo(14 * u, 3.5f * u); lineTo(14 * u, 8 * u); lineTo(18.5f * u, 8 * u)
    }, color, style = stroke)
}

@Composable internal fun ListGlyph(color: Color) = Glyph(color) { u, stroke ->
    listOf(7f, 12f, 17f).forEach { y -> drawLine(color, Offset(7 * u, y * u), Offset(20 * u, y * u), stroke.width, StrokeCap.Round); drawCircle(color, 0.9f * u, Offset(4 * u, y * u)) }
}
