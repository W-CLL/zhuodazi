from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageSequence


ROOT = Path(__file__).resolve().parents[1]
SOURCE_DIR = ROOT / "source" / "images" / "screenshots"
PHONE_BOX = (748, 16, 1132, 760)


def crop_gif(source_name: str, output_name: str) -> None:
    source = SOURCE_DIR / source_name
    output = SOURCE_DIR / output_name
    with Image.open(source) as gif:
        duration = gif.info.get("duration", 140)
        loop = gif.info.get("loop", 0)
        frames = []
        for frame in ImageSequence.Iterator(gif):
            cropped = frame.convert("RGBA").crop(PHONE_BOX)
            frames.append(cropped.convert("P", palette=Image.Palette.ADAPTIVE, colors=160))
    frames[0].save(
        output,
        save_all=True,
        append_images=frames[1:],
        duration=duration,
        loop=loop,
        optimize=True,
        disposal=2,
    )
    print(f"{output.name} {frames[0].size[0]}x{frames[0].size[1]} {len(frames)}f")


def main() -> None:
    crop_gif("android-visit.gif", "android-visit-phone.gif")
    crop_gif("android-wechat.gif", "android-wechat-phone.gif")
    crop_gif("android-send.gif", "android-send-phone.gif")


if __name__ == "__main__":
    main()
