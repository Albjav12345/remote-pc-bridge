"""Build a portfolio-ready video from the repository's fictional sample screenshots.

Requires Pillow and imageio-ffmpeg (or ffmpeg on PATH). No live app state,
credentials, network requests, or screen capture are used.
"""

from __future__ import annotations

import argparse
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

from PIL import Image, ImageDraw, ImageFilter, ImageFont, ImageOps


ROOT = Path(__file__).resolve().parents[1]
IMAGES = ROOT / "docs" / "images"
OUTPUT = ROOT / "docs" / "media"
SIZE = (1920, 1080)
BG = (10, 16, 22)
WHITE = (244, 249, 252)
MUTED = (168, 189, 198)
GREEN = (80, 226, 162)
BLUE = (60, 150, 255)


def font(size: int, bold: bool = False, mono: bool = False) -> ImageFont.FreeTypeFont:
    name = "consolab.ttf" if mono and bold else "consola.ttf" if mono else "segoeuib.ttf" if bold else "segoeui.ttf"
    path = Path(os.environ.get("WINDIR", r"C:\Windows")) / "Fonts" / name
    if not path.exists():
        for fallback in ("DejaVuSansMono-Bold.ttf" if mono and bold else "DejaVuSansMono.ttf" if mono else "DejaVuSans-Bold.ttf" if bold else "DejaVuSans.ttf",):
            try:
                return ImageFont.truetype(fallback, size)
            except OSError:
                pass
        raise FileNotFoundError(f"No font available for {name}")
    return ImageFont.truetype(str(path), size)


def sample(name: str) -> Image.Image:
    path = IMAGES / name
    image = Image.open(path).convert("RGB")
    if image.size != (2910, 1830):
        raise ValueError(f"Unexpected sample screenshot size: {path}")
    return image


def background(source: Image.Image, index: int) -> Image.Image:
    base = ImageOps.fit(source, SIZE, method=Image.Resampling.LANCZOS)
    base = base.filter(ImageFilter.GaussianBlur(70)).convert("RGBA")
    shade = Image.new("RGBA", SIZE, (4, 10, 16, 226))
    base = Image.alpha_composite(base, shade)
    glow = Image.new("RGBA", SIZE)
    gd = ImageDraw.Draw(glow)
    gd.ellipse((1030, -350, 2260, 660), fill=(38, 144, 255, 42))
    gd.ellipse((-500, 460, 650, 1560), fill=(34, 224, 149, 30))
    glow = glow.filter(ImageFilter.GaussianBlur(150))
    base = Image.alpha_composite(base, glow)
    grid = Image.new("RGBA", SIZE)
    draw = ImageDraw.Draw(grid)
    for x in range(0, 1921, 80):
        draw.line((x, 0, x, 1080), fill=(149, 211, 207, 12), width=1)
    for y in range(0, 1081, 80):
        draw.line((0, y, 1920, y), fill=(149, 211, 207, 12), width=1)
    base = Image.alpha_composite(base, grid)
    draw = ImageDraw.Draw(base)
    draw.line((74, 82, 1846, 82), fill=(129, 173, 185, 62), width=2)
    draw.line((74, 1000, 1846, 1000), fill=(129, 173, 185, 62), width=2)
    draw.text((76, 42), "AMEDINA.DEV / SELECTED SYSTEMS", font=font(18, mono=True), fill=MUTED)
    draw.text((1520, 42), "SYS_06  /  REMOTE ACCESS", font=font(18, mono=True), fill=GREEN)
    draw.text((76, 1023), "REMOTE PC BRIDGE  /  UI DEMO", font=font(17, mono=True), fill=MUTED)
    draw.text((1470, 1023), "SIMULATED DATA  •  NO LIVE CONNECTION", font=font(15, mono=True), fill=(123, 160, 169))
    draw.rounded_rectangle((77 + index * 275, 991, 290 + index * 275, 996), radius=3, fill=GREEN)
    return base.convert("RGB")


