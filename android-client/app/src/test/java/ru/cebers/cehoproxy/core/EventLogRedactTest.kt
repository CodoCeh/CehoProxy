package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Test

class EventLogRedactTest {
    @Test fun removesIpv4HostAndUuid() {
        val redacted = EventLog.redact("dial tcp 81.90.29.25:443 node-tr.vlessi.cloud 11111111-2222-3333-4444-555555555555")
        assertEquals("dial tcp [ip]:443 [host] [id]", redacted)
    }

    @Test fun keepsPlainStatusText() {
        assertEquals("статус=error: Нет интернета. Подключитесь к Wi-Fi", EventLog.redact("статус=error: Нет интернета. Подключитесь к Wi-Fi"))
    }

    @Test fun removesIpv6() {
        assertFalse(EventLog.redact("peer 2001:db8:85a3:0:0:8a2e:370:7334 down").contains("2001:db8"))
    }
}
