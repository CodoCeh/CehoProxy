package ru.cebers.cehoproxy.vpn

import io.nekohasekai.libbox.Libbox
import org.junit.Assert.assertEquals
import org.junit.Test

class InterfaceTypePolicyTest {
    @Test fun knownPhysicalInterfacesUseNativeNamedTypesAndLoopbackIsOther() {
        assertEquals(Libbox.InterfaceTypeWIFI, InterfaceTypePolicy.forName("wlan0"))
        assertEquals(Libbox.InterfaceTypeCellular, InterfaceTypePolicy.forName("rmnet_data0"))
        assertEquals(Libbox.InterfaceTypeEthernet, InterfaceTypePolicy.forName("eth0"))
        assertEquals(Libbox.InterfaceTypeOther, InterfaceTypePolicy.forName("lo"))
        assertEquals(0, InterfaceTypePolicy.forName("wlan0"))
        assertEquals(3, InterfaceTypePolicy.forName("lo"))
    }
}
