# README recordings

The GIFs are recordings of MonitorShade 1.18.2's WebView2 settings UI.
The recorder loads the released EXE in its existing preview mode.
No replacement UI, generated animation frames, or image crossfades are used.

- Captures use the English interface.
- Brightness changes pass through the normal UI event and native controller.
- The monitors are preview devices. Recording does not send DDC/CI commands.
- The appearance recording advances the app's preview clock through 24 hours.
- Frames come from WebView2 at 8 fps. The appearance GIF uses every second frame to reduce its size. Native DWM blur is outside this capture.
- A solid base uses the app's background color under the translucent UI.
- Preferences and the released EXE are not modified.

## Record

Run from the repository root on Windows with .NET Framework 4.8 and WebView2.
The pinned WebView2 SDK must already exist in `.build/webview2` (created by the application build).

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\docs\tools\record-demos.ps1 -ApplicationPath .\MonitorShade.exe
python .\docs\tools\encode-demos.py .\.build\readme-recordings .\docs\images
```

The encoder requires Pillow. Intermediate frames and validation results stay in
`.build/readme-recordings`. The GIFs, four appearance stills, and window-layout screenshot are published.

Run `python docs/tools/build-readme-hero.py` after updating the screenshots to rebuild the SVG cover. It embeds the morning and evening screenshots and needs no remote assets, scripts, or custom README CSS.

Source EXE SHA-256:

```text
a3abe50cfe6c70faf5b00792feedb12d36bb6de7e34eb069d5ed2cd9e965f11a
```
