import argparse
from pathlib import Path

from PIL import Image


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("frames", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--duration", type=int, default=250)
    parser.add_argument("--width", type=int, default=960)
    parser.add_argument("--skip", type=int, default=0)
    args = parser.parse_args()

    frame_paths = sorted(args.frames.glob("frame-*.png"))[args.skip:]
    if not frame_paths:
        raise SystemExit("No capture frames found.")

    images = []
    for frame_path in frame_paths:
        with Image.open(frame_path) as source:
            frame = source.convert("RGB")
            target_height = round(frame.height * args.width / frame.width)
            images.append(frame.resize((args.width, target_height), Image.Resampling.LANCZOS))

    args.output.parent.mkdir(parents=True, exist_ok=True)
    images[0].save(
        args.output,
        save_all=True,
        append_images=images[1:],
        duration=args.duration,
        loop=0,
        optimize=True,
    )


if __name__ == "__main__":
    main()
