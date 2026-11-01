package ru.cebers.cehoproxy.core

import org.json.JSONObject

/** Chosen subscriptions sent from a phone to another device; the receiver adds them to what it already has. */
internal object SubscriptionShare {
    const val FORMAT = "cehoproxy-share-v1"
    /** Key of the servers that were added as single links, outside any subscription. */
    const val OWN_SERVERS = ""

    data class Group(val key: String, val title: String, val nodes: List<ProxyNode>)
    data class Incoming(val urls: List<String>, val nodes: List<ProxyNode>)

    fun groups(profile: ClientProfile): List<Group> {
        val bySubscription = profile.subscriptionUrls.map { url ->
            val nodes = profile.nodes.filter { SubscriptionMembership.belongs(it, url, profile.subscriptionUrls) }
            Group(url, nodes.firstNotNullOfOrNull { it.subscriptionMetadata.title } ?: SubscriptionRemovalPolicy.hostOf(url) ?: url, nodes)
        }
        val claimed = bySubscription.flatMap { it.nodes }.map { it.raw }.toSet()
        val own = profile.nodes.filter { it.raw !in claimed }
        return bySubscription + listOfNotNull(Group(OWN_SERVERS, "Отдельные серверы", own).takeIf { own.isNotEmpty() })
    }

    fun encode(profile: ClientProfile, keys: Set<String>): String {
        val chosen = groups(profile).filter { it.key in keys }
        val payload = ClientProfile(nodes = chosen.flatMap { it.nodes }.distinctBy { it.raw }, subscriptionUrls = chosen.map { it.key }.filter { it != OWN_SERVERS })
        return JSONObject().put("format", FORMAT).put("profile", JSONObject(ProfileCodec.encode(payload))).toString()
    }

    /** Also reads the whole-profile backup that version 0.1.15 sent. */
    fun decode(text: String): Incoming? = runCatching {
        val json = JSONObject(text.trim())
        val profile = when (json.optString("format")) {
            FORMAT -> ProfileCodec.decode(json.getJSONObject("profile").toString())
            ProfileBackup.FORMAT -> ProfileBackup.decode(text)
            else -> null
        } ?: return null
        Incoming(profile.subscriptionUrls, profile.nodes).takeIf { it.nodes.isNotEmpty() || it.urls.isNotEmpty() }
    }.getOrNull()

    fun merge(current: ClientProfile, incoming: Incoming): ClientProfile {
        if (current.importedConfig != null) return current
        val known = current.nodes.map { it.raw }.toSet()
        return current.copy(
            nodes = current.nodes + incoming.nodes.filter { it.raw !in known }.distinctBy { it.raw },
            subscriptionUrls = (current.subscriptionUrls + incoming.urls).distinct(),
        )
    }

    fun summary(incoming: Incoming): List<Pair<String, Int>> {
        val profile = ClientProfile(nodes = incoming.nodes, subscriptionUrls = incoming.urls)
        return groups(profile).map { it.title to it.nodes.size }
    }
}
