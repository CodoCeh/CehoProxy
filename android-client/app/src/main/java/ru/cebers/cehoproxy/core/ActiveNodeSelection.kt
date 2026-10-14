package ru.cebers.cehoproxy.core

internal object ActiveNodeSelection {
    fun sync(ui: ClientProfile, stored: ClientProfile): ClientProfile {
        if (!stored.autoRotationEnabled || ui.importedConfig != null || stored.importedConfig != null || ui.nodes.map { it.raw } != stored.nodes.map { it.raw }) return ui
        if (stored.selected !in ui.nodes.indices) return ui
        return ui.copy(selected = stored.selected)
    }

    fun beforeSave(previous: ClientProfile, next: ClientProfile, stored: ClientProfile, explicitSelection: Boolean): ClientProfile {
        if (explicitSelection || !previous.autoRotationEnabled || next.nodes.map { it.raw } != previous.nodes.map { it.raw }) return next
        val active = sync(previous, stored)
        return next.copy(selected = active.selected)
    }
}
