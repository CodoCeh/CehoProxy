#!/usr/bin/env python3
"""Проверка панели CehoProxy реальными кликами в Chrome.

Запуск: tools/ui/.venv/bin/python tools/ui/panel_check.py http://127.0.0.1:8899 [--shots папка] [--via ПОРТ]
Работает на русском и английском интерфейсе (язык берётся со страницы). Только лабораторные машины.
Состояние не меняется: ошибочные формы отклоняются, режим (простой/профи) возвращается как был.
Код возврата 1, если что-то не прошло.

--full: сверх этого проходит настоящий сценарий на самой машине (только лабораторные!): добавляет две программы и
«Свой сервер», убирает «Свой сервер», нажимает «Применить» (одно применение на пачку правок), проверяет, что защита
работает, выключает и включает её, убирает обе программы, снова применяет и возвращает исходное состояние.
--apps ПУТЬ1,ПУТЬ2 задаёт две программы для добавления (по умолчанию подбираются под систему).
"""
import re
import sys
import time
import pathlib
from urllib.parse import quote
from playwright.sync_api import sync_playwright

base = (sys.argv[1] if len(sys.argv) > 1 else "http://127.0.0.1:8899").rstrip("/")
via = sys.argv[sys.argv.index("--via") + 1] if "--via" in sys.argv else None
shots = pathlib.Path(sys.argv[sys.argv.index("--shots") + 1]) if "--shots" in sys.argv else None
if shots:
    shots.mkdir(parents=True, exist_ok=True)
FULL = "--full" in sys.argv
APPS = sys.argv[sys.argv.index("--apps") + 1].split(",") if "--apps" in sys.argv else None
failures = []
BAD = re.compile(r"\{\d\}|undefined|NaN")
MSG = {
    "ru": {"mismatch": "не совпали", "no_parts": "Отметьте, что переносить", "no_path": "Такого пути нет",
           "bad_link": "не похоже на ссылку", "applied": "Защита перезапущена", "off": "Защита выключена",
           "connected": "Туннель подключён", "already": "уже есть права администратора"},
    "en": {"mismatch": "do not match", "no_parts": "Choose what to move", "no_path": "No such path",
           "bad_link": "does not look like a link", "applied": "Protection restarted", "off": "Protection is off",
           "connected": "Tunnel connected", "already": "already has administrator rights"},
}


def check(name, ok, detail=""):
    print(("ok   " if ok else "FAIL ") + name + ("" if ok else f"  -> {str(detail)[:200]}"))
    if not ok:
        failures.append(name)


def banner(page):
    el = page.locator(".msg, .banner, [role=alert], .alert").first
    return el.inner_text() if el.count() else ""


def go(pg, url):
    for attempt in (1, 2):
        try:
            pg.goto(url, wait_until="domcontentloaded")
            pg.wait_for_load_state("load")
            return
        except Exception:
            if attempt == 2:
                raise
            pg.wait_for_timeout(1500)


def submit(pg, action):
    """Нажимает отправку формы и дожидается перехода на страницу с сообщением (параметр m)."""
    with pg.expect_navigation(wait_until="load", timeout=30000):
        action()


def overflow(page):
    return page.evaluate("""() => {
      const sc=[...document.querySelectorAll('.scroll')].filter(e=>e.scrollWidth>e.clientWidth+1).length;
      return {doc: document.documentElement.scrollWidth - innerWidth, tables: sc};
    }""")


def install_proxy(context):
    """--via ПОРТ: страница открывается по адресу base, а запросы идут на проброшенный порт (если свой порт занят)."""
    import urllib.request
    import urllib.error

    class NoRedirect(urllib.request.HTTPRedirectHandler):
        def redirect_request(self, *a, **k):
            return None

    opener = urllib.request.build_opener(NoRedirect)

    def handle(route):
        r = route.request
        headers = {k: v for k, v in r.headers.items() if k.lower() not in ("host", "accept-encoding")}
        headers["Host"] = base.replace("http://", "")
        req = urllib.request.Request(r.url.replace(base, "http://127.0.0.1:" + via, 1),
                                     data=r.post_data_buffer, method=r.method, headers=headers)
        resp = None
        for attempt in range(40):
            try:
                resp = opener.open(req, timeout=90)
                break
            except urllib.error.HTTPError as e:
                resp = e
                break
            except (urllib.error.URLError, ConnectionError, TimeoutError):
                if attempt == 39:
                    raise
                time.sleep(3)
        body = resp.read()
        out = {k: v for k, v in resp.headers.items()
               if k.lower() not in ("content-length", "transfer-encoding", "connection")}
        if resp.status in (301, 302, 303, 307):
            where = out.pop("Location", "/")
            route.fulfill(status=200, headers={**out, "Content-Type": "text/html"},
                          body=f"<script>location.replace({where!r})</script>".encode())
            return
        route.fulfill(status=resp.status, headers=out, body=body)

    context.route(base + "/**", handle)


