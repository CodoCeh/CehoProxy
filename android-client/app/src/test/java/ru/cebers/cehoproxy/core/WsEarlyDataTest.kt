package ru.cebers.cehoproxy.core

import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Test

class WsEarlyDataTest {
    private fun transport(link: String): JSONObject =
        JSONObject(RoutingConfig.build(NodeShareParser.parse(link).first(), RoutePolicy())).getJSONArray("outbounds")
            .let { list -> (0 until list.length()).map(list::getJSONObject).first { it.has("transport") } }.getJSONObject("transport")

    @Test fun edInThePathBecomesEarlyDataInTheXrayHeader() {
        val t = transport("vless://11111111-1111-1111-1111-111111111111@198.51.100.2:443?security=tls&sni=cdn.example&type=ws&path=%2Fws%3Fed%3D2048&host=cdn.example#ws")
        assertEquals("/ws", t.getString("path"))
        assertEquals(2048, t.getInt("max_early_data"))
        assertEquals("Sec-WebSocket-Protocol", t.getString("early_data_header_name"))
    }

    @Test fun otherQueryPartsOfThePathStay() {
        val t = transport("vless://11111111-1111-1111-1111-111111111111@198.51.100.2:443?security=tls&type=ws&path=%2Fws%3Fkey%3Dv%26ed%3D512#ws")
        assertEquals("/ws?key=v", t.getString("path"))
        assertEquals(512, t.getInt("max_early_data"))
    }

    @Test fun plainPathHasNoEarlyData() {
        val t = transport("vless://11111111-1111-1111-1111-111111111111@198.51.100.2:443?security=tls&type=ws&path=%2Fws#ws")
        assertEquals("/ws", t.getString("path"))
        assertFalse(t.has("max_early_data"))
    }
}
