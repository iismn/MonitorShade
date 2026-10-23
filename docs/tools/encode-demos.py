"""Encode captured app frames for the README. Requires Pillow."""
from pathlib import Path
import argparse
from PIL import Image

parser = argparse.ArgumentParser()
parser.add_argument("frames", type=Path)
parser.add_argument("destination", type=Path)
args = parser.parse_args()
args.destination.mkdir(parents=True, exist_ok=True)

for name, expected in (("brightness", 104), ("appearance", 160)):
    paths = sorted((args.frames / name).glob("*.png"))
    if len(paths) != expected:
        raise RuntimeError(f"{name}: expected {expected} frames, found {len(paths)}")
    stride = 2 if name == "appearance" else 1
    width = 800 if name == "appearance" else 960
    frames = []
    for path in paths[::stride]:
        with Image.open(path) as source:
            rgb = source.convert("RGB")
            size = (width, round(rgb.height * width / rgb.width))
            frames.append(rgb.resize(size, Image.Resampling.LANCZOS))
    # Brightness has a fixed theme: a full-resolution palette keeps text crisp
    # and static areas stable. Appearance needs a separate palette per frame
    # so day and night do not compete for the same 256 colors.
    if name == "brightness":
        palette = frames[0].quantize(colors=256, method=Image.Quantize.MEDIANCUT)
        encoded = [frame.quantize(palette=palette, dither=Image.Dither.FLOYDSTEINBERG) for frame in frames]
    else:
        encoded = [frame.quantize(colors=256, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE) for frame in frames]
    path = args.destination / f"{name}-demo.gif"
    encoded[0].save(
        path, save_all=True, append_images=encoded[1:], loop=0,
        duration=[250 if stride == 2 else (120 if i % 2 == 0 else 130) for i in range(len(encoded))],
        disposal=1, optimize=True,
    )
    with Image.open(path) as check:
        duration = 0
        for frame in range(check.n_frames):
            check.seek(frame)
            check.load()
            duration += check.info["duration"]
        assert duration == expected * 125, (name, duration)
        assert check.n_frames > 20, (name, check.n_frames)
        print(f"{path.name}: {check.n_frames} frames, {duration} ms, {path.stat().st_size / 1048576:.2f} MiB")

for name in ("morning", "day", "evening", "night"):
    with Image.open(args.frames / f"appearance-{name}.png") as source:
        image = source.convert("RGB")
        image = image.resize((1000, round(image.height * 1000 / image.width)), Image.Resampling.LANCZOS)
        image.save(args.destination / f"appearance-{name}.png", optimize=True)
