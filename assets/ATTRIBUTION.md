# Asset attributions

## `src/Beam.Tray/Assets/high-beam.png` and `assets/beam-256.png`

"High beam" icon by **I Create Stuff**, from [The Noun Project](https://thenounproject.com/),
licensed under [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/).

The PNG is stored unmodified as a black silhouette with an alpha channel. At runtime the
tray app tints it (Pantone 638C fill, deeper halo) via a `ColorMatrix`; the icon generator
(`installer/generate-icon.ps1`) does the same to produce `src/Beam.Tray/Assets/app.ico`.

CC BY 3.0 requires attribution: this file plus the credit in `README.md`.
