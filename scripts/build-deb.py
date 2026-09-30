#!/usr/bin/env python3
import gzip, io, os, sys, tarfile, time

# build-deb.py <версия> <x64|arm64> <файл cehoproxy-linux-…> <куда>
VERSION, KIND, BINARY, OUT = sys.argv[1:5]
ARCH = {"x64": "amd64", "arm64": "arm64"}[KIND]
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
LIN = os.path.join(HERE, "installers", "linux")
NOW = 1767268800


def add(tar, name, data, mode):
    info = tarfile.TarInfo(name)
    info.size = len(data)
    info.mode = mode
    info.uid = info.gid = 0
    info.uname = info.gname = "root"
    info.mtime = NOW
    tar.addfile(info, io.BytesIO(data))


def folder(tar, name):
    info = tarfile.TarInfo(name)
    info.type = tarfile.DIRTYPE
    info.mode = 0o755
    info.uid = info.gid = 0
    info.uname = info.gname = "root"
    info.mtime = NOW
    tar.addfile(info)


def read(path):
    return open(path, "rb").read()


def targz(fill):
    raw = io.BytesIO()
    with gzip.GzipFile(fileobj=raw, mode="wb", mtime=NOW, compresslevel=9) as gz:
        with tarfile.open(fileobj=gz, mode="w", format=tarfile.GNU_FORMAT) as tar:
            fill(tar)
    return raw.getvalue()


def data(tar):
    for d in ("./usr", "./usr/lib", "./usr/lib/cehoproxy", "./usr/share", "./usr/share/applications",
              "./usr/share/icons", "./usr/share/icons/hicolor", "./usr/share/icons/hicolor/256x256",
              "./usr/share/icons/hicolor/256x256/apps", "./usr/share/doc", "./usr/share/doc/cehoproxy"):
        folder(tar, d)
    add(tar, "./usr/lib/cehoproxy/cehoproxy", read(BINARY), 0o755)
    add(tar, "./usr/lib/cehoproxy/install.sh", read(os.path.join(HERE, "install.sh")), 0o755)
    add(tar, "./usr/lib/cehoproxy/setup-gui.sh", read(os.path.join(LIN, "setup-gui.sh")), 0o755)
    add(tar, "./usr/lib/cehoproxy/open-panel.sh", read(os.path.join(LIN, "open-panel.sh")), 0o755)
    add(tar, "./usr/share/applications/cehoproxy.desktop", read(os.path.join(LIN, "cehoproxy.desktop")), 0o644)
    add(tar, "./usr/share/applications/cehoproxy-setup.desktop", read(os.path.join(LIN, "cehoproxy-setup.desktop")), 0o644)
    add(tar, "./usr/share/icons/hicolor/256x256/apps/cehoproxy.png", read(os.path.join(REPO, "assets", "cehoproxy.png")), 0o644)
    add(tar, "./usr/share/doc/cehoproxy/copyright", read(os.path.join(REPO, "LICENSE")), 0o644)
    add(tar, "./usr/share/doc/cehoproxy/THIRD-PARTY.md", read(os.path.join(REPO, "THIRD-PARTY.md")), 0o644)


size_kb = (os.path.getsize(BINARY) + 600_000) // 1024
control_text = f"""Package: cehoproxy
Version: {VERSION}
Section: net
Priority: optional
Architecture: {ARCH}
Installed-Size: {size_kb}
Maintainer: КодоЦех <info@codoceh.ru>
Homepage: https://github.com/CodoCeh/CehoProxy
Recommends: zenity | kdialog, policykit-1 | polkitd
Description: Only chosen programs go through your VPN
 CehoProxy sends the programs you choose through the tunnel of your VPN
 subscription. Everything else, browser, bank, government sites, system
 updates, goes directly. Control from a terminal (chp) and a local web panel.
""".encode()


def control(tar):
    add(tar, "./control", control_text, 0o644)
    add(tar, "./postinst", read(os.path.join(LIN, "postinst")), 0o755)
    add(tar, "./prerm", read(os.path.join(LIN, "prerm")), 0o755)


def ar(members):
    out = b"!<arch>\n"
    for name, blob in members:
        head = f"{name}/".ljust(16) + str(NOW).ljust(12) + "0".ljust(6) + "0".ljust(6) + "100644".ljust(8) + str(len(blob)).ljust(10) + "`\n"
        out += head.encode() + blob + (b"\n" if len(blob) % 2 else b"")
    return out


os.makedirs(OUT, exist_ok=True)
path = os.path.join(OUT, f"CehoProxy-{VERSION}-linux-{KIND}.deb")
open(path, "wb").write(ar([
    ("debian-binary", b"2.0\n"),
    ("control.tar.gz", targz(control)),
    ("data.tar.gz", targz(data)),
]))
print(path)
