from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont, ImageSequence


ROOT = Path(__file__).resolve().parents[1]
IMAGE_DIR = ROOT / "source" / "images"
OUTPUT_DIR = IMAGE_DIR / "screenshots"
WIDTH = 1200
HEIGHT = 760
PHONE_W = 360
PHONE_H = 720


def load_font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont:
    font_name = "msyhbd.ttc" if bold else "msyh.ttc"
    return ImageFont.truetype(str(Path("C:/Windows/Fonts") / font_name), size)


FONT_16 = load_font(16)
FONT_18 = load_font(18)
FONT_18_BOLD = load_font(18, bold=True)
FONT_20_BOLD = load_font(20, bold=True)
FONT_24_BOLD = load_font(24, bold=True)
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


def rounded_mask(size: tuple[int, int], radius: int) -> Image.Image:
    mask = Image.new("L", size, 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, size[0] - 1, size[1] - 1), radius=radius, fill=255)
    return mask


def draw_phone_shell(canvas: Image.Image, origin: tuple[int, int]) -> tuple[int, int, int, int]:
    x, y = origin
    shell = Image.new("RGBA", (PHONE_W, PHONE_H), (0, 0, 0, 0))
    draw = ImageDraw.Draw(shell)
    draw.rounded_rectangle((0, 0, PHONE_W - 1, PHONE_H - 1), radius=46, fill="#111111")
    draw.rounded_rectangle((10, 10, PHONE_W - 11, PHONE_H - 11), radius=38, fill="#f3f7f5")
    draw.rounded_rectangle((138, 18, 222, 34), radius=10, fill="#111111")
    canvas.alpha_composite(shell, origin)
    return (x + 10, y + 10, x + PHONE_W - 10, y + PHONE_H - 10)


def draw_status(draw: ImageDraw.ImageDraw, box: tuple[int, int, int, int], time_text: str, light: bool = False) -> None:
    x1, y1, x2, _ = box
    color = "#ffffff" if light else "#17201e"
    draw.text((x1 + 16, y1 + 12), time_text, fill=color, font=FONT_16)
    draw.text((x2 - 78, y1 + 12), "5G  86%", fill=color, font=FONT_16)


def save_gif(frames: list[Image.Image], output: Path, duration: int = 140) -> None:
    output.parent.mkdir(parents=True, exist_ok=True)
    palette_frames = [frame.convert("P", palette=Image.Palette.ADAPTIVE, colors=160) for frame in frames]
    palette_frames[0].save(
        output,
        save_all=True,
        append_images=palette_frames[1:],
        duration=duration,
        loop=0,
        optimize=True,
        disposal=2,
    )


def draw_copy(draw: ImageDraw.ImageDraw, kicker: str, title: str, body: str, left: int = 72, top: int = 88) -> None:
    draw.text((left, top), kicker, fill="#d97752", font=FONT_18_BOLD)
    draw.multiline_text((left, top + 34), title, fill="#17201e", font=FONT_36_BOLD, spacing=8)
    draw.multiline_text((left, top + 168), body, fill="#5d6b67", font=FONT_20_BOLD, spacing=8)


def build_video_overlay() -> None:
    visit_frames = load_gif_frames(IMAGE_DIR / "collection" / "bubu-visit.gif", limit=20)
    frames: list[Image.Image] = []
    for index in range(20):
        canvas = Image.new("RGBA", (WIDTH, HEIGHT), "#20352d")
        draw = ImageDraw.Draw(canvas)
        draw.text((72, 72), "安卓也能这么回", fill="#e86e55", font=FONT_18_BOLD)
        draw.multiline_text((72, 110), "刷短视频时\n桌角来了一只熊", fill="#f7f5ef", font=FONT_36_BOLD, spacing=8)
        draw.multiline_text((72, 250), "不是壁纸。\n是对面派过来的。", fill="#c9d4ce", font=FONT_20_BOLD, spacing=8)

        phone_origin = (760, 28)
        inner = draw_phone_shell(canvas, phone_origin)
        screen = Image.new("RGBA", (inner[2] - inner[0], inner[3] - inner[1]), "#10151c")
        screen_draw = ImageDraw.Draw(screen)
        screen_draw.rectangle((0, 0, screen.width, 34), fill="#111111")
        for i, y in enumerate((120, 210, 300)):
            screen_draw.ellipse((screen.width - 42, y, screen.width - 18, y + 24), fill=(255, 255, 255, 40))
        screen_draw.multiline_text((16, screen.height - 148), "下班了吗\n先不回也行", fill="#ffffff", font=FONT_18_BOLD, spacing=4)
        bounce = -5 if index % 10 in (2, 3, 4) else 0
        visitor = resize_sprite(visit_frames[index % len(visit_frames)], 148)
        screen.alpha_composite(visitor, (screen.width - 156, screen.height - 196 + bounce))
        chip = Image.new("RGBA", (126, 30), (0, 0, 0, 0))
        ImageDraw.Draw(chip).rounded_rectangle((0, 0, 125, 29), radius=6, fill="#ffffff", outline="#20352d", width=2)
        ImageDraw.Draw(chip).text((10, 5), "搭子发来一张", fill="#20352d", font=FONT_16)
        screen.alpha_composite(chip, (screen.width - 148, screen.height - 228 + bounce))
        mask = rounded_mask(screen.size, 34)
        canvas.paste(screen, (inner[0], inner[1]), mask)
        frames.append(canvas.convert("RGB"))
    save_gif(frames, OUTPUT_DIR / "android-visit.gif")


