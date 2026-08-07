#!/usr/bin/env python3
"""Собирает архивы релиза «скачал — распаковал — запустил».

Почему не `zip`: он не ставит флаг UTF-8 в записи, и Проводник Windows показывает
русское имя установщика кракозябрами. Почему переводы строк правятся здесь: пакетный
файл Windows обязан быть с CRLF, иначе cmd.exe спотыкается, — а рабочая копия легко
оказывается с LF после любой правки скриптом.
"""
import os, sys, zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
VERSION = sys.argv[1] if len(sys.argv) > 1 else "0.0.0"
BIN = sys.argv[2] if len(sys.argv) > 2 else os.path.join(ROOT, "publish")
OUT = sys.argv[3] if len(sys.argv) > 3 else os.path.join(ROOT, "publish", "bundles")

DOCS = ["README.md", "LICENSE", "THIRD-PARTY.md"]
TARGETS = [
    ("windows",     "cehoproxy-win-x64.exe", "cehoproxy.exe", "Установить CehoProxy.cmd"),
    ("macos-apple", "cehoproxy-osx-arm64",   "cehoproxy",     "Установить CehoProxy.command"),
    ("macos-intel", "cehoproxy-osx-x64",     "cehoproxy",     "Установить CehoProxy.command"),
    ("linux-x64",   "cehoproxy-linux-x64",   "cehoproxy",     "install.sh"),
    ("linux-arm64", "cehoproxy-linux-arm64", "cehoproxy",     "install.sh"),
]


def body(path, text_file):
    """Байты файла. Переводы строк правим ТОЛЬКО у текстовых: у программы
    последовательность 0D 0A — это код, а не конец строки, и «нормализация»
    молча ломает исполняемый файл. Поймано живьём на выложенном архиве."""
    data = open(path, "rb").read()
    if not text_file:
        return data
    text = data.replace(b"\r\n", b"\n")
    return text.replace(b"\n", b"\r\n") if path.endswith((".cmd", ".bat", ".ps1")) else text


def main():
    os.makedirs(OUT, exist_ok=True)
    for suffix, binary, binname, installer in TARGETS:
        name = f"CehoProxy-{VERSION}-{suffix}.zip"
        # третье поле — «запускается», четвёртое — «текстовый файл»
        files = [(os.path.join(BIN, binary), f"CehoProxy/{binname}", True, False),
                 (os.path.join(ROOT, "scripts", installer), f"CehoProxy/{installer}", True, True)]
        files += [(os.path.join(ROOT, d), f"CehoProxy/{d}", False, True) for d in DOCS]

        with zipfile.ZipFile(os.path.join(OUT, name), "w", zipfile.ZIP_DEFLATED, compresslevel=6) as z:
            for src, arc, runnable, text_file in files:
                info = zipfile.ZipInfo(arc)
                info.date_time = (2026, 1, 1, 12, 0, 0)   # без текущего времени архив воспроизводим
                info.compress_type = zipfile.ZIP_DEFLATED
                info.external_attr = (0o755 if runnable else 0o644) << 16
                z.writestr(info, body(src, text_file))
        # сверяем то, что легло в архив, с исходником: молчаливая порча программы
        # не должна повториться никогда
        with zipfile.ZipFile(os.path.join(OUT, name)) as z:
            packed = z.read(f"CehoProxy/{binname}")
        original = open(os.path.join(BIN, binary), "rb").read()
        if packed != original:
            raise SystemExit(f"{name}: программа в архиве отличается от собранной")

        print(f"{name}  {os.path.getsize(os.path.join(OUT, name)) // 1048576} МБ  ·  программа совпадает")


if __name__ == "__main__":
    main()
