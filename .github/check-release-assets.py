#!/usr/bin/env python3
import os, re, sys, zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
VERSION = sys.argv[1]
DIR = sys.argv[2]

BARE = [
    "cehoproxy-win-x64.exe",
    "cehoproxy-osx-arm64",
    "cehoproxy-osx-x64",
    "cehoproxy-linux-x64",
    "cehoproxy-linux-arm64",
    "cehoproxy-tray-win-x64.exe",
]
ZIPS = [f"CehoProxy-{VERSION}-{s}.zip"
        for s in ("windows", "macos-apple", "macos-intel", "linux-x64", "linux-arm64")]
EXTRA = ["CehoProxy.cmd"]

TRAY_MAC = "CehoProxy/CehoProxy Tray.app/Contents/MacOS/CehoProxyTray"
INSIDE = {
    f"CehoProxy-{VERSION}-windows.zip": [("CehoProxy/cehoproxy-tray.exe", None)],
    f"CehoProxy-{VERSION}-macos-apple.zip": [("CehoProxy/install-tray-mac.sh", None),
                                             (TRAY_MAC, "arm64")],
    f"CehoProxy-{VERSION}-macos-intel.zip": [("CehoProxy/install-tray-mac.sh", None),
                                             (TRAY_MAC, "x86_64")],
}

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
    ("scripts/install.ps1", r'releases/latest/download/cehoproxy-tray-win-x64\.exe',
     "install.ps1 качает значок Windows под другим именем"),
    ("src/ProxyCage.Core/Installer.cs", r'TrayWindowsFileName = "cehoproxy-tray\.exe"',
     "Installer.cs ждёт другое имя значка Windows"),
    ("scripts/install.sh", r'install-tray-mac\.sh',
     "install.sh больше не ставит значок macOS"),
    ("scripts/install-tray-mac.sh", r'CehoProxy Tray\.app',
     "install-tray-mac.sh ищет другое имя приложения"),
]

PACKED = ["cehoproxy-tray.exe", "install-tray-mac.sh", "CehoProxy Tray.app"]

CPU = {0x0100000C: "arm64", 0x01000007: "x86_64"}

def arch_of(blob):
    if blob[:4] == b"\xcf\xfa\xed\xfe":
        return [CPU.get(int.from_bytes(blob[4:8], "little"))]
    if blob[:4] in (b"\xca\xfe\xba\xbe", b"\xca\xfe\xba\xbf"):
        count = int.from_bytes(blob[4:8], "big")
        step = 20 if blob[:4] == b"\xca\xfe\xba\xbe" else 32
        return [CPU.get(int.from_bytes(blob[8 + i * step: 12 + i * step], "big"))
                for i in range(min(count, 16))]
    return []

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
    text = open(os.path.join(ROOT, "scripts", "make-release.py"), encoding="utf-8").read()
    packed = set(re.findall(r'"(cehoproxy-[a-z0-9-]+(?:\.exe)?)"', text))
    missing = sorted((set(BARE) - {"cehoproxy-tray-win-x64.exe"}) - packed)
    problems = [f"scripts/make-release.py больше не упаковывает: {', '.join(missing)}"] if missing else []
    for name in PACKED:
        if name not in text:
            problems.append(f"scripts/make-release.py больше не упаковывает {name}")
    return problems

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

def inside():
    problems = []
    for name, wanted in INSIDE.items():
        path = os.path.join(DIR, name)
        if not os.path.exists(path):
            continue
        with zipfile.ZipFile(path) as z:
            names = set(z.namelist())
            for entry, arch in wanted:
                if entry not in names:
                    problems.append(f"{name}: в архиве нет {entry}")
                    continue
                info = z.getinfo(entry)
                if not (info.external_attr >> 16) & 0o111:
                    problems.append(f"{name}: {entry} в архиве без права на запуск")
                if arch is None:
                    continue
                found = arch_of(z.read(entry)[:4096])
                if arch not in found:
                    problems.append(
                        f"{name}: {entry} собран под {found or 'неизвестно что'}, а нужен {arch}")
    return problems

def main():
    problems = sources() + packer() + files() + inside()
    if problems:
        fail(problems)
    print(f"Ассеты версии {VERSION}:")
    for name in sorted(os.listdir(DIR)):
        print(f"  {name}  {os.path.getsize(os.path.join(DIR, name)) // 1024} КБ")
    for name, wanted in INSIDE.items():
        print(f"{name}:")
        with zipfile.ZipFile(os.path.join(DIR, name)) as z:
            for item in z.infolist():
                print(f"  {item.filename}  {item.file_size // 1024} КБ")

if __name__ == "__main__":
    main()
