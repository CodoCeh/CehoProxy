package ru.cebers.cehoproxy.vpn

import io.nekohasekai.libbox.Libbox

internal object InterfaceTypePolicy {
    fun forName(name: String): Int = when {
        name.startsWith("wlan") || name.startsWith("wifi") -> Libbox.InterfaceTypeWIFI
        name.startsWith("rmnet") || name.startsWith("ccmni") || name.startsWith("pdp") -> Libbox.InterfaceTypeCellular
        name.startsWith("eth") || name.startsWith("en") -> Libbox.InterfaceTypeEthernet
        else -> Libbox.InterfaceTypeOther
    }
}
