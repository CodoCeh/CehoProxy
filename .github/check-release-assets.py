#!/usr/bin/env python3
import os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
VERSION = sys.argv[1]
DIR = sys.argv[2]

BARE = [
    "cehoproxy-win-x64.exe",
    "cehoproxy-osx-arm64",
    "cehoproxy-osx-x64",
    "cehoproxy-linux-x64",
    "cehoproxy-linux-arm64",
]
ZIPS = [f"CehoProxy-{VERSION}-{s}.zip"
        for s in ("windows", "macos-apple", "macos-intel", "linux-x64", "linux-arm64")]
EXTRA = ["CehoProxy.cmd"]

SOURCES = [
    ("src/ProxyCage.Core/Updater.cs", r'"cehoproxy-\{os\}-\{arch\}"',
     "chp update ищет ассет по другому шаблону"),
    ("scripts/install.sh", r'ASSET="cehoproxy-\$OS-\$ARCH"',
     "install.sh качает ассет по другому шаблону"),
    ("scripts/install.ps1", r'releases/latest/download/cehoproxy-win-x64\.exe',
     "install.ps1 качает другой файл для Windows"),
    ("scripts/install.ps1", r'releases/latest/download/libcronet\.dll',
     "install.ps1 больше не качает libcronet.dll"),
    ("src/ProxyCage.Core/Installer.cs", r'releases/latest/download/libcronet\.dll',
     "Installer.cs больше не качает libcronet.dll"),
]

def fail(lines):
    print("Проверка имён ассетов не пройдена:", file=sys.stderr)
    for line in lines:
        print(f"  {line}", file=sys.stderr)
    sys.exit(1)

def sources():
    problems = []
    for rel, pattern, why in SOURCES:
        path = os.path.join(ROOT, rel)
        if not os.path.exists(path):
            problems.append(f"нет файла {rel}")
            continue
        if not re.search(pattern, open(path, encoding="utf-8-sig").read()):
            problems.append(f"{rel}: {why} (искали /{pattern}/)")
    return problems

def packer():
    path = os.path.join(ROOT, "scripts", "make-release.py")
    text = open(path, encoding="utf-8").read()
    packed = set(re.findall(r'"(cehoproxy-[a-z0-9-]+(?:\.exe)?)"', text))
    missing = set(BARE) - packed
    return [f"scripts/make-release.py больше не упаковывает: {', '.join(sorted(missing))}"] if missing else []

def files():
    problems = []
    for name in BARE + ZIPS + EXTRA:
        path = os.path.join(DIR, name)
        if not os.path.exists(path):
            problems.append(f"не собран ассет {name}")
            continue
        size = os.path.getsize(path)
        limit = 10 * 1024 * 1024 if name in BARE + ZIPS else 512
        if size < limit:
            problems.append(f"{name}: размер {size} Б меньше ожидаемого {limit} Б")
    return problems

def main():
    problems = sources() + packer() + files()
    if problems:
        fail(problems)
    print(f"Ассеты версии {VERSION}:")
    for name in sorted(os.listdir(DIR)):
        print(f"  {name}  {os.path.getsize(os.path.join(DIR, name)) // 1024} КБ")

if __name__ == "__main__":
    main()
