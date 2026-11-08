"""Renders the FFR Randomizer icon and banners from art/src with a headless
Chromium browser (Playwright). Writes art/icon.ico (used by gui/build.cmd and
the installer), art/icon-512.png, art/banner-1300x372.png and
art/banner-1920x1080.png.

    pip install playwright pillow
    python tools/make_art.py                  # uses Playwright's Chromium
    python tools/make_art.py --channel msedge # or the installed Edge/Chrome

The .ico keeps bitmap entries up to 128 px and a PNG only at 256, because
.NET's icon loader turns smaller PNG entries into noise.
"""
import argparse
import io
import pathlib
import struct

from PIL import Image
from playwright.sync_api import sync_playwright

ROOT = pathlib.Path(__file__).resolve().parent.parent
ART = ROOT / "art"
SRC = ART / "src"
ICON_SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
BANNERS = [(1300, 372), (1920, 1080)]


def render_icon(browser, size):
    page = browser.new_page(viewport={"width": size, "height": size})
    page.goto((SRC / "icon.html").as_uri() + f"?size={size}")
    page.wait_for_function("document.getElementById('icon').complete")
    png = page.locator("#icon").screenshot(omit_background=True)
    page.close()
    return Image.open(io.BytesIO(png)).convert("RGBA")


def render_banner(browser, width, height, path):
    page = browser.new_page(viewport={"width": width, "height": height})
    page.goto((SRC / "banner.html").as_uri() + f"?w={width}&h={height}")
    page.evaluate("document.fonts.ready")
    page.wait_for_timeout(300)
    page.locator(".b").screenshot(path=str(path))
    page.close()


def bmp_entry(image):
    """A 32-bit DIB with its AND mask, as an .ico stores it."""
    w, h = image.size
    pixels = image.transpose(Image.FLIP_TOP_BOTTOM).tobytes("raw", "BGRA")
    mask_row = ((w + 31) // 32) * 4
    mask = bytes(mask_row * h)
    header = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, len(pixels) + len(mask), 0, 0, 0, 0)
    return header + pixels + mask


def write_ico(images, path):
    entries = []
    for image in images:
        size = image.size[0]
        if size >= 256:
            buffer = io.BytesIO()
            image.save(buffer, "PNG")
            data = buffer.getvalue()
        else:
            data = bmp_entry(image)
        entries.append((size, data))
    out = bytearray(struct.pack("<HHH", 0, 1, len(entries)))
    offset = 6 + 16 * len(entries)
    for size, data in entries:
        dim = 0 if size >= 256 else size
        out += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    for _, data in entries:
        out += data
    path.write_bytes(bytes(out))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--channel", help="msedge or chrome to use an installed browser")
    parser.add_argument("--executable", help="path to a Chromium-based browser")
    args = parser.parse_args()
    with sync_playwright() as p:
        options = {}
        if args.channel:
            options["channel"] = args.channel
        if args.executable:
            options["executable_path"] = args.executable
        browser = p.chromium.launch(**options)
        icons = [render_icon(browser, size) for size in ICON_SIZES]
        write_ico(icons, ART / "icon.ico")
        render_icon(browser, 512).save(ART / "icon-512.png")
        for width, height in BANNERS:
            render_banner(browser, width, height, ART / f"banner-{width}x{height}.png")
        browser.close()
    print("Wrote icon.ico, icon-512.png and the banners in", ART)


if __name__ == "__main__":
    main()
