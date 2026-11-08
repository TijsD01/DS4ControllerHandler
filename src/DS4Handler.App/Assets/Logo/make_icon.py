"""Builds app.ico, the DS4 Handler logo, at every size Windows asks for.

The mark is the controller outline from the top bar (ControllerGeometry in Themes/Theme.xaml),
filled, with the DualShock 4 light bar glowing along its top and both stick caps resting dead
centre (the app's job is keeping them there). Sizes up to 24 px drop the glow and use bigger
details, so the taskbar and tray stay crisp.

Run: python3 make_icon.py   (needs Pillow). Writes app.ico and preview.png next to this file;
preview.png is only for looking at and is not shipped.
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter

HERE = Path(__file__).parent
SIZES = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
SUPERSAMPLE = 8

TILE_TOP = (0x1A, 0x27, 0x45)
TILE_BOTTOM = (0x0B, 0x12, 0x22)
TILE_EDGE = (0x34, 0x48, 0x70)
ACCENT = (0x4C, 0x8D, 0xFF)  # AccentColor in Themes/Theme.xaml
BODY = (0xEE, 0xF3, 0xFA)
WELL = (0x0F, 0x17, 0x2A)  # BgBrush

# ControllerGeometry, 72 x 48: start point, then cubic segments (c1, c2, end) and lines (end).
OUTLINE = [(20, 6), "L", (52, 6), "C", (61, 6), (66, 14), (68, 26), "C", (69.6, 35.6), (66.6, 42), (61.4, 42),
           "C", (57.4, 42), (55, 38), (52, 34), "L", (20, 34), "C", (17, 38), (14.6, 42), (10.6, 42),
           "C", (5.4, 42), (2.4, 35.6), (4, 26), "C", (6, 14), (11, 6), (20, 6)]


def outline_points() -> list[tuple[float, float]]:
    pts = [OUTLINE[0]]
    i = 1
    while i < len(OUTLINE):
        if OUTLINE[i] == "L":
            pts.append(OUTLINE[i + 1])
            i += 2
        else:
            p0, (c1, c2, p3) = pts[-1], OUTLINE[i + 1:i + 4]
            for k in range(1, 25):
                t = k / 24
                m = 1 - t
                pts.append(tuple(m**3 * a + 3 * m * m * t * b + 3 * m * t * t * c + t**3 * d
                                 for a, b, c, d in zip(p0, c1, c2, p3)))
            i += 4
    return pts


def draw(size: int) -> Image.Image:
    s = size * SUPERSAMPLE
    small = size <= 24
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))

    # Tile: vertical gradient, rounded, with a lighter edge so it shows on a dark taskbar.
    grad = Image.new("RGBA", (s, s))
    gd = ImageDraw.Draw(grad)
    for y in range(s):
        t = y / (s - 1)
        gd.line([(0, y), (s, y)], fill=tuple(round(a + (b - a) * t) for a, b in zip(TILE_TOP, TILE_BOTTOM)) + (255,))
    mask = Image.new("L", (s, s), 0)
    radius = s * 0.22
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, s - 1, s - 1], radius=radius, fill=255)
    img.paste(grad, (0, 0), mask)
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([0, 0, s - 1, s - 1], radius=radius, outline=TILE_EDGE + (255,),
                        width=max(1, round(s * (0.03 if small else 0.018))))

    # Controller, 72 x 48 design units, centred and slightly low to leave room for the glow.
    k = s * (0.86 if small else 0.8) / 72
    ox, oy = (s - 72 * k) / 2, (s - 48 * k) / 2 + s * 0.03
    at = lambda x, y: (ox + x * k, oy + y * k)

    bar_y0, bar_y1 = (6, 13) if small else (6, 11.5)
    if not small:
        glow = Image.new("RGBA", (s, s), (0, 0, 0, 0))
        ImageDraw.Draw(glow).rounded_rectangle([*at(18, 2), *at(54, 14)], radius=4 * k, fill=ACCENT + (150,))
        img.alpha_composite(glow.filter(ImageFilter.GaussianBlur(4 * k)))

    d.polygon([at(x, y) for x, y in outline_points()], fill=BODY + (255,))
    # Light bar across the top edge, clipped to the body's flat top.
    d.rounded_rectangle([*at(20, bar_y0), *at(52, bar_y1)], radius=2.5 * k, fill=ACCENT + (255,))
    d.rectangle([*at(22, bar_y0), *at(50, bar_y0 + 2.5)], fill=ACCENT + (255,))

    # Sticks: dark well, accent cap centred.
    well_r, cap_r = (6.6, 3.0) if small else (5.6, 2.4)
    for cx in (25, 47):
        cy = 25 if small else 24
        d.ellipse([*at(cx - well_r, cy - well_r), *at(cx + well_r, cy + well_r)], fill=WELL + (255,))
        d.ellipse([*at(cx - cap_r, cy - cap_r), *at(cx + cap_r, cy + cap_r)], fill=ACCENT + (255,))

    return img.resize((size, size), Image.LANCZOS)


def main() -> None:
    frames = [draw(n) for n in SIZES]
    frames[-1].save(HERE / "app.ico", format="ICO", sizes=[(n, n) for n in SIZES], append_images=frames[:-1])

    # Every size on a light and a dark taskbar colour.
    width = sum(SIZES) + 12 * (len(SIZES) + 1)
    sheet = Image.new("RGBA", (width, 2 * 256 + 36), (0, 0, 0, 0))
    sd = ImageDraw.Draw(sheet)
    sd.rectangle([0, 0, width, 256 + 18], fill=(0xF3, 0xF3, 0xF3, 255))
    sd.rectangle([0, 256 + 18, width, 2 * 256 + 36], fill=(0x20, 0x20, 0x20, 255))
    x = 12
    for f in frames:
        sheet.alpha_composite(f, (x, 9))
        sheet.alpha_composite(f, (x, 256 + 27))
        x += f.width + 12
    sheet.save(HERE / "preview.png")


if __name__ == "__main__":
    main()
