"""Draws simple test label images. Run: python3 samples/make_labels.py (needs Pillow and macOS Arial fonts)."""

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

FONTS = Path("/System/Library/Fonts/Supplemental")
OUT = Path(__file__).parent
WARNING_BODY = (
    "(1) According to the Surgeon General, women should not drink alcoholic beverages during pregnancy "
    "because of the risk of birth defects. (2) Consumption of alcoholic beverages impairs your ability to "
    "drive a car or operate machinery, and may cause health problems."
)

LABELS = {
    "old-tom-good": dict(
        brand="OLD TOM DISTILLERY", kind="Kentucky Straight Bourbon Whiskey", abv="45% Alc./Vol. (90 Proof)",
        net="750 mL", bottler="Bottled by Old Tom Distillery, Louisville, Kentucky",
        header="GOVERNMENT WARNING:", header_bold=True, body_bold=False),
    "old-tom-title-case-warning": dict(
        brand="OLD TOM DISTILLERY", kind="Kentucky Straight Bourbon Whiskey", abv="45% Alc./Vol. (90 Proof)",
        net="750 mL", bottler="Bottled by Old Tom Distillery, Louisville, Kentucky",
        header="Government Warning:", header_bold=True, body_bold=False),
    "stones-throw-wrong-abv": dict(
        brand="STONE'S THROW", kind="London Dry Gin", abv="40% Alc./Vol. (80 Proof)",
        net="1 L", bottler="Distilled and bottled by Stone's Throw Spirits, Portland, Oregon",
        header="GOVERNMENT WARNING:", header_bold=True, body_bold=False),
    "highland-import-bold-body": dict(
        brand="GLEN ARDEN", kind="Single Malt Scotch Whisky", abv="43% Alc./Vol.",
        net="700 mL", bottler="Imported by Arden Imports, New York, NY", origin="Product of Scotland",
        header="GOVERNMENT WARNING:", header_bold=True, body_bold=True),
    "old-tom-tiny-warning": dict(
        brand="OLD TOM DISTILLERY", kind="Kentucky Straight Bourbon Whiskey", abv="45% Alc./Vol. (90 Proof)",
        net="750 mL", bottler="Bottled by Old Tom Distillery, Louisville, Kentucky",
        header="GOVERNMENT WARNING:", header_bold=True, body_bold=False, warning_size=8),
}


def font(bold: bool, size: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(str(FONTS / ("Arial Bold.ttf" if bold else "Arial.ttf")), size)


def centered(draw: ImageDraw.ImageDraw, y: int, text: str, f: ImageFont.FreeTypeFont, width: int) -> int:
    w = draw.textlength(text, font=f)
    draw.text(((width - w) / 2, y), text, font=f, fill="#2b1d0e")
    return y + f.size + 18


def warning(draw: ImageDraw.ImageDraw, y: int, spec: dict, left: int, right: int) -> None:
    """Wraps the warning word by word so the header and body can use different weights."""
    words = [(w, spec["header_bold"]) for w in spec["header"].split()]
    words += [(w, spec["body_bold"]) for w in WARNING_BODY.split()]
    x, size = left, spec.get("warning_size", 19)
    for word, bold in words:
        f = font(bold, size)
        w = draw.textlength(word + " ", font=f)
        if x + w > right:
            x, y = left, y + size + max(2, size // 3)
        draw.text((x, y), word, font=f, fill="#2b1d0e")
        x += w


def draw_label(name: str, spec: dict) -> None:
    width, height = 800, 1050
    img = Image.new("RGB", (width, height), "#f7f0e1")
    draw = ImageDraw.Draw(img)
    draw.rectangle([20, 20, width - 20, height - 20], outline="#2b1d0e", width=4)
    y = centered(draw, 110, spec["brand"], font(True, 56), width)
    y = centered(draw, y + 20, spec["kind"], font(False, 34), width)
    y = centered(draw, y + 60, spec["abv"], font(True, 30), width)
    y = centered(draw, y, spec["net"], font(False, 30), width)
    if "origin" in spec:
        y = centered(draw, y, spec["origin"], font(False, 26), width)
    centered(draw, y + 60, spec["bottler"], font(False, 22), width)
    warning(draw, 760, spec, 60, width - 60)
    img.save(OUT / f"{name}.png")


def draw_front_back(name: str, spec: dict) -> None:
    """Splits one label across a front panel and a back panel, like most real bottles."""
    width, height = 800, 650
    for side in ("front", "back"):
        img = Image.new("RGB", (width, height), "#f7f0e1")
        draw = ImageDraw.Draw(img)
        draw.rectangle([20, 20, width - 20, height - 20], outline="#2b1d0e", width=4)
        if side == "front":
            y = centered(draw, 90, spec["brand"], font(True, 56), width)
            y = centered(draw, y + 20, spec["kind"], font(False, 34), width)
            y = centered(draw, y + 60, spec["abv"], font(True, 30), width)
            centered(draw, y, spec["net"], font(False, 30), width)
        else:
            centered(draw, 90, spec["bottler"], font(False, 22), width)
            warning(draw, 300, spec, 60, width - 60)
        img.save(OUT / f"{name}-{side}.png")
        print("wrote", f"{name}-{side}.png")


if __name__ == "__main__":
    for label_name, label_spec in LABELS.items():
        draw_label(label_name, label_spec)
        print("wrote", label_name + ".png")
    draw_front_back("old-tom", LABELS["old-tom-good"])
