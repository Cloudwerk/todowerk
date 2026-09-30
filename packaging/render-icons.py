#!/usr/bin/env python3
"""Render the three TodoWerk icons from one geometry.

The colour icon, the outline icon and the Entra ID app logo are the same mark at three sizes, and
the Teams Store makes it a *must fix* that the colour and outline icons depict the same symbol. So
they are generated together rather than drawn separately: there is no way for one to be updated and
the others forgotten.

    pip install pillow
    python packaging/render-icons.py

The style is the CloudWerk house style; its measurements are recorded beside the constants below.
Changing the mark should mean changing GLYPH or FONT here and re-running, nothing else.
"""

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

TEAL = (0, 105, 112)  # #006970, and the manifest's accentColor
BAND = (40, 129, 134)  # #288186 - TEAL mixed with 15.7% white, the house style's ratio
GLYPH = "#"

# Georgia Bold is the house icon face. Listed by path because the faces a renderer can reach are
# not the faces a browser can.
FONT_CANDIDATES = (
    r"C:\Windows\Fonts\georgiab.ttf",
    "/usr/share/fonts/truetype/msttcorefonts/Georgia_Bold.ttf",
    "/Library/Fonts/Georgia Bold.ttf",
)

SUPERSAMPLE = 8  # rendered this many times over and reduced with LANCZOS, so the curves are clean

BAND_FRACTION = 33 / 192  # the house style's band: the bottom 33 of 192 rows
CENTRE_FRACTION = 89 / 192  # the glyph's optical centre, a little above the tile's middle
HEIGHT_FRACTION = 86 / 192  # its cap height; the reference is 85, and 86 keeps the hash symmetrical


def _font_path() -> str:
    for candidate in FONT_CANDIDATES:
        if Path(candidate).exists():
            return candidate

    raise SystemExit(
        "Georgia Bold was not found. It ships with Windows and with the Microsoft core fonts "
        f"package elsewhere. Looked in:\n  " + "\n  ".join(FONT_CANDIDATES)
    )


def _size_for_height(font_path: str, target: int) -> int:
    """The point size at which the glyph's *inked* height is `target` pixels.

    Point size is not glyph height - it includes the ascender and descender the hash does not use -
    so the size is searched for rather than calculated. This is what keeps the geometry fixed when
    the face changes.
    """
    low, high = 4, target * 6

    while low < high:
        middle = (low + high) // 2
        probe = Image.new("L", (middle * 4, middle * 4), 0)
        ImageDraw.Draw(probe).text(
            (middle * 2, middle * 2),
            GLYPH,
            font=ImageFont.truetype(font_path, middle),
            fill=255,
            anchor="mm",
        )
        box = probe.getbbox()
        height = 0 if box is None else box[3] - box[1]

        if height < target:
            low = middle + 1
        else:
            high = middle

    return low


def _offset_to_centre(canvas_size, font, centre):
    """How far to move the glyph so its ink, not its advance width, sits on `centre`.

    A typeface's side bearings are not symmetrical, so drawing anchored at the centre leaves the
    mark visibly off it. Drawn once to find out where the ink landed, then corrected.
    """
    probe = Image.new("L", canvas_size, 0)
    ImageDraw.Draw(probe).text(centre, GLYPH, font=font, fill=255, anchor="mm")
    box = probe.getbbox()

    return (
        centre[0] - (box[0] + box[2]) / 2,
        centre[1] - (box[1] + box[3]) / 2,
    )


def tile(size: int, destination: Path) -> None:
    """The full composition: teal tile, lighter band across the bottom, white glyph."""
    font_path = _font_path()
    n = size * SUPERSAMPLE
    image = Image.new("RGB", (n, n), TEAL)
    band = round(n * BAND_FRACTION)
    ImageDraw.Draw(image).rectangle([0, n - band, n, n], fill=BAND)

    font = ImageFont.truetype(font_path, _size_for_height(font_path, round(n * HEIGHT_FRACTION)))
    centre = (n / 2, n * CENTRE_FRACTION)
    dx, dy = _offset_to_centre((n, n), font, centre)
    ImageDraw.Draw(image).text(
        (centre[0] + dx, centre[1] + dy), GLYPH, font=font, fill=(255, 255, 255), anchor="mm"
    )

    image.resize((size, size), Image.LANCZOS).save(destination, optimize=True)


def outline(size: int, destination: Path) -> None:
    """The glyph alone, white on transparent, filling the tile.

    No margin, deliberately: "the icon mustn't have any extra padding around the symbol" is a
    *must fix*, and the app bar draws this at 32 pixels where a 4-pixel margin is an eighth of it.
    """
    font_path = _font_path()
    n = size * SUPERSAMPLE
    ink = Image.new("L", (n, n), 0)

    font = ImageFont.truetype(font_path, _size_for_height(font_path, n))
    centre = (n / 2, n / 2)
    dx, dy = _offset_to_centre((n, n), font, centre)
    ImageDraw.Draw(ink).text((centre[0] + dx, centre[1] + dy), GLYPH, font=font, fill=255, anchor="mm")

    # White everywhere, transparent where there is no ink - rather than a white glyph composited
    # onto transparency, which leaves grey fringes when the app bar draws it on a dark theme.
    image = Image.new("RGBA", (size, size), (255, 255, 255, 0))
    image.putalpha(ink.resize((size, size), Image.LANCZOS))
    image.save(destination, optimize=True)


if __name__ == "__main__":
    here = Path(__file__).parent
    tile(192, here / "teams" / "color.png")
    outline(32, here / "teams" / "outline.png")
    tile(215, here / "entra" / "app-logo.png")
    tile(300, here / "marketplace" / "icon.png")
    print("Rendered teams/color.png, teams/outline.png and entra/app-logo.png")
