#!/usr/bin/env python3
"""Дымовой тест первой настройки: то, что вызывают установщики на всех системах.

Запуск: python3 tests/smoke/setup_smoke.py [путь к cehoproxy]
Без пути запускает `dotnet run` из исходников (нужна предварительная сборка Release).
Настройки живут во временной папке, боевые не трогаются.
"""
import http.server
import os
import shutil
import subprocess
import sys
import tempfile
import threading

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

NODE = "vless://11111111-1111-1111-1111-111111111111@127.0.0.1:443?type=tcp&security=none#smoke"
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
exe_arg = sys.argv[1] if len(sys.argv) > 1 else None
failures = []


class Handler(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        body = (NODE + "\n").encode()
        self.send_response(200)
        self.send_header("Content-Type", "text/plain")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, *args):
        pass


server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Handler)
threading.Thread(target=server.serve_forever, daemon=True).start()
url = f"http://127.0.0.1:{server.server_address[1]}/sub"


def chp(home, *args):
    env = dict(os.environ, CEHOPROXY_HOME=home, NO_COLOR="1", DOTNET_NOLOGO="1")
    if exe_arg:
        cmd = [exe_arg, *args]
    else:
        cmd = ["dotnet", "run", "--project", os.path.join(ROOT, "src", "ProxyCage.Cli"),
               "-c", "Release", "--no-build", "--", *args]
    proc = subprocess.run(cmd, env=env, capture_output=True, text=True, encoding="utf-8", errors="replace",
                          timeout=180, stdin=subprocess.DEVNULL)
    return proc.returncode, proc.stdout + proc.stderr


def check(name, ok, detail=""):
    print(("ok   " if ok else "FAIL ") + name + ("" if ok else f"  -> {detail[:300]}"))
    if not ok:
        failures.append(name)


def fresh():
    return tempfile.mkdtemp(prefix="chp-smoke-")


# 1. Рабочая ссылка остаётся в списке подписок.
home = fresh()
rc, out = chp(home, "setup", "--lang", "en", "--sub", url)
check("setup --sub: код 0", rc == 0, out)
rc, out = chp(home, "subs")
check("setup --sub: подписка осталась и отмечена рабочей", "works" in out, out)
shutil.rmtree(home, ignore_errors=True)

# 2. Нерабочая ссылка не остаётся.
home = fresh()
rc, out = chp(home, "setup", "--lang", "en", "--sub", "http://127.0.0.1:9/none")
check("setup --sub с мёртвой ссылкой: ненулевой код", rc != 0, out)
rc, out = chp(home, "subs")
check("setup --sub с мёртвой ссылкой: подписка не сохранена", "works" not in out and "http://127.0.0.1:9" not in out, out)
shutil.rmtree(home, ignore_errors=True)

# 3. Программа по пути добавляется.
home = fresh()
target = shutil.which("python3") or sys.executable
rc, out = chp(home, "setup", "--lang", "en", "--app", target)
check("setup --app: код 0", rc == 0, out)
rc, out = chp(home, "apps")
check("setup --app: программа в списке", os.path.basename(os.path.dirname(os.path.realpath(target))) in out
      or "python" in out.lower(), out)
shutil.rmtree(home, ignore_errors=True)

# 4. add-app all не падает на машине без известных программ.
home = fresh()
rc, out = chp(home, "add-app", "all")
check("add-app all: без падения", "Exception" not in out and "Unhandled" not in out, out)
shutil.rmtree(home, ignore_errors=True)

# 5. detect-apps печатает строки R|B|M|O<TAB>имя<TAB>путь.
home = fresh()
rc, out = chp(home, "detect-apps")
rows = [l for l in out.splitlines() if l.strip()]
check("detect-apps: код 0 и формат строк", rc == 0 and all(len(l.split("\t")) == 3 and l[0] in "RBMO" for l in rows), out)
shutil.rmtree(home, ignore_errors=True)

server.shutdown()
if failures:
    print(f"\nПровалено: {len(failures)}")
    sys.exit(1)
print("\nВсе проверки пройдены")
