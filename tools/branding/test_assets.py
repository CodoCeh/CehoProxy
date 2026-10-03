#!/usr/bin/env python3
"""Fast structural/format checks. Use generate.py --check for full raster regeneration."""
import hashlib
import io
import json
import struct
import unittest
import xml.etree.ElementTree as ET
from PIL import Image
from generate import ROOT, SIZES, TRAY_SIZES, SVG


class ProductAssetsTests(unittest.TestCase):
    def test_generated_hashes_match_manifests(self):
        for filename in ("manifest.json", "tray-manifest.json"):
            manifest = json.loads((ROOT / "assets/logo" / filename).read_text())
            for relative, expected in manifest.get("masters", {}).items() | manifest["outputs"].items():
                with self.subTest(path=relative):
                    self.assertEqual(expected, hashlib.sha256((ROOT / relative).read_bytes()).hexdigest())

    def test_selected_mark_keeps_both_contours_and_application_window(self):
        for relative in ("assets/cehoproxy.svg", "assets/logo/cehoproxy-optical.svg", "assets/cehoproxy-dark.svg", "assets/cehoproxy-app.svg"):
            root = ET.parse(ROOT / relative).getroot()
            ids = {element.attrib.get("id") for element in root.iter()}
            self.assertTrue({"outer-tunnel", "inner-tunnel", "application-window", "application-frame"}.issubset(ids))
            self.assertEqual("0 0 512 512", root.attrib["viewBox"])
            self.assertFalse(list(root.iter(f"{{{SVG}}}image")))

    def test_light_dark_header_geometry_matches(self):
        def paths(relative):
            return [path.attrib["d"] for path in ET.parse(ROOT / relative).iter(f"{{{SVG}}}path")]
        self.assertEqual(paths("assets/cehoproxy.svg"), paths("assets/cehoproxy-dark.svg"))

    def test_all_rasters_are_rgba_at_requested_native_sizes(self):
        for size in SIZES:
            for relative in (f"assets/logo/cehoproxy-{size}.png", f"assets/logo/cehoproxy-mark-light-{size}.png", f"assets/logo/cehoproxy-mark-dark-{size}.png"):
                with Image.open(ROOT / relative) as image:
                    self.assertEqual("RGBA", image.mode)
                    self.assertEqual((size, size), image.size)
                    self.assertEqual(0, image.getpixel((0, 0))[3])
        for size in TRAY_SIZES:
            for mode in ("tray", "template", "light", "dark"):
                with Image.open(ROOT / f"assets/tray/cehoproxy-{mode}-{size}.png") as image:
                    self.assertEqual((size, size), image.size)
                    self.assertEqual("RGBA", image.mode)

    def test_mac_templates_have_no_colour_and_six_distinct_states(self):
        for size in TRAY_SIZES:
            with Image.open(ROOT / f"assets/tray/cehoproxy-template-{size}.png") as image:
                self.assertTrue(all(pixel[:3] == (0, 0, 0) for pixel in image.get_flattened_data()))
        for suffix, size in [("", (26, 18)), ("@2x", (52, 36))]:
            distinct = set()
            for state in ("protected", "starting", "off", "trouble", "stopped", "locked"):
                with Image.open(ROOT / f"assets/tray/cehoproxy-status-{state}{suffix}.png") as image:
                    self.assertEqual(size, image.size)
                    self.assertTrue(all(pixel[:3] == (0, 0, 0) for pixel in image.get_flattened_data()))
                    distinct.add(image.tobytes())
            self.assertEqual(6, len(distinct))

    def test_ico_contains_optical_images_without_downsampling(self):
        data = (ROOT / "assets/cehoproxy.ico").read_bytes()
        self.assertEqual((0, 1, 8), struct.unpack_from("<HHH", data))
        for index, size in enumerate(SIZES[:-1]):
            entry = struct.unpack_from("<BBBBHHII", data, 6 + index * 16)
            body = data[entry[-1]:entry[-1] + entry[-2]]
            self.assertEqual((ROOT / f"assets/logo/cehoproxy-{size}.png").read_bytes(), body)

    def test_icns_is_valid_and_contains_png_frames(self):
        data = (ROOT / "assets/cehoproxy.icns").read_bytes()
        self.assertEqual(b"icns", data[:4])
        self.assertEqual(len(data), struct.unpack_from(">I", data, 4)[0])
        offset, sizes = 8, []
        while offset < len(data):
            length = struct.unpack_from(">I", data, offset + 4)[0]
            with Image.open(io.BytesIO(data[offset + 8:offset + length])) as image:
                sizes.append(image.width)
                self.assertEqual(image.width, image.height)
            offset += length
        self.assertEqual([16, 32, 64, 128, 256, 512, 1024], sizes)
        self.assertEqual(len(data), offset)

    def test_installer_bitmaps_keep_size_and_rgb_format(self):
        for relative, dimensions in [("wizard-164.bmp", (164, 314)), ("wizard-328.bmp", (328, 628)), ("wizard-small-55.bmp", (55, 55)), ("wizard-small-110.bmp", (110, 110))]:
            with Image.open(ROOT / "assets" / relative) as image:
                self.assertEqual(dimensions, image.size)
                self.assertEqual("RGB", image.mode)
                self.assertEqual("BMP", image.format)

    def test_product_brand_is_separate_from_company_brand(self):
        source = (ROOT / "src/ProxyCage.Core/ProductBrand.cs").read_text()
        for name in ("LogoDataUri", "DarkLogoDataUri", "IconDataUri"):
            self.assertIn("public static readonly string " + name, source)
        company = (ROOT / "src/ProxyCage.Core/Brand.cs").read_text()
        self.assertIn('public const string Site = "https://codoceh.ru"', company)
        self.assertIn('data:image/png;base64,', company)


if __name__ == "__main__": unittest.main()
