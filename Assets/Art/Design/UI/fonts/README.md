# Fonts

Typeface: **Archivo** (Google Fonts, open source — https://fonts.google.com/specimen/Archivo)

Weights used:
- **Archivo ExtraBold/800** — headings, HUD numerals (timer, ammo, K/D), button labels, eyebrows
- **Archivo Regular/400** — body copy, settings labels, lobby lists

No font files are bundled here (Google Fonts license allows direct download instead of redistribution).
To get the .ttf/.otf files for a Unity TMP Font Asset:

1. Download from https://fonts.google.com/specimen/Archivo (Get font → choose Regular + ExtraBold)
2. In Unity: Window → TextMeshPro → Font Asset Creator
3. Source Font File: Archivo-Regular.ttf and Archivo-ExtraBold.ttf → generate two TMP Font Assets
4. For HUD numerals (timer/ammo/K-D), enable tabular/monospaced figures if the font source has them, or use the ExtraBold asset with fixed-width number glyphs to avoid layout jitter.
