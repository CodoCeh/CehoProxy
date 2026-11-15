package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class TrafficRowsTest {
    private fun sample(pkg: String, exit: String, rule: String = "", down: Long = 10, closed: Boolean = false, host: String = "example.org") =
        ConnectionSample("$pkg-$exit-$host-$down", pkg, if (exit == "direct" || exit == "block") exit else "global", if (exit == "direct" || exit == "block") listOf(exit) else listOf(exit, "global"), rule, host, 5, down, closed)

    @Test fun directAppsComeFirstWithTheirRule() {
        val rows = TrafficRows.rows(listOf(
            sample("app.vpn", "proxy-1", down = 1000),
            sample("com.google.android.youtube", "direct", "package_name=[com.google.android.youtube] => route(direct)", host = "rr1---sn-x.googlevideo.com"),
            sample("com.google.android.youtube", "direct", "package_name=[com.google.android.youtube] => route(direct)", down = 0, closed = true),
        )) { tag -> "Узел $tag" }
        assertEquals(listOf("com.google.android.youtube", "app.vpn"), rows.map { it.packageName })
        val youtube = rows.first()
        assertEquals(RouteKind.DIRECT, youtube.kind)
        assertEquals(listOf("Правило для приложения"), youtube.rules)
        assertEquals(1, youtube.noReply)
        assertEquals(1, youtube.active)
        assertEquals(listOf("Узел proxy-1"), rows.last().exits)
        assertEquals(listOf("Основной маршрут"), rows.last().rules)
    }

    @Test fun ruleLabelsArePlain() {
        assertEquals("Список сайтов", TrafficRows.ruleLabel("domain=[a.ru] domain_suffix=[.a.ru] => route(direct)"))
        assertEquals("Набор сайтов страны", TrafficRows.ruleLabel("rule_set=[country-ru-ip] => route(direct)"))
        assertEquals("IPv6 закрыт", TrafficRows.ruleLabel("ip_version=6 => reject"))
    }

    @Test fun throughVpnRemovesEveryDirectPath() {
        val excluded = ClientProfile(apps = listOf("a", "b"), excludeSelectedApps = true, localDpiApps = listOf("b"))
        assertEquals(ClientProfile(apps = listOf("a"), excludeSelectedApps = true, localDpiApps = emptyList()), TrafficRows.throughVpn(excluded, "b"))
        val only = ClientProfile(apps = listOf("a"), excludeSelectedApps = false)
        assertEquals(listOf("a", "c"), TrafficRows.throughVpn(only, "c")!!.apps)
        assertNull(TrafficRows.throughVpn(ClientProfile(), "c"))
    }

    @Test fun bytesAreReadable() {
        assertEquals("512 Б", TrafficRows.bytes(512))
        assertEquals("3 КБ", TrafficRows.bytes(3 * 1024))
        assertEquals("1,5 МБ", TrafficRows.bytes(1536L * 1024))
        assertEquals("2,0 ГБ", TrafficRows.bytes(2L shl 30))
    }
}
