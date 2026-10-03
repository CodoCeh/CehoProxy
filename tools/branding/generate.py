#!/usr/bin/env python3
"""Deterministically export the selected A SVG masters. Requires Inkscape 1.4 + Pillow 12.3."""
from __future__ import annotations
import argparse
import hashlib
import io
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import tempfile
import xml.etree.ElementTree as ET
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
SVG = "http://www.w3.org/2000/svg"
ET.register_namespace("", SVG)
SIZES = (16, 20, 24, 32, 48, 64, 128, 256, 512)
TRAY_SIZES = (16, 18, 20, 22, 24, 32, 36, 48, 64)
INK = "#192630"
PALE = "#f4f7f8"
GREEN = "#00a96c"
SOURCE_REFERENCE_SHA256 = "27cacacbb986ab3292174002464248653c182db0e975efe40c99d60cd54ea241"


def variant(small=False, dark=False, tile=False, template=False, universal=False):
    if universal:
        tile = True
    source = ROOT / ("assets/logo/cehoproxy-optical.svg" if small else "assets/cehoproxy.svg")
    root = ET.parse(source).getroot()
    for path in root.iter(f"{{{SVG}}}path"):
        ident = path.attrib.get("id", "")
        path.set("fill", "#000000" if template else (GREEN if ident == "application-frame" else (PALE if dark or tile else INK)))
    if tile:
        root.insert(0, ET.Element(f"{{{SVG}}}rect", {"width": "512", "height": "512", "rx": "112", "fill": INK}))
        # Preserve the reference app tile's breathing room, retaining both tunnel contours.
        root.find(f"{{{SVG}}}g").set("transform", "translate(18 18) scale(.93)" if universal else "translate(46 46) scale(.82)")
    return ET.tostring(root, encoding="unicode") + "\n"


def png_bytes(image):
    stream = io.BytesIO()
    image.save(stream, format="PNG", optimize=False, compress_level=9)
    return stream.getvalue()


def ico_bytes(frames):
    offset = 6 + 16 * len(frames)
    records = []
    bodies = []
    for size, image in frames:
        body = png_bytes(image)
        records.append(struct.pack("<BBBBHHII", size if size < 256 else 0, size if size < 256 else 0, 0, 0, 1, 32, len(body), offset))
        bodies.append(body)
        offset += len(body)
    return struct.pack("<HHH", 0, 1, len(frames)) + b"".join(records + bodies)


def icns_bytes(frames):
    kinds = {16: b"icp4", 32: b"icp5", 64: b"icp6", 128: b"ic07", 256: b"ic08", 512: b"ic09", 1024: b"ic10"}
    chunks = []
    for size, image in frames:
        body = png_bytes(image)
        chunks.append(kinds[size] + struct.pack(">I", len(body) + 8) + body)
    body = b"".join(chunks)
    return b"icns" + struct.pack(">I", len(body) + 8) + body


def font(size):
    return ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", size)


