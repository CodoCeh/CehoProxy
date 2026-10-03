# CehoProxy product artwork

The user selected **A, flat nested tunnel + application window**. `assets/cehoproxy.svg` is the reviewable vector master reconstructed from the **large** selected mark. The reference board's tiny tile accidentally omitted the inner contour, so it was not used as the shape authority. The selected reference image SHA-256 is `27cacacbb986ab3292174002464248653c182db0e975efe40c99d60cd54ea241`.

The product mark is deliberately independent of `src/ProxyCage.Core/Brand.cs`, which retains the original CodoCeh company artwork and links.

## Sources and derivatives

- `assets/cehoproxy.svg`: transparent graphite tunnel and emerald window. Contains distinct `outer-tunnel`, `inner-tunnel`, and `application-window` paths.
- `assets/logo/cehoproxy-optical.svg`: the same mark at small sizes. The gap around the inner contour is slightly wider, the window border is thicker, and three subpixel window controls are omitted. There is no new symbol or geometry concept.
- `assets/cehoproxy-dark.svg`: identical full geometry with pale tunnel contours for dark panels.
- `assets/cehoproxy-app.svg`: the full mark on the selected graphite rounded tile; unlike the reference's tiny preview, both tunnel contours remain present.
- `assets/logo/cehoproxy-{size}.png`: native-size app exports at 16, 20, 24, 32, 48, 64, 128, 256, and 512 px. The first four use the optical master.
- `assets/cehoproxy.png`: existing 256 px packaging contract.
- `assets/cehoproxy.ico`: eight individually rendered native-size PNG frames, 16–256 px, not downsamples of one large bitmap.
- `assets/cehoproxy.icns`: PNG chunks at 16–1024 px, including a 512 pt Retina representation.
- Existing `wizard*.bmp` files: 24-bit BMPs with the same dimensions, panel background, and layout, using the new product tile.
- `ProductBrand.cs`: generated inline, reviewable SVG and data URIs for the panel and favicon. No network request or extra runtime Core resource is required.

## Tray treatment

Windows and Linux use `assets/tray/cehoproxy-tray-{size}.png`, a **compact graphite tile with pale nested contours** and transparent rounded corners. This keeps the product silhouette readable in both light and dark OS trays without inferring an unreliable desktop theme. Transparent theme-specific variants are also supplied as `cehoproxy-light-*` and `cehoproxy-dark-*`.

macOS uses black, alpha-only `cehoproxy-template-18.png` and `cehoproxy-template-36.png` as the 18 pt 1×/2× source marks. AppKit supplies the menu tint through `isTemplate`. The six checked-in `cehoproxy-status-*.png` and `@2x` images are exported from the actual shared `TrayPixmap.RenderTemplate` implementation. They are 26×18 and 52×36 px: full 18 pt product mark plus a separate 7 pt state glyph. The states remain distinguishable by shape, not just colour.

The Windows/Linux previews likewise use exact `TrayPixmap.Render` output. They do not simulate or certify platform-native window manager scaling. Native Windows shell, Linux desktop integrations, and macOS menu-bar execution still require platform testing.

## Regenerate and verify

Use Python 3, **Pillow 12.3.0**, **Inkscape 1.4**, .NET 8, and DejaVu Sans (preview labels only). Install dependencies only from their official sources or reputable package registries.

```sh
python3 -m pip install -r tools/branding/requirements.txt
python3 tools/branding/generate.py
python3 tools/branding/export_tray.py --dotnet dotnet
python3 tools/branding/test_assets.py
```

`generate.py` writes the vector/raster derivatives, installer art, `ProductBrand.cs`, a review board, and a SHA-256 manifest. Rendering is deterministic under the versions above, with no timestamps, external images, or hidden transparent RGB.

`export_tray.py` builds the repository-local `TrayAssetExport` helper against the actual Core code, renders all 42 Windows/Linux state/size combinations and all 12 macOS templates in temporary storage, then writes the macOS resources and exact-pixel preview. No installed application, system settings, account, or network state is touched.

For a drift check that does not modify asset files:

```sh
python3 tools/branding/generate.py --check
python3 tools/branding/export_tray.py --dotnet dotnet --check
python3 tools/branding/test_assets.py
```

The previews are `assets/logo/cehoproxy-preview.png` and `assets/logo/cehoproxy-tray-preview.png`. They include actual-size rasters and clearly labelled nearest-neighbour enlargements on both light and dark surfaces.
