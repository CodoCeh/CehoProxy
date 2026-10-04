#!/usr/bin/env python3
"""Снимки панели CehoProxy только на чтение: ничего не нажимает и не меняет.

Запуск: tools/ui/.venv/bin/python tools/ui/shots.py http://127.0.0.1:ПОРТ_НА_МАШИНЕ ПРОБРОШЕННЫЙ_ПОРТ ПАПКА [метка]
Для каждой вкладки: ширина 1280 и 375, светлая и тёмная тема. Проверяет шаблонные остатки, ширину страницы,
ошибки скрипта страницы. Код возврата 1, если что-то не прошло.
"""
import pathlib
import re
import sys
import time
import urllib.error
import urllib.request

from playwright.sync_api import sync_playwright

base = sys.argv[1].rstrip("/")
via = sys.argv[2]
out = pathlib.Path(sys.argv[3])
label = sys.argv[4] if len(sys.argv) > 4 else "machine"
out.mkdir(parents=True, exist_ok=True)
BAD = re.compile(r"\{\d\}|undefined|NaN")
failures = []


def check(name, ok, detail=""):
    print(("ok   " if ok else "FAIL ") + name + ("" if ok else f"  -> {str(detail)[:200]}"))
    if not ok:
        failures.append(name)


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
    for attempt in range(20):
        try:
            resp = opener.open(req, timeout=60)
            break
        except urllib.error.HTTPError as e:
            resp = e
            break
        except (urllib.error.URLError, ConnectionError, TimeoutError):
            if attempt == 19:
                raise
            time.sleep(3)
    body = resp.read()
    headers_out = {k: v for k, v in resp.headers.items()
                   if k.lower() not in ("content-length", "transfer-encoding", "connection")}
    route.fulfill(status=resp.status, headers=headers_out, body=body)


with sync_playwright() as p:
    browser = p.chromium.launch(channel="chrome", headless=True)
    tabs = None
    for scheme in ("light", "dark"):
        for width, height in ((1280, 900), (375, 800)):
            ctx = browser.new_context(viewport={"width": width, "height": height}, color_scheme=scheme)
            ctx.route(base + "/**", handle)
            page = ctx.new_page()
            errors = []
            page.on("pageerror", lambda e: errors.append(str(e)))
            page.on("console", lambda m: errors.append(m.text) if m.type == "error" else None)
            page.goto(base + "/", wait_until="load")
            if tabs is None:
                tabs = sorted(set(re.findall(r'href="/\?tab=([a-z]+)', page.content())) or {"state"}) 
                tabs = ["state"] + [t for t in tabs if t != "state"]
                check(f"{label}: найдены вкладки", len(tabs) >= 3, tabs)
            for tab in tabs:
                page.goto(f"{base}/?tab={tab}", wait_until="load")
                page.wait_for_timeout(600)
                html = re.sub(r"<script.*?</script>", "", page.content(), flags=re.S)
                over = page.evaluate("document.documentElement.scrollWidth - innerWidth")
                tag = f"{label}-{tab}-{scheme}-{width}"
                check(f"{tag}: без шаблонных остатков", not BAD.search(html))
                check(f"{tag}: страница не шире экрана", over <= 0, over)
                clash = page.evaluate("""() => [...document.querySelectorAll('li.app-observation, article.app-card')].filter(li => {
                    const a = li.querySelector('.app-identity'), b = li.querySelector('.app-observation-body');
                    if (!a || !b) return false;
                    const x = a.getBoundingClientRect(), y = b.getBoundingClientRect();
                    const sameRow = x.top < y.bottom && y.top < x.bottom;
                    return sameRow && x.right > y.left + 1 && x.left < y.right;
                }).length""")
                check(f"{tag}: имя программы не налезает на статус", clash == 0, clash)
                page.screenshot(path=str(out / f"{tag}.png"), full_page=True)
            check(f"{label} {scheme} {width}: без ошибок скрипта", not errors, errors[:3])
            ctx.close()
    browser.close()

print("Все проверки пройдены" if not failures else f"Провалено: {len(failures)}")
sys.exit(1 if failures else 0)
