package ru.cebers.cehoproxy.core

internal object DirectSitePresets {
    val tunnelSites: List<String> = listOf(
        "youtube.com", "google.com", "gmail.com", "instagram.com",
        "facebook.com", "x.com", "twitter.com", "chatgpt.com",
        "openai.com", "claude.ai", "discord.com", "netflix.com",
        "spotify.com", "reddit.com", "tiktok.com", "twitch.tv",
        "linkedin.com", "wikipedia.org",
    )

    val russianSites: List<String> = listOf(
        "gosuslugi.ru", "госуслуги.рф", "nalog.gov.ru", "nalog.ru",
        "mos.ru", "gov.ru", "pfr.gov.ru", "sfr.gov.ru",
        "fssp.gov.ru", "rosreestr.gov.ru", "rosreestr.ru", "mvd.ru",
        "мвд.рф", "kremlin.ru", "президент.рф", "government.ru",
        "правительство.рф", "cikrf.ru", "duma.gov.ru", "rkn.gov.ru",
        "goskey.ru", "mil.ru", "customs.gov.ru", "zakupki.gov.ru",
        "torgi.gov.ru", "pochta.ru", "russianpost.ru", "cbr.ru",
        "moex.com", "consultant.ru", "garant.ru", "max.ru",
        "web.max.ru", "oneme.ru", "vk.com", "vk.ru",
        "vkontakte.ru", "vk-cdn.net", "userapi.com", "vkuseraudio.net",
        "vkuservideo.net", "vkplay.ru", "vkplay.live", "vkvideo.ru",
        "ok.ru", "mycdn.me", "okcdn.ru", "mail.ru",
        "my.mail.ru", "imgsmail.ru", "tamtam.chat", "pikabu.ru",
        "habr.com", "vc.ru", "dtf.ru", "yandex.ru",
        "ya.ru", "yandex.net", "yandex.by", "yandex.kz",
        "yandex.com", "yastatic.net", "dzen.ru", "kinopoisk.ru",
        "2gis.ru", "2gis.com", "sberbank.ru", "sber.ru",
        "sberbank.com", "sbrf.ru", "sbermarket.ru", "megamarket.ru",
        "sbermegamarket.ru", "zvuk.com", "tinkoff.ru", "tbank.ru",
        "vtb.ru", "alfabank.ru", "gazprombank.ru", "gpb.ru",
        "raiffeisen.ru", "rshb.ru", "psbank.ru", "sovcombank.ru",
        "mkb.ru", "open.ru", "rosbank.ru", "nspk.ru",
        "mironline.ru", "qiwi.com", "yoomoney.ru", "yookassa.ru",
        "cloudpayments.ru", "robokassa.ru", "banki.ru", "sravni.ru",
        "ozon.ru", "wildberries.ru", "wb.ru", "avito.ru",
        "youla.ru", "lamoda.ru", "dns-shop.ru", "citilink.ru",
        "mvideo.ru", "eldorado.ru", "aliexpress.ru", "detmir.ru",
        "leroymerlin.ru", "letu.ru", "goldapple.ru", "vkusvill.ru",
        "perekrestok.ru", "magnit.ru", "5ka.ru", "samokat.ru",
        "delivery-club.ru", "cdek.ru", "boxberry.ru", "5post.ru",
        "playerok.com", "funpay.com", "plati.market", "plati.ru",
        "digiseller.ru", "ggsel.net", "zaka-zaka.com", "lolz.guru",
        "lzt.market", "my.games", "4game.com", "lesta.ru",
        "rustore.ru", "rutube.ru", "ivi.ru", "okko.tv",
        "kion.ru", "wink.ru", "premier.one", "more.tv",
        "start.ru", "smotrim.ru", "1tv.ru", "ntv.ru",
        "vesti.ru", "ria.ru", "tass.ru", "rbc.ru",
        "kommersant.ru", "rg.ru", "gazeta.ru", "lenta.ru",
        "iz.ru", "mk.ru", "kp.ru", "aif.ru",
        "rzd.ru", "aeroflot.ru", "s7.ru", "pobeda.aero",
        "utair.ru", "tutu.ru", "ostrovok.ru", "sutochno.ru",
        "aviasales.ru", "mts.ru", "beeline.ru", "megafon.ru",
        "tele2.ru", "rostelecom.ru", "rt.ru", "yota.ru",
        "dom.ru", "hh.ru", "superjob.ru", "rabota.ru",
        "gb.ru", "skillbox.ru", "netology.ru", "stepik.org",
        "uchi.ru", "sdamgia.ru", "1c.ru", "sbis.ru",
        "kontur.ru", "diadoc.ru",
        "x5.ru", "lenta.com", "auchan.ru", "metro-cc.ru", "kazanexpress.ru", "oldi.ru", "technodom.ru", "sportmaster.ru", "eapteka.ru", "apteka.ru", "zdravcity.ru", "holodilnik.ru", "dodopizza.ru", "cian.ru", "domclick.ru", "drom.ru", "auto.ru", "otpbank.ru", "uralsib.ru", "bspb.ru", "mtsbank.ru", "rencredit.ru", "homecredit.ru", "pochtabank.ru", "akbars.ru", "vtb24.ru", "minfin.gov.ru", "minzdrav.gov.ru", "mosreg.ru", "gov.spb.ru", "fssprus.ru", "sudrf.ru", "pravo.gov.ru", "arbitr.ru", "gibdd.ru", "trudvsem.ru", "edu.gov.ru", "fipi.ru", "ttk.ru", "citymobil.ru", "maxim.ru", "dellin.ru", "pecom.ru", "dpd.ru", "smotreshka.tv", "amediateka.ru", "forbes.ru", "foxford.ru", "dnevnik.ru", "zarplata.ru", "gaijin.net", "warthunder.com", "gitverse.ru", "selectel.ru", "timeweb.ru", "reg.ru", "beget.com", "cloud.ru",
    )

    fun forMode(onlySites: Boolean): List<String> = if (onlySites) tunnelSites else russianSites

    fun newHosts(listed: List<String>, incoming: List<String>): List<String> {
        val known = listed.map(RoutingConfig::normalizeDomain).toMutableSet()
        return incoming.filter { known.add(RoutingConfig.normalizeDomain(it)) }
    }
}
