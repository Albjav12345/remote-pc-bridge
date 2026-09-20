"""Render the project cover from the fictional dashboard sample screenshot.

Requires Pillow. No app settings, credentials, screen capture, or network access
are read. The capture's black padding is trimmed before it enters the mockup.
"""

from __future__ import annotations

import os
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont, ImageOps


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "docs" / "images" / "dashboard.png"
OUTPUT = ROOT / "docs" / "media" / "remote-pc-bridge-cover.png"
SIZE = (1920, 1080)
WHITE = (244, 249, 252)
MUTED = (168, 189, 198)
GREEN = (80, 226, 162)
BLUE = (60, 150, 255)


def font(size: int, bold: bool = False, mono: bool = False) -> ImageFont.FreeTypeFont:
    name = "consolab.ttf" if mono and bold else "consola.ttf" if mono else "segoeuib.ttf" if bold else "segoeui.ttf"
    path = Path(os.environ.get("WINDIR", r"C:\Windows")) / "Fonts" / name
    if path.exists():
        return ImageFont.truetype(str(path), size)
    fallback = "DejaVuSansMono-Bold.ttf" if mono and bold else "DejaVuSansMono.ttf" if mono else "DejaVuSans-Bold.ttf" if bold else "DejaVuSans.ttf"
    return ImageFont.truetype(fallback, size)


def trim_black_capture(image: Image.Image) -> Image.Image:
    bounds = ImageChops.difference(image, Image.new("RGB", image.size)).getbbox()
    if bounds is None:
        raise ValueError("The sample screenshot is empty")
    return image.crop(bounds)


def background(source: Image.Image) -> Image.Image:
    base = ImageOps.fit(source, SIZE, method=Image.Resampling.LANCZOS)
    base = base.filter(ImageFilter.GaussianBlur(70)).convert("RGBA")
    base = Image.alpha_composite(base, Image.new("RGBA", SIZE, (4, 10, 16, 226)))

    glow = Image.new("RGBA", SIZE)
    d = ImageDraw.Draw(glow)
    d.ellipse((1030, -350, 2260, 660), fill=(38, 144, 255, 42))
    d.ellipse((-500, 460, 650, 1560), fill=(34, 224, 149, 30))
    base = Image.alpha_composite(base, glow.filter(ImageFilter.GaussianBlur(150)))

    grid = Image.new("RGBA", SIZE)
    d = ImageDraw.Draw(grid)
    for x in range(0, SIZE[0] + 1, 80):
        d.line((x, 0, x, SIZE[1]), fill=(149, 211, 207, 12), width=1)
    for y in range(0, SIZE[1] + 1, 80):
        d.line((0, y, SIZE[0], y), fill=(149, 211, 207, 12), width=1)
    base = Image.alpha_composite(base, grid)

    d = ImageDraw.Draw(base)
    d.line((74, 82, 1846, 82), fill=(129, 173, 185, 62), width=2)
    d.line((74, 1000, 1846, 1000), fill=(129, 173, 185, 62), width=2)
    d.text((76, 42), "AMEDINA.DEV / SELECTED SYSTEMS", font=font(18, mono=True), fill=MUTED)
    d.text((1520, 42), "SYS_06  /  REMOTE ACCESS", font=font(18, mono=True), fill=GREEN)
    d.text((76, 1023), "REMOTE PC BRIDGE  /  PROJECT PREVIEW", font=font(17, mono=True), fill=MUTED)
    d.text((1470, 1023), "SIMULATED DATA  •  NO LIVE CONNECTION", font=font(15, mono=True), fill=(123, 160, 169))
    return base.convert("RGB")


def screen_card(canvas: Image.Image, source: Image.Image) -> None:
    # Source content is 2883 × 1760 after trimming. The 1196 × 730 content area
    # has the same ratio, so the UI fits with neither cropping nor letterboxing.
    x0, y0, x1 = 634, 191, 1830
    y1 = y0 + round((x1 - x0) * source.height / source.width)

    shadow = Image.new("RGBA", SIZE)
    ImageDraw.Draw(shadow).rounded_rectangle(
        (x0 - 20, y0 - 16, x1 + 20, y1 + 30), radius=34, fill=(0, 0, 0, 150)
    )
    canvas.paste(Image.alpha_composite(canvas.convert("RGBA"), shadow.filter(ImageFilter.GaussianBlur(26))).convert("RGB"))

    d = ImageDraw.Draw(canvas)
    d.rounded_rectangle((x0 - 10, y0 - 42, x1 + 10, y1 + 10), radius=22,
                        fill=(24, 35, 45), outline=(79, 111, 124), width=2)
    for offset, color in ((10, GREEN), (31, BLUE), (52, (121, 145, 153))):
        d.ellipse((x0 + offset, y0 - 26, x0 + offset + 11, y0 - 15), fill=color)
    d.text((x0 + 84, y0 - 33), "APPLICATION / WINDOWS 11", font=font(16, mono=True), fill=(171, 193, 201))

    fitted = source.resize((x1 - x0, y1 - y0), Image.Resampling.LANCZOS)
    mask = Image.new("L", fitted.size)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, fitted.width, fitted.height), radius=11, fill=255)
    canvas.paste(fitted, (x0, y0), mask)
    d.rounded_rectangle((x0, y0, x1, y1), radius=11, outline=(95, 133, 149), width=2)


def copy(canvas: Image.Image) -> None:
    d = ImageDraw.Draw(canvas)
    d.rounded_rectangle((92, 211, 275, 254), radius=12, fill=(18, 55, 48), outline=(44, 122, 94), width=2)
    d.text((110, 219), "06 / OVERVIEW", font=font(18, mono=True), fill=GREEN)
    d.text((92, 318), "YOUR PC.", font=font(70, bold=True), fill=WHITE)
    d.text((92, 399), "ANYWHERE.", font=font(70, bold=True), fill=GREEN)
    for i, line in enumerate(("Wake it from anywhere.", "See every step.", "Open Moonlight when ready.")):
        d.text((96, 550 + i * 40), line, font=font(28), fill=MUTED)
    for i, chip in enumerate(("WINDOWS", "ESP32", "MOONLIGHT")):
        y = 818 + i * 52
        width = int(d.textlength(chip, font=font(17, mono=True))) + 34
        d.rounded_rectangle((96, y, 96 + width, y + 41), radius=10,
                            fill=(23, 44, 49), outline=(50, 101, 91), width=2)
        d.text((113, y + 10), chip, font=font(17, mono=True), fill=(183, 235, 210))


def main() -> None:
    source = trim_black_capture(Image.open(SOURCE).convert("RGB"))
    canvas = background(source)
    screen_card(canvas, source)
    copy(canvas)
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(OUTPUT, optimize=True)
    print(f"Wrote {OUTPUT} ({canvas.width} × {canvas.height}); sample crop: {source.width} × {source.height}")


if __name__ == "__main__":
    main()
