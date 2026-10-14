package ru.cebers.cehoproxy

import org.junit.Assert.assertEquals
import org.junit.Test

class ThemePreferenceTest {
    @Test fun everyThemePreferenceRoundTripsThroughStorageValue() {
        ThemePreference.entries.forEach { option ->
            assertEquals(option, ThemePreference.fromStorage(option.storageValue))
        }
    }

    @Test fun missingOrUnknownPreferenceStartsWithTheLightRedesign() {
        assertEquals(ThemePreference.DARK, ThemePreference.fromStorage(null))
        assertEquals(ThemePreference.DARK, ThemePreference.fromStorage("unexpected"))
    }
}
