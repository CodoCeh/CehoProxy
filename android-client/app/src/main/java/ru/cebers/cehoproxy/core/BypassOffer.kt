package ru.cebers.cehoproxy.core

/** What the user is told when a site or an application does not work through the tunnel but works directly. */
internal object BypassOffer {
    enum class Kind(val wire: String) {
        SITE("site"), APP("app"), TUNNEL("tunnel");

        companion object { fun from(wire: String?): Kind? = entries.firstOrNull { it.wire == wire } }
    }

    data class Text(val title: String, val body: String, val confirm: String, val never: String)

    fun hintKey(kind: Kind, target: String): String = when (kind) { Kind.APP -> "app:$target"; Kind.TUNNEL -> "tunnel:$target"; Kind.SITE -> target }

    fun text(kind: Kind, label: String): Text = when (kind) {
        Kind.SITE -> Text(
            title = "$label не открывается",
            body = "Через VPN сайт не отвечает, а напрямую открывается. Если открывать его мимо VPN, он загрузится сразу. Остальные сайты останутся под защитой.",
            confirm = "Открывать напрямую",
            never = "Не предлагать",
        )
        Kind.TUNNEL -> Text(
            title = "$label не открывается без VPN",
            body = "Сайт не отвечает напрямую. Часть сайтов закрыта для российских адресов. Если открывать его через VPN, он может заработать. Остальные сайты останутся как были.",
            confirm = "Открывать через VPN",
            never = "Не предлагать",
        )
        Kind.APP -> Text(
            title = "«$label» не работает через VPN",
            body = "Приложение не получает ответ, пока включён VPN, а напрямую связь есть. Часть банков и игр не принимает VPN. Если пустить его мимо VPN, оно заработает. Остальные приложения останутся под защитой.",
            confirm = "Пустить мимо VPN",
            never = "Не предлагать",
        )
    }
}