def build_wechat_overlay() -> None:
    home_frames = load_gif_frames(IMAGE_DIR / "collection" / "bubu-home.gif", limit=20)
    frames: list[Image.Image] = []
    for index in range(20):
        canvas = Image.new("RGBA", (WIDTH, HEIGHT), "#f3f7f5")
        draw = ImageDraw.Draw(canvas)
        draw_copy(draw, "手机上长这样", "悬浮在微信上面", "不弹窗，也不催回复。\n原来那只还在。")

        phone_origin = (760, 28)
        inner = draw_phone_shell(canvas, phone_origin)
        screen = Image.new("RGBA", (inner[2] - inner[0], inner[3] - inner[1]), "#ededed")
        screen_draw = ImageDraw.Draw(screen)
        screen_draw.rectangle((0, 0, screen.width, 78), fill="#ededed")
        screen_draw.text((16, 14), "12:20", fill="#17201e", font=FONT_16)
        screen_draw.text((screen.width - 78, 14), "5G  74%", fill="#17201e", font=FONT_16)
        screen_draw.text((16, 44), "‹  搭子", fill="#111111", font=FONT_20_BOLD)
        screen_draw.rounded_rectangle((16, 96, 236, 136), radius=8, fill="#ffffff")
        screen_draw.text((26, 106), "我在开会，晚点回你", fill="#111111", font=FONT_16)
        screen_draw.rounded_rectangle((118, 150, 324, 190), radius=8, fill="#95ec69")
        screen_draw.text((132, 160), "那我不催了", fill="#111111", font=FONT_16)
        screen_draw.rounded_rectangle((16, 204, 248, 244), radius=8, fill="#ffffff")
        screen_draw.text((26, 214), "？你发了什么过来", fill="#111111", font=FONT_16)
        bounce = -4 if index % 8 in (3, 4) else 0
        home = resize_sprite(home_frames[index % len(home_frames)], 132)
        screen.alpha_composite(home, (12, screen.height - 168 + bounce))
        mask = rounded_mask(screen.size, 34)
        canvas.paste(screen, (inner[0], inner[1]), mask)
        frames.append(canvas.convert("RGB"))
    save_gif(frames, OUTPUT_DIR / "android-wechat.gif")


def build_send_home() -> None:
    send_frames = load_gif_frames(IMAGE_DIR / "collection" / "bubu-send.gif", limit=20)
    frames: list[Image.Image] = []
    for index in range(20):
        canvas = Image.new("RGBA", (WIDTH, HEIGHT), "#f6cf63")
        draw = ImageDraw.Draw(canvas)
        draw_copy(draw, "打开 App", "点一下\n发给搭子", "Windows / Mac / 安卓\n都能先养一只。")

        phone_origin = (760, 28)
        inner = draw_phone_shell(canvas, phone_origin)
        screen = Image.new("RGBA", (inner[2] - inner[0], inner[3] - inner[1]), "#f3f7f5")
        screen_draw = ImageDraw.Draw(screen)
        screen_draw.text((16, 14), "18:42", fill="#17201e", font=FONT_16)
        screen_draw.text((screen.width - 78, 14), "5G  90%", fill="#17201e", font=FONT_16)
        screen_draw.rounded_rectangle((16, 42, 40, 66), radius=6, fill="#17201e")
        screen_draw.text((20, 44), "宠", fill="#f2c861", font=FONT_16)
        screen_draw.text((48, 40), "桌搭子", fill="#17201e", font=FONT_18_BOLD)
        screen_draw.text((48, 62), "首页", fill="#5d6b67", font=FONT_16)
        screen_draw.rounded_rectangle((246, 46, 318, 68), radius=12, fill="#dff1eb")
        screen_draw.text((256, 48), "已激活", fill="#0c5f52", font=FONT_16)
        screen_draw.text((16, 86), "今天也一起", fill="#17201e", font=FONT_20_BOLD)
        screen_draw.text((16, 114), "先让桌宠出来，再随手发一张。", fill="#5d6b67", font=FONT_16)
        screen_draw.rounded_rectangle((16, 146, screen.width - 16, 292), radius=8, fill="#17201e")
        screen_draw.text((28, 158), "布布", fill="#ffffff", font=FONT_16)
        screen_draw.text((28, 178), "正在陪伴", fill="#f2c861", font=FONT_16)
        pet = resize_sprite(send_frames[index % len(send_frames)], 112)
        screen.alpha_composite(pet, ((screen.width - 112) // 2, 172))
        screen_draw.rounded_rectangle((16, 308, screen.width - 16, 404), radius=8, fill="#ffffff", outline="#d8e2de", width=1)
        screen_draw.text((28, 320), "桌宠待在屏幕边角", fill="#147d6b", font=FONT_16)
        screen_draw.text((28, 342), "点它打开菜单，或直接发给搭子。", fill="#5d6b67", font=FONT_16)
        pulse = 8 if 8 <= index <= 13 else 0
        screen_draw.rounded_rectangle((28, 366 - pulse // 8, screen.width - 28, 394 + pulse // 8), radius=6, fill="#147d6b")
        screen_draw.text((118, 370), "发给搭子", fill="#ffffff", font=FONT_18_BOLD)
        mask = rounded_mask(screen.size, 34)
        canvas.paste(screen, (inner[0], inner[1]), mask)
        frames.append(canvas.convert("RGB"))
    save_gif(frames, OUTPUT_DIR / "android-send.gif")


def main() -> None:
    build_video_overlay()
    build_wechat_overlay()
    build_send_home()


if __name__ == "__main__":
    main()
