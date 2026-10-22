package ru.cebers.cehoproxy.ui

import android.app.Activity
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Shapes
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.SideEffect
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.view.WindowCompat
import ru.cebers.cehoproxy.ThemePreference

private val Serif = FontFamily.Serif

private val QuietTypography = Typography().let { base ->
    fun serif(style: TextStyle, size: Int, line: Int) = style.copy(fontFamily = Serif, fontWeight = FontWeight.Normal, fontSize = size.sp, lineHeight = line.sp)
    base.copy(
        displaySmall = serif(base.displaySmall, 40, 46),
        headlineLarge = serif(base.headlineLarge, 36, 42),
        headlineMedium = serif(base.headlineMedium, 32, 38),
        headlineSmall = serif(base.headlineSmall, 26, 32),
        titleLarge = serif(base.titleLarge, 22, 28),
    )
}

private val QuietShapes = Shapes(
    extraSmall = RoundedCornerShape(8.dp),
    small = RoundedCornerShape(12.dp),
    medium = RoundedCornerShape(14.dp),
    large = RoundedCornerShape(18.dp),
    extraLarge = RoundedCornerShape(24.dp),
)

private val paperLight = lightColorScheme(
    primary = Color(0xFF52654C), onPrimary = Color(0xFFFFFCF7), primaryContainer = Color(0xFFE4E8D9), onPrimaryContainer = Color(0xFF2F3D2B),
    secondary = Color(0xFF8C9981), onSecondary = Color(0xFFFFFCF7), secondaryContainer = Color(0xFFE4E8D9), onSecondaryContainer = Color(0xFF2F3D2B),
    tertiary = Color(0xFF79602C), onTertiary = Color(0xFFFFFFFF), tertiaryContainer = Color(0xFFF2E3B9), onTertiaryContainer = Color(0xFF302308),
    background = Color(0xFFF6F2E9), onBackground = Color(0xFF303A33),
    surface = Color(0xFFFFFCF7), onSurface = Color(0xFF303A33), onSurfaceVariant = Color(0xFF5F6558),
    surfaceContainerLowest = Color(0xFFFFFCF7), surfaceContainerLow = Color(0xFFFBF8F0), surfaceContainer = Color(0xFFFFFCF7),
    surfaceContainerHigh = Color(0xFFEFEBDF), surfaceContainerHighest = Color(0xFFE0DFD2),
    surfaceVariant = Color(0xFFE4E8D9),
    outline = Color(0xFF8A8F80), outlineVariant = Color(0xFFE0DFD2),
    error = Color(0xFFA3362D), onError = Color(0xFFFFFFFF), errorContainer = Color(0xFFF8DED8), onErrorContainer = Color(0xFF641B16),
)

private val paperDark = darkColorScheme(
    primary = Color(0xFFA9C29A), onPrimary = Color(0xFF1F2A1B), primaryContainer = Color(0xFF34402F), onPrimaryContainer = Color(0xFFE4E8D9),
    secondary = Color(0xFF7F8F75), onSecondary = Color(0xFF1F2A1B), secondaryContainer = Color(0xFF34402F), onSecondaryContainer = Color(0xFFE4E8D9),
    tertiary = Color(0xFFE8B657), onTertiary = Color(0xFF2A2110),
    background = Color(0xFF1B1A16), onBackground = Color(0xFFECE7DA),
    surface = Color(0xFF24231E), onSurface = Color(0xFFECE7DA), onSurfaceVariant = Color(0xFFB3B09F),
    surfaceContainerLowest = Color(0xFF1B1A16), surfaceContainerLow = Color(0xFF211F1B), surfaceContainer = Color(0xFF24231E),
    surfaceContainerHigh = Color(0xFF2E2D26), surfaceContainerHighest = Color(0xFF3A3932),
    surfaceVariant = Color(0xFF34402F),
    outline = Color(0xFF7B7A6C), outlineVariant = Color(0xFF3A3932),
    error = Color(0xFFF2857A), onError = Color(0xFF690005), errorContainer = Color(0xFF5A2A25), onErrorContainer = Color(0xFFFFDAD6),
)

@Composable internal fun CehoTheme(preference: ThemePreference, content: @Composable () -> Unit) {
    val useDark = when (preference) {
        ThemePreference.SYSTEM -> isSystemInDarkTheme()
        ThemePreference.LIGHT -> false
        ThemePreference.DARK -> true
    }
    val scheme = if (useDark) paperDark else paperLight
    val view = LocalView.current
    SideEffect {
        val window = (view.context as? Activity)?.window ?: return@SideEffect
        window.statusBarColor = scheme.background.toArgb()
        window.navigationBarColor = scheme.background.toArgb()
        WindowCompat.getInsetsController(window, view).apply {
            isAppearanceLightStatusBars = !useDark
            isAppearanceLightNavigationBars = !useDark
        }
    }
    MaterialTheme(colorScheme = scheme, typography = QuietTypography, shapes = QuietShapes, content = content)
}