def preview(images):
    page = Image.new("RGB", (1280, 1010), "#e8edf0")
    draw = ImageDraw.Draw(page)
    draw.text((32, 24), "CehoProxy / вариант A", fill=INK, font=font(30))
    draw.text((32, 68), "Внутренний контур туннеля + окно приложения.", fill="#58636e", font=font(16))
    for x, bg, mode in [(24, "#ffffff", "light"), (652, "#101a23", "dark")]:
        draw.rounded_rectangle((x, 112, x + 604, 404), radius=16, fill=bg)
        fg = INK if mode == "light" else PALE
        page.paste(images[f"mark-{mode}-128"], (x + 38, 146), images[f"mark-{mode}-128"])
        draw.text((x + 188, 182), "CehoProxy", fill=fg, font=font(36))
        draw.text((x + 38, 328), "Логотип в шапке", fill=fg, font=font(16))
        page.paste(images["app-128"], (x + 444, 146), images["app-128"])
        draw.text((x + 444, 294), "Значок", fill=fg, font=font(15))
    draw.text((32, 436), "Трей: фактический размер и увеличение 3× без сглаживания", fill=INK, font=font(20))
    for row, (bg, label) in enumerate([("#ffffff", "Светлая тема"), ("#19232d", "Тёмная тема")]):
        y = 482 + row * 186
        draw.rounded_rectangle((24, y, 1256, y + 168), radius=12, fill=bg)
        fg = INK if row == 0 else PALE
        draw.text((44, y + 16), label, fill=fg, font=font(16))
        for index, size in enumerate((16, 20, 22, 24, 32)):
            x = 214 + index * 200
            im = images[f"tray-{size}"]
            page.paste(im, (x, y + 16), im)
            enlarged = im.resize((size * 3, size * 3), Image.Resampling.NEAREST)
            page.paste(enlarged, (x + 44, y + 44), enlarged)
            draw.text((x, y + 147), f"{size}px / 3x", fill=fg, font=font(14))
    draw.text((32, 880), "macOS: системная монохромная маска, фактический размер и 2×", fill=INK, font=font(18))
    for index, size in enumerate((16, 18, 20, 24, 32, 36)):
        x = 48 + index * 186
        im = images[f"template-{size}"]
        page.paste(im, (x, 926), im)
        enlarged = im.resize((size * 2, size * 2), Image.Resampling.NEAREST)
        page.paste(enlarged, (x + 50, 914), enlarged)
        draw.text((x, 986), f"{size}px / 2x", fill=INK, font=font(12))
    return png_bytes(page)


def csharp(light, dark, tile):
    def literal(name, value):
        # SVG text stays reviewable instead of becoming an opaque raster/base64 blob.
        return f'    public const string {name} = """\n' + "\n".join("        " + line for line in value.rstrip().splitlines()) + '\n        """;\n'
    return ("// Generated by tools/branding/generate.py. Edit assets/cehoproxy.svg, then regenerate.\n"
            "namespace ProxyCage.Core;\n\n"
            "/// <summary>CehoProxy product identity. The separate CodoCeh company identity lives in Brand.</summary>\n"
            "public static class ProductBrand\n{\n"
            + literal("LogoSvg", light) + "\n" + literal("DarkLogoSvg", dark) + "\n" + literal("IconSvg", tile)
            + '\n    public static readonly string LogoDataUri = SvgDataUri(LogoSvg);\n'
            + '    public static readonly string DarkLogoDataUri = SvgDataUri(DarkLogoSvg);\n'
            + '    public static readonly string IconDataUri = SvgDataUri(IconSvg);\n\n'
            + '    private static string SvgDataUri(string svg) =>\n'
            + '        "data:image/svg+xml;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(svg));\n}\n')


