"""Composite captured app frames into the layered README cover. Requires Pillow and sharp."""
import argparse
import importlib.util
import json
import subprocess
from pathlib import Path

from PIL import Image, ImageChops

parser = argparse.ArgumentParser()
parser.add_argument("recording", type=Path, help="Directory containing the 160 appearance PNGs")
parser.add_argument("--node", default="node")
parser.add_argument("--sharp", default="sharp", help="Module name or absolute sharp package path")
parser.add_argument("--output", type=Path, default=Path("docs/images/hero.gif"))
parser.add_argument("--work", type=Path, default=Path(".build/hero-animation"))
parser.add_argument("--reuse-rendered", action="store_true", help="Re-encode unchanged PNG composites already in the work directory")
args = parser.parse_args()
args.work.mkdir(parents=True, exist_ok=True)
spec = importlib.util.spec_from_file_location("hero", Path(__file__).with_name("build-readme-hero.py"))
hero = importlib.util.module_from_spec(spec)
spec.loader.exec_module(hero)
paths = sorted((args.recording / "appearance").glob("*.png"))
if len(paths) != 160:
    raise RuntimeError(f"Expected 160 real app frames; found {len(paths)}")

# Keep the original two-panel geometry. Offset the second recording so both
# palettes are visible. Replay at half speed for a quiet 40-second loop.
for frame, index in enumerate(range(0, len(paths), 2)):
    svg = hero.cover(paths[(index + 72) % 160], paths[index])
    (args.work / f"{frame:04}.svg").write_text(svg, encoding="utf-8")

renderer = args.work / "render.cjs"
renderer.write_text("""const fs = require('node:fs');
const path = require('node:path');
const sharp = require(process.argv[2]);
(async () => {
  const folder = process.argv[3];
  for (const name of fs.readdirSync(folder).filter(n => n.endsWith('.svg')).sort()) {
    await sharp(path.join(folder, name)).resize(1040).flatten({background:'#101714'})
      .png().toFile(path.join(folder, name.replace('.svg', '.png')));
  }
})().catch(e => { console.error(e); process.exitCode = 1; });
""", encoding="utf-8")
if not args.reuse_rendered:
    subprocess.run([args.node, str(renderer.resolve()), args.sharp, str(args.work.resolve())], check=True)
frames = []
for path in sorted(args.work.glob("[0-9][0-9][0-9][0-9].png")):
    with Image.open(path) as image:
        frames.append(image.convert("RGB"))
if len(frames) != 80:
    raise RuntimeError(f"Expected 80 composited frames; found {len(frames)}")

# Reserve 64 palette entries for the static cover, so its gradients do not
# shimmer. Give the changing panels 192 colors per frame; a single global
# palette makes the green card pick up orange speckles from the evening theme.
change = Image.new("L", frames[0].size)
for frame in frames[1:]:
    diff = ImageChops.difference(frames[0], frame)
    r, g, b = diff.split()
    diff = ImageChops.lighter(ImageChops.lighter(r, g), b)
    change = ImageChops.lighter(change, diff)
change = change.point(lambda value: 255 if value else 0)
static_sample = frames[0].copy()
static_sample.paste((23, 34, 30), mask=change)
static_palette = static_sample.quantize(colors=60, method=Image.Quantize.MEDIANCUT)
# Keep the small, bright typography exact instead of averaging it into gray.
fixed_colors = static_palette.getpalette()[:60 * 3] + [241, 244, 233, 228, 233, 223, 216, 226, 211, 192, 203, 187]
static_palette.putpalette(fixed_colors * 4)
static = frames[0].quantize(palette=static_palette, dither=Image.Dither.FLOYDSTEINBERG)
static = static.point(lambda index: index % 64)
encoded = []
for frame in frames:
    changing = frame.crop(change.getbbox()).quantize(colors=192, method=Image.Quantize.MEDIANCUT)
    palette = Image.new("P", (1, 1))
    colors = fixed_colors + changing.getpalette()[:192 * 3]
    palette.putpalette(colors)
    quantized = frame.quantize(palette=palette, dither=Image.Dither.FLOYDSTEINBERG)
    combined = static.copy()
    combined.putpalette(colors)
    combined.paste(quantized, mask=change)
    encoded.append(combined)
args.output.parent.mkdir(parents=True, exist_ok=True)
encoded[0].save(args.output, save_all=True, append_images=encoded[1:], loop=0, duration=500, disposal=1, optimize=True)
with Image.open(args.output) as gif:
    durations = []
    for index in range(gif.n_frames):
        gif.seek(index)
        gif.load()
        durations.append(gif.info["duration"])
        if index in (0, 20, 40, 60):
            gif.convert("RGB").save(args.work / f"check-{index:02}.png")
    assert sum(durations) == 40000
    assert gif.n_frames >= 60
    assert gif.info["loop"] == 0
    report = {"frames": gif.n_frames, "durationMs": sum(durations), "size": gif.size, "bytes": args.output.stat().st_size,
              "source": "MonitorShade 1.18.2 WebView2 appearance recording", "method": "SVG composition of real app frames; no UI crossfades"}
(args.work / "report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print(json.dumps(report))
