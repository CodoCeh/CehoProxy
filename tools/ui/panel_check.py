#!/usr/bin/env python3
"""Проверка панели CehoProxy реальными кликами в Chrome.

Запуск: tools/ui/.venv/bin/python tools/ui/panel_check.py http://127.0.0.1:8899 [--shots папка]
Работает на русском и английском интерфейсе (язык берётся со страницы). Только лабораторные машины.
Состояние не меняется: ошибочные формы отклоняются, режим (простой/профи) возвращается как был.
Код возврата 1, если что-то не прошло.
"""
import re
import sys
import pathlib
from urllib.parse import quote
from playwright.sync_api import sync_playwright

base = (sys.argv[1] if len(sys.argv) > 1 else "http://127.0.0.1:8899").rstrip("/")
shots = pathlib.Path(sys.argv[sys.argv.index("--shots") + 1]) if "--shots" in sys.argv else None
if shots:
    shots.mkdir(parents=True, exist_ok=True)
failures = []
BAD = re.compile(r"\{\d\}|undefined|NaN")
MSG = {
    "ru": {"mismatch": "не совпали", "no_parts": "Отметьте, что переносить", "no_path": "Такого пути нет",
           "bad_link": "не похоже на ссылку"},
    "en": {"mismatch": "do not match", "no_parts": "Choose what to move", "no_path": "No such path",
           "bad_link": "does not look like a link"},
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


with sync_playwright() as p:
    browser = p.chromium.launch(channel="chrome", headless=True)
    ctx = browser.new_context(viewport={"width": 1280, "height": 900})
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

    # Режим: запоминаем и возвращаем.
    modes = page.locator("form[action='/mode'] button")
    start_pro = modes.nth(1).get_attribute("class") or ""
    was_pro = "on" in start_pro.split() or "active" in start_pro
    modes.nth(1).click()
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
    page.locator("button[aria-label^='Тема'], button[aria-label^='Theme']").first.click()
    after = page.evaluate("document.documentElement.getAttribute('data-theme')")
    check("кнопка темы меняет тему", before != after, (before, after))
    page.evaluate("localStorage.removeItem('ceho-theme')")

    # Пароль: несовпадение.
    go(page, base + "/?tab=access")
    page.fill("form[action='/password'] input[name=password]", "abc12345")
    page.fill("form[action='/password'] input[name=password2]", "different")
    submit(page, lambda: page.locator("form[action='/password'] button").first.click())
    check("пароли не совпали: сообщение", M["mismatch"] in page.content(), page.url)

    # Перенос: без выбранных частей, и галочки переживают перезагрузку.
    go(page, base + "/?tab=access")
    boxes = page.locator(".transfer-parts").first.locator("input[type=checkbox]")
    n = boxes.count()
    for i in range(n):
        boxes.nth(i).uncheck()
    boxes.nth(0).check()
    page.fill("form[action='/settings/export'] input[name=password]", "filepass1")
    page.fill("form[action='/settings/export'] input[name=password2]", "otherpass")
    submit(page, lambda: page.locator("form[action='/settings/export'] button[type=submit], form[action='/settings/export'] button").last.click())
    boxes = page.locator(".transfer-parts").first.locator("input[type=checkbox]")
    state = [boxes.nth(i).is_checked() for i in range(boxes.count())]
    check("перенос: галочки сохранились после ошибки", state == [True] + [False] * (n - 1), state)

    # Программы: несуществующий путь.
    go(page, base + "/?tab=apps")
    page.fill("form[action='/apps/add'] input[name=path]", r"C:\net\takogo\net.exe")
    submit(page, lambda: page.locator("form[action='/apps/add'] button").first.click())
    check("программа по несуществующему пути отклонена", M["no_path"] in page.content(), page.url)

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

    # Узкий экран.
    mobile = browser.new_context(viewport={"width": 375, "height": 812})
    m = mobile.new_page()
    for tab in ("state", "apps", "subs"):
        go(m, f"{base}/?tab={tab}")
        o = m.evaluate("document.documentElement.scrollWidth - innerWidth")
        check(f"телефон, вкладка {tab}: без горизонтальной прокрутки", o <= 0, o)

    # Режим обратно.
    if not was_pro:
        go(page, base + "/")
        page.locator("form[action='/mode'] button").nth(0).click()

    check("в консоли браузера нет ошибок", not [e for e in errors if "favicon" not in e], errors[:3])
    browser.close()

if failures:
    print(f"\nПровалено: {len(failures)}")
    sys.exit(1)
print("\nВсе проверки пройдены")
