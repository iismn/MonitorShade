# Embedded fonts

These unmodified fonts and their full SIL Open Font License texts are embedded in MonitorShade.exe. They can also be read inside the app under Help → Appearance and fonts. No font installation or network request is required at runtime.

- **Pretendard Regular / SemiBold** — [orioncactus/pretendard v1.3.9](https://github.com/orioncactus/pretendard/tree/v1.3.9/packages/pretendard/dist/public/static/alternative). License: `Pretendard-LICENSE.txt`.
- **Manrope Light / Regular** — [aaronbell/manrope](https://github.com/aaronbell/manrope/tree/6f81ebecdf65e4463b798cc07b16a4f8d5216917/fonts/ttf), commit `6f81ebecdf65e4463b798cc07b16a4f8d5216917`. License: `Manrope-OFL.txt`.

All fonts are distributed under the SIL Open Font License 1.1. Font names and copyright statements remain unchanged.

The default Korean UI font is bundled Pretendard (restored from 1.15.1). Chromium uses the real Regular and SemiBold faces without synthesized weights. [Malgun Gothic](https://learn.microsoft.com/en-us/typography/font-list/malgun-gothic) remains an alternative in General settings and is loaded from the user's Windows installation. Malgun Gothic is **not** embedded or redistributed and is not covered by the bundled OFL licenses. If it is unavailable, the CSS font stack falls back to bundled Pretendard.