def generate():
    if not shutil.which("inkscape"):
        raise SystemExit("Inkscape 1.4 is required (official Inkscape package).")
    outputs = {}
    images = {}
    with tempfile.TemporaryDirectory(prefix="cehoproxy-brand-") as temporary:
        tmp = Path(temporary)
        env = dict(os.environ, INKSCAPE_PROFILE_DIR=str(tmp / "profile"))
        cache = {}
        def render(svg, size):
            key = (svg, size)
            if key not in cache:
                source = tmp / "input.svg"
                target = tmp / "output.png"
                source.write_text(svg)
                subprocess.run(["inkscape", str(source), "--export-filename=" + str(target), "--export-width=" + str(size), "--export-height=" + str(size)], check=True, env=env, capture_output=True)
                image = Image.open(target).convert("RGBA")
                # Canonical transparent RGB prevents hidden RGB from affecting template interpretation.
                pixels = bytearray(image.tobytes())
                for i in range(0, len(pixels), 4):
                    if not pixels[i + 3]: pixels[i:i + 3] = b"\0\0\0"
                cache[key] = Image.frombytes("RGBA", image.size, bytes(pixels))
            return cache[key].copy()
        light, dark, tile = variant(), variant(dark=True), variant(tile=True)
        outputs["assets/cehoproxy-dark.svg"] = dark.encode()
        outputs["assets/cehoproxy-app.svg"] = tile.encode()
        outputs["assets/tray/cehoproxy-template.svg"] = variant(small=True, template=True).encode()
        outputs["assets/tray/cehoproxy-tray.svg"] = variant(small=True, universal=True).encode()
        for size in SIZES:
            image = render(variant(small=size <= 32, tile=True), size)
            images[f"app-{size}"] = image
            outputs[f"assets/logo/cehoproxy-{size}.png"] = png_bytes(image)
            for mode in ("light", "dark"):
                image = render(variant(small=size <= 24, dark=mode == "dark"), size)
                images[f"mark-{mode}-{size}"] = image
                outputs[f"assets/logo/cehoproxy-mark-{mode}-{size}.png"] = png_bytes(image)
        for size in TRAY_SIZES:
            for mode, options in [("light", {}), ("dark", {"dark": True}), ("template", {"template": True}), ("tray", {"universal": True})]:
                # Template uses the clean optical silhouette at all sizes, including @2x.
                image = render(variant(small=True, **options), size)
                images[f"{mode}-{size}"] = image
                outputs[f"assets/tray/cehoproxy-{mode}-{size}.png"] = png_bytes(image)
        outputs["assets/cehoproxy.png"] = outputs["assets/logo/cehoproxy-256.png"]
        outputs["assets/cehoproxy.ico"] = ico_bytes([(size, images[f"app-{size}"]) for size in SIZES if size <= 256])
        outputs["assets/cehoproxy.icns"] = icns_bytes([(size, images.get(f"app-{size}") or render(tile, size)) for size in (16, 32, 64, 128, 256, 512, 1024)])
        outputs["assets/tray/cehoproxy-template.png"] = outputs["assets/tray/cehoproxy-template-18.png"]
        outputs["assets/tray/cehoproxy-template@2x.png"] = outputs["assets/tray/cehoproxy-template-36.png"]
        for width, scale in [(164, 1), (328, 2)]:
            image = Image.new("RGB", (width, 314 * scale), "#101512")
            mark = render(tile, 128 * scale)
            image.paste(mark, (18 * scale, 40 * scale), mark)
            ImageDraw.Draw(image).rectangle((0, 308 * scale, width, 314 * scale), fill="#4bb98a")
            buffer = io.BytesIO()
            image.save(buffer, "BMP", dpi=(96, 96))
            outputs[f"assets/wizard-{width}.bmp"] = buffer.getvalue()
        for size in (55, 110):
            image = Image.new("RGB", (size, size), "white")
            mark = render(tile, size)
            image.paste(mark, (0, 0), mark)
            buffer = io.BytesIO()
            image.save(buffer, "BMP", dpi=(96, 96))
            outputs[f"assets/wizard-small-{size}.bmp"] = buffer.getvalue()
        outputs["src/ProxyCage.Core/ProductBrand.cs"] = csharp(light, dark, tile).encode()
        outputs["assets/logo/cehoproxy-preview.png"] = preview(images)
    manifest = {
        "reference_sha256": SOURCE_REFERENCE_SHA256,
        "masters": {str(path.relative_to(ROOT)): hashlib.sha256(path.read_bytes()).hexdigest() for path in (ROOT / "assets/cehoproxy.svg", ROOT / "assets/logo/cehoproxy-optical.svg")},
        "renderer": "Inkscape 1.4; Pillow 12.3.0; straight-alpha RGBA PNG; no timestamps",
        "outputs": {path: hashlib.sha256(data).hexdigest() for path, data in sorted(outputs.items())},
    }
    outputs["assets/logo/manifest.json"] = (json.dumps(manifest, indent=2) + "\n").encode()
    return outputs


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="Rebuild in temp storage and fail if committed outputs drift.")
    args = parser.parse_args()
    outputs = generate()
    changed = []
    for relative, data in outputs.items():
        path = ROOT / relative
        if not path.exists() or path.read_bytes() != data:
            changed.append(relative)
            if not args.check:
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(data)
    if args.check and changed:
        raise SystemExit("Generated asset drift:\n" + "\n".join(changed))
    print(f"{'Verified' if args.check else 'Generated'} {len(outputs)} product assets. {len(changed)} changed.")


if __name__ == "__main__":
    main()
