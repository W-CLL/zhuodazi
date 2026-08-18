from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont, ImageSequence


ROOT = Path(__file__).resolve().parents[1]
IMAGE_DIR = ROOT / "source" / "images"
OUTPUT_DIR = IMAGE_DIR / "screenshots"
WIDTH = 1200
HEIGHT = 760


def load_font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont:
    font_name = "msyhbd.ttc" if bold else "msyh.ttc"
    return ImageFont.truetype(str(Path("C:/Windows/Fonts") / font_name), size)


FONT_18 = load_font(18)
FONT_20_BOLD = load_font(20, bold=True)
FONT_24 = load_font(24)
FONT_28_BOLD = load_font(28, bold=True)
FONT_36_BOLD = load_font(36, bold=True)


def load_gif_frames(path: Path, limit: int = 24) -> list[Image.Image]:
    with Image.open(path) as source:
        frames = [frame.convert("RGBA") for frame in ImageSequence.Iterator(source)]
    if len(frames) <= limit:
        return frames
    step = len(frames) / limit
    return [frames[int(index * step)] for index in range(limit)]


def resize_sprite(frame: Image.Image, size: int) -> Image.Image:
    return frame.resize((size, size), Image.Resampling.LANCZOS)


def draw_topbar(draw: ImageDraw.ImageDraw, title: str) -> None:
    draw.rectangle((0, 0, WIDTH, 62), fill="#ffffff")
    draw.line((0, 61, WIDTH, 61), fill="#d9e2de", width=1)
    for index, color in enumerate(("#ef7865", "#e6b949", "#50a887")):
        x = 30 + index * 25
        draw.ellipse((x, 23, x + 13, 36), fill=color)
    draw.text((126, 19), title, fill="#21302a", font=FONT_20_BOLD)
    draw.text((WIDTH - 160, 20), "DESKPET", fill="#718078", font=FONT_18)


def draw_speech_bubble(
    canvas: Image.Image,
    box: tuple[int, int, int, int],
    speaker: str,
    message: str,
    pointer: tuple[int, int],
    accent: str = "#16846f",
) -> None:
    draw = ImageDraw.Draw(canvas)
    x1, y1, x2, y2 = box
    draw.rounded_rectangle(box, radius=20, fill="#ffffff", outline="#cdd9d4", width=2)
    pointer_x, pointer_y = pointer
    if pointer_x < x1:
        polygon = [(x1 + 3, y2 - 52), (x1 - 22, y2 - 28), (x1 + 3, y2 - 20)]
    else:
        polygon = [(x2 - 3, y2 - 52), (x2 + 22, y2 - 28), (x2 - 3, y2 - 20)]
    draw.polygon(polygon, fill="#ffffff")
    draw.line((polygon[0], polygon[1]), fill="#cdd9d4", width=2)
    draw.text((x1 + 28, y1 + 22), speaker, fill=accent, font=FONT_20_BOLD)
    draw.multiline_text(
        (x1 + 28, y1 + 62),
        message,
        fill="#17231e",
        font=FONT_28_BOLD,
        spacing=10,
    )


def save_gif(frames: list[Image.Image], output: Path, duration: int = 150) -> None:
    output.parent.mkdir(parents=True, exist_ok=True)
    palette_frames = [frame.convert("P", palette=Image.Palette.ADAPTIVE, colors=128) for frame in frames]
    palette_frames[0].save(
        output,
        save_all=True,
        append_images=palette_frames[1:],
        duration=duration,
        loop=0,
        optimize=True,
        disposal=2,
    )


def build_visit_scene() -> None:
    home_frames = load_gif_frames(IMAGE_DIR / "collection" / "bubu-phone.gif", limit=20)
    visit_frames = load_gif_frames(IMAGE_DIR / "collection" / "bubu-visit.gif", limit=20)
    frames: list[Image.Image] = []
    for index in range(20):
        canvas = Image.new("RGB", (WIDTH, HEIGHT), "#edf4f0")
        draw = ImageDraw.Draw(canvas)
        draw_topbar(draw, "今天下午的桌面")

        draw.rounded_rectangle((58, 118, 620, 430), radius=10, fill="#ffffff", outline="#d5dfdb", width=2)
        draw.text((92, 154), "还在写的那封邮件", fill="#17231e", font=FONT_28_BOLD)
        draw.rounded_rectangle((92, 214, 560, 236), radius=6, fill="#dbe7e1")
        draw.rounded_rectangle((92, 258, 486, 280), radius=6, fill="#e7eee9")
        draw.rounded_rectangle((92, 302, 520, 324), radius=6, fill="#dbe7e1")
        draw.rounded_rectangle((92, 346, 404, 368), radius=6, fill="#f3d990")

        draw_speech_bubble(
            canvas,
            (668, 126, 1140, 318),
            "搭子送来一张",
            "下班啦，\n记得抬头看我一眼。",
            (980, 390),
        )

        home = resize_sprite(home_frames[index % len(home_frames)], 250)
        visitor = resize_sprite(visit_frames[index % len(visit_frames)], 250)
        bounce = -6 if index % 10 in (2, 3, 4) else 0
        canvas.paste(home, (86, 470 + bounce), home)
        canvas.paste(visitor, (860, 456 + bounce), visitor)
        frames.append(canvas)
    save_gif(frames, OUTPUT_DIR / "visit-showcase.gif")


def build_together_scene() -> None:
    left_frames = load_gif_frames(IMAGE_DIR / "collection" / "bubu-play.gif", limit=20)
    right_frames = load_gif_frames(IMAGE_DIR / "collection" / "bubu-together.gif", limit=20)
    frames: list[Image.Image] = []
    for index in range(20):
        canvas = Image.new("RGB", (WIDTH, HEIGHT), "#203d33")
        draw = ImageDraw.Draw(canvas)
        draw_topbar(draw, "小剧场：下班后来坐一会儿")
        draw.rectangle((0, 62, 92, HEIGHT), fill="#ed765f")
        draw.rectangle((WIDTH - 92, 62, WIDTH, HEIGHT), fill="#ed765f")
        draw.rectangle((92, 646, WIDTH - 92, HEIGHT), fill="#e7ba4d")
        draw.line((92, 646, WIDTH - 92, 646), fill="#f5d98c", width=4)
        draw.ellipse((166, 604, 500, 690), fill="#183028")
        draw.ellipse((696, 604, 1030, 690), fill="#183028")

        if index < 10:
            draw_speech_bubble(
                canvas,
                (142, 114, 530, 312),
                "左边这只",
                "忙完了吗，\n我先过来坐一会儿。",
                (392, 400),
                accent="#d45443",
            )
        else:
            draw_speech_bubble(
                canvas,
                (670, 114, 1058, 312),
                "右边这只",
                "坐吧，\n我不赶你走。",
                (800, 400),
                accent="#16846f",
            )

        left_sprite = resize_sprite(left_frames[index % len(left_frames)], 280)
        right_sprite = resize_sprite(right_frames[index % len(right_frames)], 280)
        left_y = 400 - (8 if index % 8 in (2, 3) else 0)
        right_y = 400 - (8 if index % 8 in (5, 6) else 0)
        canvas.paste(left_sprite, (216, left_y), left_sprite)
        canvas.paste(right_sprite, (702, right_y), right_sprite)
        frames.append(canvas)
    save_gif(frames, OUTPUT_DIR / "together-showcase.gif")


def main() -> None:
    build_visit_scene()
    build_together_scene()


if __name__ == "__main__":
    main()