def screen_card(canvas: Image.Image, source: Image.Image, box: tuple[int, int, int, int],
                crop: tuple[int, int, int, int] | None = None, label: str = "APPLICATION / WINDOWS 11") -> None:
    x0, y0, x1, y1 = box
    shadow = Image.new("RGBA", SIZE)
    sd = ImageDraw.Draw(shadow)
    sd.rounded_rectangle((x0 - 20, y0 - 16, x1 + 20, y1 + 30), radius=34, fill=(0, 0, 0, 160))
    shadow = shadow.filter(ImageFilter.GaussianBlur(26))
    canvas.paste(Image.alpha_composite(canvas.convert("RGBA"), shadow).convert("RGB"))
    draw = ImageDraw.Draw(canvas)
    draw.rounded_rectangle((x0 - 10, y0 - 42, x1 + 10, y1 + 10), radius=22,
                           fill=(24, 35, 45), outline=(79, 111, 124), width=2)
    draw.ellipse((x0 + 10, y0 - 26, x0 + 21, y0 - 15), fill=GREEN)
    draw.ellipse((x0 + 31, y0 - 26, x0 + 42, y0 - 15), fill=BLUE)
    draw.ellipse((x0 + 52, y0 - 26, x0 + 63, y0 - 15), fill=(121, 145, 153))
    draw.text((x0 + 84, y0 - 33), label, font=font(16, mono=True), fill=(171, 193, 201))
    im = source.crop(crop) if crop else source
    fitted = ImageOps.fit(im, (x1 - x0, y1 - y0), method=Image.Resampling.LANCZOS)
    mask = Image.new("L", fitted.size)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, fitted.width, fitted.height), radius=12, fill=255)
    canvas.paste(fitted, (x0, y0), mask)
    draw = ImageDraw.Draw(canvas)
    draw.rounded_rectangle((x0, y0, x1, y1), radius=12, outline=(95, 133, 149), width=2)


def left_copy(canvas: Image.Image, number: str, kicker: str, lines: list[str],
              body: list[str], chips: list[str]) -> None:
    d = ImageDraw.Draw(canvas)
    d.rounded_rectangle((92, 211, 245, 254), radius=12, fill=(18, 55, 48), outline=(44, 122, 94), width=2)
    d.text((110, 219), f"{number} / {kicker}", font=font(18, mono=True), fill=GREEN)
    y = 318
    for i, line in enumerate(lines):
        d.text((92, y), line, font=font(70, bold=True), fill=GREEN if i == len(lines) - 1 and number == "00" else WHITE)
        y += 81
    y += 30
    for line in body:
        d.text((96, y), line, font=font(28), fill=MUTED)
        y += 40
    y = 838
    for chip in chips:
        w = int(d.textlength(chip, font=font(17, mono=True))) + 34
        if 96 + w > 580:
            break
        d.rounded_rectangle((96, y, 96 + w, y + 41), radius=10, fill=(23, 44, 49), outline=(50, 101, 91), width=2)
        d.text((113, y + 10), chip, font=font(17, mono=True), fill=(183, 235, 210))
        y += 52


def regular_scene(index: int, screenshot: str, number: str, kicker: str,
                  lines: list[str], body: list[str], chips: list[str],
                  crop: tuple[int, int, int, int] | None = None) -> Image.Image:
    source = sample(screenshot)
    canvas = background(source, index)
    screen_card(canvas, source, (652, 197, 1818, 932), crop)
    left_copy(canvas, number, kicker, lines, body, chips)
    return canvas


def theme_scene(index: int) -> Image.Image:
    dark, light = sample("dashboard.png"), sample("dashboard.light.png")
    canvas = background(dark, index)
    left_copy(canvas, "04", "DESIGN", ["FITS YOUR", "DESKTOP."],
              ["Windows-inspired UI.", "English or Spanish.", "Light or dark."], ["PERSONALIZE"])
    screen_card(canvas, dark, (650, 210, 1580, 795), label="DARK MODE")
    screen_card(canvas, light, (970, 345, 1810, 873), label="LIGHT MODE")
    d = ImageDraw.Draw(canvas)
    d.text((690, 832), "DARK", font=font(22, bold=True), fill=MUTED)
    d.text((1692, 893), "LIGHT", font=font(22, bold=True), fill=WHITE)
    return canvas


def outro(index: int) -> Image.Image:
    canvas = background(sample("dashboard.png"), index).convert("RGBA")
    d = ImageDraw.Draw(canvas)
    d.rounded_rectangle((245, 187, 1675, 877), radius=34,
                        fill=(13, 28, 36, 237), outline=(66, 127, 119, 185), width=3)
    d.rounded_rectangle((860, 249, 1060, 449), radius=45, fill=(25, 129, 231), outline=(127, 201, 255), width=3)
    d.rounded_rectangle((902, 298, 1018, 376), radius=7, outline=WHITE, width=10)
    d.line((960, 377, 960, 396), fill=WHITE, width=9)
    d.line((925, 399, 995, 399), fill=WHITE, width=9)
    headline = "REMOTE PC BRIDGE"
    f = font(83, bold=True)
    w = d.textlength(headline, font=f)
    d.text(((1920 - w) / 2, 492), headline, font=f, fill=WHITE)
    sub = "WAKE  /  DIAGNOSE  /  CONNECT"
    f = font(30, mono=True)
    w = d.textlength(sub, font=f)
    d.text(((1920 - w) / 2, 630), sub, font=f, fill=GREEN)
    url = "github.com/Albjav12345/remote-pc-bridge"
    f = font(24)
    w = d.textlength(url, font=f)
    d.text(((1920 - w) / 2, 758), url, font=f, fill=MUTED)
    return canvas.convert("RGB")


