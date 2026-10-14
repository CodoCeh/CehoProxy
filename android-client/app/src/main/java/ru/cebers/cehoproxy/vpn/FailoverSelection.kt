package ru.cebers.cehoproxy.vpn

import ru.cebers.cehoproxy.core.ClientProfile

internal object FailoverSelection {
    fun apply(current: ClientProfile, started: ClientProfile, selectedRaw: String, generationCurrent: Boolean): ClientProfile {
        if (!generationCurrent || !current.autoRotationEnabled || current.nodes.map { it.raw } != started.nodes.map { it.raw }) return current
        val index = current.nodes.indexOfFirst { it.raw == selectedRaw }
        return if (index >= 0) current.copy(selected = index) else current
    }
}