def full_scenario(pg, M):
    pg.on("dialog", lambda d: d.accept())

    def body():
        return pg.locator("body").inner_text()

    def wait_for(pred, seconds, every=3):
        end = __import__("time").time() + seconds
        while __import__("time").time() < end:
            try:
                if pred():
                    return True
            except Exception:
                pass
            pg.wait_for_timeout(every * 1000)
        return False

    def reload_tab(tab):
        go(pg, f"{base}/?tab={tab}")

    def label_of(path):
        return re.split(r"[\\/]", path)[-1].rsplit(".", 1)[0].lower()

    def add_app(path):
        reload_tab("apps")
        pg.evaluate("document.querySelectorAll('details').forEach(d => d.open = true)")
        pg.fill("form[action='/apps/add'] input[name=path]", path)
        pg.locator("form[action='/apps/add'] button").first.click()
        pg.wait_for_selector("#tunnel-confirm-add", state="visible", timeout=15000)
        pg.locator("#tunnel-confirm-add").click()
        return wait_for(lambda: label_of(path) in body().lower() and M["no_path"] not in body(), 30, 1)

    def remove_app(path):
        reload_tab("apps")
        card = pg.locator("article.app-card").filter(has_text=label_of(path)).first
        if not card.count():
            return False
        card.evaluate("e => e.querySelectorAll('details').forEach(d => d.open = true)")
        with pg.expect_navigation(wait_until="load", timeout=30000):
            card.locator("form[action='/apps/remove'] button").first.click()
        return True

    def pending_count():
        el = pg.locator("form.pending")
        if not el.count():
            return 0
        m = re.search(r"(\d+)", el.first.inner_text())
        return int(m.group(1)) if m else 1

    def apply_pending(seconds=480):
        pg.locator("form.pending button").first.click()
        return wait_for(lambda: M["applied"] in body() and not pg.locator("form.pending").count(), seconds, 3)

    def connected(seconds=300):
        def ok():
            reload_tab("state")
            return M["connected"] in body()
        return wait_for(ok, seconds, 5)

    reload_tab("apps")
    is_win = "C:\\" in (pg.evaluate("(document.querySelector('form[action=\"/apps/add\"] input[name=path]')||{}).placeholder||''") or "")
    is_mac = "/Applications" in (pg.evaluate("(document.querySelector('form[action=\"/apps/add\"] input[name=path]')||{}).placeholder||''") or "")
    paths = APPS or ([r"C:\Windows\System32\notepad.exe", r"C:\Windows\System32\cmd.exe"] if is_win
                     else ["/usr/bin/nc", "/usr/bin/host"] if is_mac else ["/usr/bin/wget", "/usr/bin/ssh"])
    reload_tab("state")
    was_running = pg.locator("button[formaction='/control/stop']").count() > 0
    print(f"сценарий на самой машине: защита {'включена' if was_running else 'выключена'}, программы {paths}")
    reload_tab("apps")
    busy = [x for x in paths if pg.locator("article.app-card").filter(has_text=label_of(x)).count()]
    if busy:
        check("сценарий: выбранные программы ещё не добавлены на машине", False, busy)
        return
    base_pending = pending_count()
    try:
        check("пакет: первая программа добавлена", add_app(paths[0]), body()[:200])
        reload_tab("subs")
        pg.locator("form[action='/subs/add'] input[name=kind][value=naive]").evaluate(
            "e => { e.checked = true; e.dispatchEvent(new Event('input', {bubbles: true})); e.dispatchEvent(new Event('change', {bubbles: true})); }")
        pg.fill("form[action='/subs/add'] input[name=name]", "ceho-test")
        pg.fill("form[action='/subs/add'] input[name=server]", "test.invalid")
        pg.fill("form[action='/subs/add'] input[name=username]", "u")
        pg.fill("form[action='/subs/add'] input[name=password]", "p")
        with pg.expect_navigation(wait_until="load", timeout=30000):
            pg.locator("form[action='/subs/add'] button").last.click()
        check("пакет: «Свой сервер» добавлен", "ceho-test" in body(), body()[:200])
        check("пакет: вторая программа добавлена", add_app(paths[1]), body()[:200])
        reload_tab("subs")
        with pg.expect_navigation(wait_until="load", timeout=30000):
            pg.locator("form[action='/subs/remove']").filter(has=pg.locator("input[value='ceho-test']")).first.locator("button").click()
        check("пакет: «Свой сервер» убран", not pg.locator("form[action='/subs/remove']").filter(has=pg.locator("input[value='ceho-test']")).count(), body()[:200])
        reload_tab("state")
        if was_running:
            n = pending_count()
            check("пакет: счётчик неприменённых правок вырос и защита не перезапускалась", n >= base_pending + 3, n)
            check("пакет: видно уведомление «Применить»", pg.locator("form.pending").count() > 0)
            check("применение одной кнопкой перезапускает защиту", apply_pending(), body()[:300])
            check("после применения туннель подключён", connected())
        reload_tab("apps")
        check("добавленные программы в списке", all(pg.locator("article.app-card").filter(has_text=label_of(x)).count() for x in paths))
        reload_tab("state")
        check("страница состояния говорит, что заменить ничего не нужно", "undefined" not in body())

        if was_running:
            reload_tab("state")
            pg.locator("button[formaction='/control/stop']").click()
            check("«Выключить» выключает защиту", wait_for(lambda: M["off"] in body() or pg.locator("button[formaction='/control/start']").count() > 0, 180), body()[:200])
            reload_tab("state")
            pg.locator("button[formaction='/control/start']").click()
            check("«Включить» включает защиту", connected(420))

        r = pg.evaluate("fetch('/elevate',{method:'POST',body:new URLSearchParams({tab:'doctor'})}).then(r=>r.text())")
        where = re.search(r"location.replace\('([^']+)'", r)
        if where:
            go(pg, base + where.group(1))
        check("кнопка прав администратора: у запущенной с правами программы просьба не повторяется",
              wait_for(lambda: M["already"] in body(), 40, 2), body()[:300])
    finally:
        for x in paths:
            try:
                remove_app(x)
            except Exception as e:
                failures.append("уборка " + x)
                print("FAIL уборка", x, e)
        reload_tab("state")
        if pg.locator("form.pending").count():
            check("уборка: применение возвращает исходные правила", apply_pending(), body()[:300])
        reload_tab("apps")
        check("уборка: добавленных программ не осталось", not any(pg.locator("article.app-card").filter(has_text=label_of(x)).count() for x in paths), body()[:200])
        if was_running:
            check("уборка: защита включена, как была", connected())


