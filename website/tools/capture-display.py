import argparse
import ctypes
import time
from pathlib import Path

from PIL import Image, ImageGrab


class Rect(ctypes.Structure):
    _fields_ = [
        ("left", ctypes.c_long),
        ("top", ctypes.c_long),
        ("right", ctypes.c_long),
        ("bottom", ctypes.c_long),
    ]


def window_bounds(handle: int) -> tuple[int, int, int, int]:
    rect = Rect()
    if not ctypes.windll.user32.GetWindowRect(handle, ctypes.byref(rect)):
        raise OSError(f"Unable to read window bounds for {handle}.")
    return rect.left, rect.top, rect.right, rect.bottom


def capture_window(handle: int) -> Image.Image:
    left, top, right, bottom = window_bounds(handle)
    width = right - left
    height = bottom - top
    user32 = ctypes.windll.user32
    gdi32 = ctypes.windll.gdi32
    window_dc = user32.GetWindowDC(handle)
    memory_dc = gdi32.CreateCompatibleDC(window_dc)
    bitmap = gdi32.CreateCompatibleBitmap(window_dc, width, height)
    old_bitmap = gdi32.SelectObject(memory_dc, bitmap)
    try:
        captured = user32.PrintWindow(handle, memory_dc, 2)
        if not captured:
            return ImageGrab.grab(bbox=(left, top, right, bottom), all_screens=True)

        class BitmapInfoHeader(ctypes.Structure):
            _fields_ = [
                ("size", ctypes.c_uint32),
                ("width", ctypes.c_int32),
                ("height", ctypes.c_int32),
                ("planes", ctypes.c_uint16),
                ("bit_count", ctypes.c_uint16),
                ("compression", ctypes.c_uint32),
                ("image_size", ctypes.c_uint32),
                ("x_pixels_per_meter", ctypes.c_int32),
                ("y_pixels_per_meter", ctypes.c_int32),
                ("colors_used", ctypes.c_uint32),
                ("colors_important", ctypes.c_uint32),
            ]

        class BitmapInfo(ctypes.Structure):
            _fields_ = [("header", BitmapInfoHeader), ("colors", ctypes.c_uint32 * 3)]

        info = BitmapInfo()
        info.header.size = ctypes.sizeof(BitmapInfoHeader)
        info.header.width = width
        info.header.height = height
        info.header.planes = 1
        info.header.bit_count = 32
        info.header.compression = 0
        buffer = (ctypes.c_ubyte * (width * height * 4))()
        gdi32.GetDIBits(memory_dc, bitmap, 0, height, buffer, ctypes.byref(info), 0)
        return Image.frombuffer("RGB", (width, height), bytes(buffer), "raw", "BGRX", 0, 1).transpose(Image.Transpose.FLIP_TOP_BOTTOM)
    finally:
        gdi32.SelectObject(memory_dc, old_bitmap)
        gdi32.DeleteObject(bitmap)
        gdi32.DeleteDC(memory_dc)
        user32.ReleaseDC(handle, window_dc)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("output", type=Path)
    parser.add_argument("--window-handle", type=int)
    parser.add_argument("--frames", type=int, default=1)
    parser.add_argument("--interval", type=float, default=0.25)
    args = parser.parse_args()

    if args.frames < 1:
        raise SystemExit("Frames must be at least 1.")

    output = args.output.resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    if args.frames > 1:
        output.mkdir(parents=True, exist_ok=True)

    bounds = window_bounds(args.window_handle) if args.window_handle else None
    for index in range(args.frames):
        frame = capture_window(args.window_handle) if args.window_handle else ImageGrab.grab(all_screens=True)
        frame_path = output / f"frame-{index:04d}.png" if args.frames > 1 else output
        frame.save(frame_path, "PNG")
        if index < args.frames - 1:
            time.sleep(args.interval)


if __name__ == "__main__":
    main()
