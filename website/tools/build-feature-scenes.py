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


def build_interaction_scene() -> None:
    pet_frames = load_gif_frames(IMAGE_DIR / "pet-keyboard.gif", limit=20)
    frames: list[Image.Image] = []
    for index in range(20):
        canvas = Image.new("RGB", (WIDTH, HEIGHT), "#edf4f0")
        draw = ImageDraw.Draw(canvas)
        draw_topbar(draw, "\u4eca\u5929\u7684\u684c\u9762")

        draw.rounded_rectangle((58, 106, 670, 628), radius=10, fill="#ffffff", outline="#d5dfdb", width=2)
        draw.text((94, 145), "\u4e0b\u5348\u7684\u5c0f\u6e05\u5355", fill="#17231e", font=FONT_28_BOLD)
        draw.text((94, 192), "\u4e09\u4ef6\u5c0f\u4e8b\uff0c\u6162\u6162\u6765", fill="#718078", font=FONT_18)
        tasks = (
            ("\u56de\u590d\u4e24\u5c01\u90ae\u4ef6", True),
            ("\u6574\u7406\u4eca\u5929\u7684\u8bb0\u5f55", True),
            ("\u7ed9\u81ea\u5df1\u7559\u4e94\u5206\u949f", False),
        )
        for task_index, (label, done) in enumerate(tasks):
            y = 268 + task_index * 92
            fill = "#16846f" if done else "#ffffff"
            draw.rounded_rectangle((94, y, 124, y + 30), radius=8, fill=fill, outline="#aebdb7", width=2)
            if done:
                draw.line((101, y + 15, 109, y + 23, 118, y + 8), fill="#ffffff", width=3)
            draw.text((148, y - 2), label, fill="#26352f", font=FONT_24)

        draw_speech_bubble(
            canvas,
            (706, 128, 1146, 344),
            "\u684c\u642d\u5b50",
            "\u4e24\u4ef6\u4e8b\u5df2\u7ecf\u5b8c\u6210\u5566\uff0c\n\u8bb0\u5f97\u7ed9\u81ea\u5df1\u559d\u53e3\u6c34\u3002",
            (1030, 420),
        )
        chip_y = 376
        draw.rounded_rectangle((742, chip_y, 874, chip_y + 48), radius=9, fill="#16846f")
        draw.text((780, chip_y + 9), "\u597d\u5440", fill="#ffffff", font=FONT_20_BOLD)
        draw.rounded_rectangle((890, chip_y, 1080, chip_y + 48), radius=9, fill="#ffffff", outline="#cdd9d4", width=2)
        draw.text((919, chip_y + 9), "\u7b49\u6211\u4e00\u5206\u949f", fill="#405149", font=FONT_20_BOLD)

        sprite = resize_sprite(pet_frames[index % len(pet_frames)], 290)
        bounce = -6 if index % 10 in (2, 3, 4) else 0
        canvas.paste(sprite, (824, 454 + bounce), sprite)
        frames.append(canvas)
    save_gif(frames, OUTPUT_DIR / "interaction-showcase.gif")


def build_theater_scene() -> None:
    left_frames = load_gif_frames(IMAGE_DIR / "pet-chair.gif", limit=20)
    right_frames = load_gif_frames(IMAGE_DIR / "pet-balance.gif", limit=20)
    frames: list[Image.Image] = []
    for index in range(20):
        canvas = Image.new("RGB", (WIDTH, HEIGHT), "#203d33")
        draw = ImageDraw.Draw(canvas)
        draw_topbar(draw, "\u5c0f\u5267\u573a\uff1a\u6708\u66dc\u4f1a\u8bae\u9003\u751f\u8ba1\u5212")
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
                "\u5de6\u8fb9\u7684\u642d\u5b50",
                "\u4f1a\u8bae\u7ed3\u675f\uff0c\n\u53bb\u559d\u676f\u5496\u5561\uff1f",
                (392, 400),
                accent="#d45443",
            )
        else:
            draw_speech_bubble(
                canvas,
                (670, 114, 1058, 312),
                "\u53f3\u8fb9\u7684\u642d\u5b50",
                "\u6210\u4ea4\uff0c\n\u4e94\u5206\u949f\u540e\u51fa\u53d1\u3002",
                (800, 400),
                accent="#16846f",
            )

        left_sprite = resize_sprite(left_frames[index % len(left_frames)], 300)
        right_sprite = resize_sprite(right_frames[(index * 2) % len(right_frames)], 300)
        left_y = 390 - (8 if index % 8 in (2, 3) else 0)
        right_y = 390 - (8 if index % 8 in (5, 6) else 0)
        canvas.paste(left_sprite, (206, left_y), left_sprite)
        canvas.paste(right_sprite, (692, right_y), right_sprite)
        frames.append(canvas)
    save_gif(frames, OUTPUT_DIR / "theater-showcase.gif")


def main() -> None:
    build_interaction_scene()
    build_theater_scene()


if __name__ == "__main__":
    main()