with sync_playwright() as p:
    browser = p.chromium.launch(channel="chrome", headless=True)
    ctx = browser.new_context(viewport={"width": 1280, "height": 900})
    if via:
        install_proxy(ctx)
    page = ctx.new_page()
    errors = []
    page.on("pageerror", lambda e: errors.append(str(e)))
    page.on("console", lambda m: errors.append(m.text) if m.type == "error" else None)

    go(page, base + "/")
    lang = page.evaluate("document.documentElement.lang") or "ru"
    lang = "en" if lang.startswith("en") else "ru"
    M = MSG[lang]
    print(f"язык интерфейса: {lang}")
    check("главная открывается", "CehoProxy" in page.title(), page.title())

    # Режим: запоминаем и возвращаем (один переключатель, кнопка меняет режим на противоположный).
    toggle = page.locator("form[action='/mode'] button.mode-toggle")
    was_pro = "pro" != (toggle.get_attribute("value") or "pro")
    if not was_pro:
        toggle.click()
        page.wait_for_load_state()
    tabs_pro = set(re.findall(r'href="/\?tab=([a-z]+)', page.content()))
    check("режим профи показывает вкладки «sites», «exit», «doctor», «access»",
          {"sites", "exit", "doctor", "access"} <= tabs_pro, tabs_pro)

    # Все вкладки: нет шаблонных остатков, нет горизонтальной прокрутки страницы.
    for tab in sorted(tabs_pro | {"browser", "log", "help"}):
        go(page, f"{base}/?tab={tab}")
        html = page.content()
        o = overflow(page)
        check(f"вкладка {tab}: без остатков шаблона", not BAD.search(re.sub(r"<script.*?</script>", "", html, flags=re.S)))
        check(f"вкладка {tab}: страница не шире экрана и таблицы без прокрутки", o["doc"] <= 0 and o["tables"] == 0, o)
        if shots:
            page.screenshot(path=str(shots / f"{tab}.png"), full_page=True)

    # Тема.
    go(page, base + "/")
    before = page.evaluate("document.documentElement.getAttribute('data-theme')")
    page.locator("#theme").click()
    after = page.evaluate("document.documentElement.getAttribute('data-theme')")
    check("кнопка темы меняет тему", before != after, (before, after))
    page.evaluate("localStorage.removeItem('ceho-theme')")

    def open_details(pg):
        pg.evaluate("document.querySelectorAll('details').forEach(d => d.open = true)")

    # Туннель: каталог, поиск, окно подтверждения открывается и закрывается без добавления.
    go(page, base + "/?tab=apps")
    cards = page.locator("form.app-pick button.app-card")
    n_cards = cards.count()
    if n_cards:
        page.fill("#tunnel-search", "zzzzqqqq")
        check("каталог: поиск без совпадений скрывает карточки",
              page.locator("form.app-pick button.app-card:visible").count() == 0, page.locator("form.app-pick button.app-card:visible").count())
        page.fill("#tunnel-search", "")
        check("каталог: пустой поиск возвращает карточки", page.locator("form.app-pick button.app-card:visible").count() == n_cards)
        cards.first.click()
        confirm = page.locator("#tunnel-confirm-add")
        check("каталог: клик по карточке показывает подтверждение", confirm.first.is_visible())
        page.locator("#tunnel-cancel").click()
        check("каталог: «Отмена» закрывает подтверждение", not confirm.first.is_visible())
    page.locator("#tunnel-choose").click()
    check("«Выбрать программу» не ломает страницу", page.locator("body").inner_text().strip() != "")

    # Пароль: несовпадение.
    go(page, base + "/?tab=access")
    open_details(page)
    page.fill("form[action='/password'] input[name=password]", "abc12345")
    page.fill("form[action='/password'] input[name=password2]", "different")
    submit(page, lambda: page.locator("form[action='/password'] button").first.click())
    check("пароли не совпали: сообщение", M["mismatch"] in page.content(), page.url)

    # Перенос: без выбранных частей, и галочки переживают перезагрузку.
    go(page, base + "/?tab=access")
    open_details(page)
    box_sel = "form[action='/settings/export'] input[type=checkbox]"
    boxes = page.locator(box_sel)
    n = boxes.count()
    for i in range(n):
        boxes.nth(i).uncheck()
    boxes.nth(0).check()
    page.fill("form[action='/settings/export'] input[name=password]", "filepass1")
    page.fill("form[action='/settings/export'] input[name=password2]", "otherpass")
    submit(page, lambda: page.locator("form[action='/settings/export'] button").last.click())
    boxes = page.locator(box_sel)
    state = [boxes.nth(i).is_checked() for i in range(boxes.count())]
    check("перенос: галочки сохранились после ошибки", state == [True] + [False] * (n - 1), state)

    # Программы: несуществующий путь.
    go(page, base + "/?tab=apps")
    open_details(page)
    page.fill("form[action='/apps/add'] input[name=path]", r"C:\net\takogo\net.exe")
    page.locator("form[action='/apps/add'] button").first.click()
    page.wait_for_selector("#tunnel-confirm-add", state="visible", timeout=10000)
    check("путь к программе сначала требует подтверждения", True)
    page.locator("#tunnel-confirm-add").click()
    page.wait_for_timeout(4000)
    check("программа по несуществующему пути отклонена", M["no_path"] in page.locator("body").inner_text(), page.url)

    # Подписки: не ссылка.
    go(page, base + "/?tab=subs")
    page.fill("form[action='/subs/add'] input[name=name]", "Проверка")
    page.fill("form[action='/subs/add'] input[name=url]", "not a url")
    submit(page, lambda: page.locator("form[action='/subs/add'] input[name=url]").press("Enter"))
    check("подписка «not a url» отклонена понятным текстом", M["bad_link"] in page.content(), page.url)

    # Журнал: фильтры.
    go(page, base + "/?tab=log")
    for view in ("important", "all", "ours", "engine", "crashes"):
        go(page, f"{base}/?tab=log&view={view}")
        check(f"журнал, вид {view}", page.locator("body").inner_text().strip() != "")

    if FULL:
        full_scenario(page, M)

    # Узкий экран.
    mobile = browser.new_context(viewport={"width": 375, "height": 812})
    if via:
        install_proxy(mobile)
    m = mobile.new_page()
    for tab in ("state", "apps", "subs"):
        go(m, f"{base}/?tab={tab}")
        o = m.evaluate("document.documentElement.scrollWidth - innerWidth")
        check(f"телефон, вкладка {tab}: без горизонтальной прокрутки", o <= 0, o)

    # Режим обратно.
    if not was_pro:
        go(page, base + "/")
        page.locator("form[action='/mode'] button.mode-toggle").click()

    check("в консоли браузера нет ошибок", not [e for e in errors if "favicon" not in e], errors[:3])
    browser.close()

if failures:
    print(f"\nПровалено: {len(failures)}")
    sys.exit(1)
print("\nВсе проверки пройдены")
