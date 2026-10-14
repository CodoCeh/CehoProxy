package ru.cebers.cehoproxy

internal object DiagnosticNodeIndices {
    fun parse(argument: String?, nodeCount: Int): List<Int> {
        val selected = if (argument == null) listOf(0, 1, 2, 4) else {
            require(argument.length <= 2048) { "diagnosticNodeIndices is too long" }
            val entries = argument.split(',').map(String::trim)
            require(entries.isNotEmpty() && entries.all { it.matches(Regex("[0-9]{1,3}")) }) {
                "diagnosticNodeIndices must contain comma-separated indices"
            }
            entries.map(String::toInt)
        }
        require(selected.size <= 501 && selected.distinct().size == selected.size && selected.all { it in 0..500 }) {
            "diagnosticNodeIndices must contain unique indices from 0 to 500"
        }
        require(selected.all { it < nodeCount }) { "diagnosticNodeIndices contains an index outside the snapshot" }
        return selected
    }
}