def find_ffmpeg() -> str:
    executable = shutil.which("ffmpeg")
    if executable:
        return executable
    # The optional package can be installed locally with:
    # python -m pip install --target .tools/video-python imageio-ffmpeg
    sys.path.insert(0, str(ROOT / ".tools" / "video-python"))
    try:
        import imageio_ffmpeg
    except ImportError as exc:
        raise RuntimeError("Install imageio-ffmpeg or place ffmpeg on PATH") from exc
    return imageio_ffmpeg.get_ffmpeg_exe()


def encode(ffmpeg: str, frames: list[Path], output: Path) -> None:
    durations = [4.2, 4.6, 4.6, 4.6, 4.2, 4.0]
    fps = 30
    command = [ffmpeg, "-hide_banner", "-loglevel", "warning", "-y"]
    for frame, duration in zip(frames, durations):
        command += ["-loop", "1", "-framerate", str(fps), "-t", str(duration), "-i", str(frame)]
    graph = []
    for i, duration in enumerate(durations):
        fade_in = 0.28 if i == 0 else 0.18
        fade_out = 0.28 if i == len(durations) - 1 else 0.18
        graph.append(
            f"[{i}:v]zoompan=z='min(zoom+0.000065,1.026)':"
            f"x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':"
            f"d=1:s=1920x1080:fps={fps},trim=duration={duration},"
            f"fps={fps},setpts=PTS-STARTPTS,"
            f"fade=t=in:st=0:d={fade_in},"
            f"fade=t=out:st={duration-fade_out:.2f}:d={fade_out},"
            f"format=yuv420p[v{i}]"
        )
    graph.append("".join(f"[v{i}]" for i in range(len(durations))) + f"concat=n={len(durations)}:v=1:a=0[out]")
    command += ["-filter_complex", ";".join(graph), "-map", "[out]",
                "-an", "-c:v", "libx264", "-preset", "medium", "-crf", "21",
                "-pix_fmt", "yuv420p", "-r", str(fps), "-movflags", "+faststart",
                "-metadata", "title=Remote PC Bridge - UI Demo",
                "-metadata", "comment=Simulated data; no live connection",
                str(output)]
    subprocess.run(command, check=True)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--scenes-only", action="store_true", help="Create storyboard frames without encoding")
    args = parser.parse_args()
    OUTPUT.mkdir(parents=True, exist_ok=True)
    ffmpeg = None if args.scenes_only else find_ffmpeg()
    with tempfile.TemporaryDirectory(prefix="remote-pc-demo-") as temp:
        temp_path = Path(temp)
        scenes = [
            regular_scene(0, "dashboard.png", "00", "OVERVIEW",
                          ["YOUR PC.", "ANYWHERE."],
                          ["Wake it from anywhere.", "See every step.", "Open Moonlight when ready."],
                          ["WINDOWS", "ESP32", "MOONLIGHT"]),
            regular_scene(1, "dashboard.png", "01", "WAKE",
                          ["ONE CLICK", "TO WAKE."],
                          ["The ESP32 bridges the", "remote command into", "your home network."],
                          ["FIREBASE", "WAKE-ON-LAN"], (400, 140, 2720, 1604)),
            regular_scene(2, "actions.png", "02", "DIAGNOSE",
                          ["KNOW WHAT", "RESPONDS."],
                          ["Follow Firebase, ESP32,", "Sunshine, and Tailscale", "without guessing."],
                          ["LIVE STATUS", "LOGS"], (390, 140, 2710, 1604)),
            regular_scene(3, "flash.png", "03", "SETUP",
                          ["FLASH THE", "BRIDGE."],
                          ["Detect the USB port.", "Write the firmware.", "Configure in one place."],
                          ["ESP32-WROOM-32", "USB"], (400, 366, 2720, 1830)),
            theme_scene(4),
            outro(5),
        ]
        paths = []
        for i, scene in enumerate(scenes):
            path = temp_path / f"scene-{i}.png"
            scene.save(path, optimize=True)
            paths.append(path)
        scenes[0].save(OUTPUT / "demo-poster.jpg", quality=90, optimize=True, subsampling=0)
        if args.scenes_only:
            storyboard = ROOT / ".build" / "demo-storyboard"
            storyboard.mkdir(parents=True, exist_ok=True)
            for i, scene in enumerate(scenes):
                scene.save(storyboard / f"scene-{i}.jpg", quality=85, optimize=True)
        else:
            encode(ffmpeg, paths, OUTPUT / "remote-pc-bridge-demo.mp4")


if __name__ == "__main__":
    main()
