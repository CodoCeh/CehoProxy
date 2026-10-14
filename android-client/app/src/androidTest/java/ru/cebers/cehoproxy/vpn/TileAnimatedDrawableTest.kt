package ru.cebers.cehoproxy.vpn

import android.graphics.drawable.Animatable2
import android.graphics.drawable.AnimatedVectorDrawable
import android.graphics.drawable.Icon
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.*
import org.junit.Test
import ru.cebers.cehoproxy.R

class TileAnimatedDrawableTest {
    @Test fun publicTileIconLoadsAsFrameworkAnimatedVector() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val drawable = Icon.createWithResource(context, R.drawable.ic_ceho_whirlpool_activation).loadDrawable(context)
        assertTrue(drawable is AnimatedVectorDrawable)
        assertTrue(drawable is Animatable2)
        assertNotNull(drawable?.constantState?.newDrawable())
    }
}
