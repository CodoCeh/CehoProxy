package ru.cebers.cehoproxy.core

import java.io.File
import org.junit.Assume.assumeTrue
import org.junit.Test

/** Writes the engine config the app builds for each lab link, so a real sing-box can be run against real servers. Does nothing unless CEHO_LAB_DIR is set. */
class LabConfigDumpTest {
    @Test fun dump() {
        val dir = System.getenv("CEHO_LAB_DIR")
        assumeTrue(dir != null)
        File(dir, "e2e.txt").takeIf { it.exists() }?.readLines()?.filter { it.isNotBlank() }?.forEach { line ->
            val name = line.substringBefore('\t')
            val nodes = NodeShareParser.parse(line.substringAfter('\t').replace("\\n", "\n"))
            val ai = name.startsWith("ai")
            val profile = ProfileCodec.encode(ClientProfile(nodes = if (ai) nodes else listOf(nodes.first()), apps = listOf("com.android.chrome"), aiMode = ai, autoRotationEnabled = ai))
            val escaped = profile.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;").replace("\"", "&quot;")
            File(dir, "profile-$name.xml").writeText("<?xml version='1.0' encoding='utf-8' standalone='yes' ?>\n<map>\n    <string name=\"data\">$escaped</string>\n</map>\n")
        }
        File(dir, "manual.txt").takeIf { it.exists() }?.readLines()?.filter { it.isNotBlank() }?.forEach { line ->
            val f = line.split('\t')
            val form = ManualConnection(ManualProtocol.valueOf(f[1]), f[2], f[3], f[4], f[5], f[6], "", f[7] == "1", f[8] == "1", f[9].ifBlank { shadowsocksMethods.first() })
            val problem = ManualConnectionPolicy.validate(form)
            val node = if (problem == null) NodeShareParser.parse(ManualConnectionPolicy.toUri(form)).firstOrNull() else null
            File(dir, "${f[0]}.json").writeText(if (node == null) "NONE $problem" else runCatching { RoutingConfig.build(node, RoutePolicy()) }.getOrElse { "ERR: ${it.message}" })
        }
        File(dir, "links.txt").readLines().filter { it.isNotBlank() }.forEach { line ->
            val name = line.substringBefore('\t')
            val input = SubscriptionInput.unwrap(line.substringAfter('\t').replace("\\n", "\n"))
            val node = NodeShareParser.parse(input).firstOrNull()
            File(dir, "$name.json").writeText(if (node == null) "NONE" else runCatching { RoutingConfig.build(node, RoutePolicy()) }.getOrElse { "ERR: ${it.message}" })
        }
    }
}
