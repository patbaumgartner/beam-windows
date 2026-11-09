# Asset attributions

## `src/Beam.Tray/Assets/high-beam.svg` and the rasters generated from it

"High beam" icon by **I Create Stuff**, from [The Noun Project](https://thenounproject.com/),
icon page: <https://thenounproject.com/icon/high-beam-497871/>, licensed under
[CC BY 3.0](https://creativecommons.org/licenses/by/3.0/).

`high-beam.svg` is an adaptation of the upstream art (the upstream SVG download is
hotlink-protected, so the glyph geometry was measured from the 512 px PNG and redrawn as
vector shapes): recoloured from the original black to the Beam brand cyan — Pantone 638C
(`#00AED8`) fill with a deep `#0080B4` halo — and thickened (bar height and ring
thickness increased, glyph enlarged to ~90 % of the canvas) so the icon stays readable at
16 px.

The SVG is the single source of truth for every raster asset; nothing is hand-edited in
the rasters:

- `installer/generate-icon.ps1` (via `installer/render-icon.fsx`, Svg.NET) renders it into
  the multi-size `src/Beam.Tray/Assets/app.ico` (exe icon + installer icon) and into
  `assets/beam-256.png` (plain cyan, halo dropped).
- The tray app embeds the SVG and renders it at runtime for the NotifyIcon
  (`TrayService.RenderBrandMark`).

CC BY 3.0 requires attribution: this file plus the credit in `README.md`.
