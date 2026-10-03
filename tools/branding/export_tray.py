#!/usr/bin/env python3
"""Export exact TrayPixmap runtime pixels and macOS bundled template state images."""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
from PIL import Image, ImageDraw
from generate import ROOT, font, png_bytes

STATES = ("protected", "starting", "off", "trouble", "stopped", "locked")
LABELS = ("Защита", "Запуск", "Выключено", "Проблема", "Остановлен", "Блокировка")


def read_pixels(path):
    data = json.loads(path.read_text())
    rgba = bytearray()
    for value in data["Pixels"]:
        rgba.extend(((value >> 16) & 255, (value >> 8) & 255, value & 255, (value >> 24) & 255))
    return Image.frombytes("RGBA", (data["Width"], data["Height"]), bytes(rgba))


def tint_template(image, rgb):
    tinted = Image.new("RGBA", image.size, rgb)
    tinted.putalpha(image.getchannel("A"))
    return tinted


def preview(images):
    page = Image.new("RGB", (1280, 1070), "#e8edf0")
    draw = ImageDraw.Draw(page)
    draw.text((28, 24), "CehoProxy / шесть состояний трея", fill="#192630", font=font(28))
    draw.text((28, 66), "Точные пиксели общего рендера. Проверка платформенных оболочек отдельно.", fill="#58636e", font=font(16))
    for row, (bg, fg, label) in enumerate([("#ffffff", "#192630", "Светлая тема"), ("#18232c", "#f4f7f8", "Тёмная тема")]):
        y = 108 + row * 262
        draw.rounded_rectangle((24, y, 1256, y + 246), radius=14, fill=bg)
        draw.text((42, y + 16), label + " · Windows / Linux", fill=fg, font=font(17))
        for col, state in enumerate(STATES):
            x = 48 + col * 200
            draw.text((x, y + 52), LABELS[col], fill=fg, font=font(15))
            for j, size in enumerate((16, 20, 24, 32)):
                im = images[f"tray-{state}-{size}"]
                page.paste(im, (x + j * 42, y + 84), im)
            draw.text((x, y + 122), "16 / 20 / 24 / 32 px", fill=fg, font=font(12))
            big = images[f"tray-{state}-16"].resize((64, 64), Image.Resampling.NEAREST)
            page.paste(big, (x + 40, y + 154), big)
            draw.text((x + 112, y + 198), "16 px ×4", fill=fg, font=font(11))
    for row, (bg, fg, rgb, label) in enumerate([("#ffffff", "#192630", (0, 0, 0), "Светлая тема"), ("#18232c", "#f4f7f8", (255, 255, 255), "Тёмная тема")]):
        y = 654 + row * 194
        draw.rounded_rectangle((24, y, 1256, y + 178), radius=14, fill=bg)
        draw.text((42, y + 16), "macOS · " + label + " · 26×18 pt, 1× / 2× Retina; ниже — увеличение 3×", fill=fg, font=font(17))
        for col, state in enumerate(STATES):
            x = 48 + col * 200
            draw.text((x, y + 50), LABELS[col], fill=fg, font=font(15))
            im = tint_template(images[f"cehoproxy-status-{state}"], rgb)
            page.paste(im, (x + 5, y + 82), im)
            retina = tint_template(images[f"cehoproxy-status-{state}@2x"], rgb)
            page.paste(retina, (x + 50, y + 74), retina)
            big = im.resize((78, 54), Image.Resampling.NEAREST)
            page.paste(big, (x + 36, y + 116), big)
    return png_bytes(page)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    with tempfile.TemporaryDirectory(prefix="cehoproxy-tray-") as temporary:
        subprocess.run([args.dotnet, "run", "--project", str(ROOT / "tools/branding/TrayAssetExport/TrayAssetExport.csproj"), "-c", "Release", "--", str(ROOT), temporary], check=True)
        images = {path.stem: read_pixels(path) for path in sorted(Path(temporary).glob("*.json"))}
    outputs = {f"assets/tray/{name}.png": png_bytes(image) for name, image in images.items() if name.startswith("cehoproxy-status-")}
    outputs["assets/logo/cehoproxy-tray-preview.png"] = preview(images)
    manifest = {"renderer": "ProxyCage.Core.TrayPixmap.Render / RenderTemplate", "outputs": {name: hashlib.sha256(data).hexdigest() for name, data in sorted(outputs.items())}}
    outputs["assets/logo/tray-manifest.json"] = (json.dumps(manifest, indent=2) + "\n").encode()
    changed = []
    for name, data in outputs.items():
        path = ROOT / name
        if not path.exists() or path.read_bytes() != data:
            changed.append(name)
            if not args.check: path.write_bytes(data)
    if args.check and changed: raise SystemExit("Tray asset drift:\n" + "\n".join(changed))
    print(f"{'Verified' if args.check else 'Exported'} 54 exact runtime pixel images; {len(outputs)} repository outputs; {len(changed)} changed.")


if __name__ == "__main__": main()
