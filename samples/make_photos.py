"""Turns the flat sample labels into rough 'phone photo' versions: tilted, glare, dim and blurry.

Run after make_labels.py: python3 samples/make_photos.py
"""

from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

OUT = Path(__file__).parent


def on_table(label: Image.Image, quad: tuple[int, ...], angle: float) -> Image.Image:
    """Places the label on a dark background, seen at a slant and slightly rotated."""
    w, h = label.size
    # QUAD maps the corners (top-left, bottom-left, bottom-right, top-right) of a source area onto the output.
    slanted = label.transform((w, h), Image.Transform.QUAD, quad, Image.Resampling.BICUBIC, fillcolor="#3a2f28")
    canvas = Image.new("RGB", (w + 160, h + 160), "#3a2f28")
    canvas.paste(slanted, (80, 80))
    return canvas.rotate(angle, Image.Resampling.BICUBIC, fillcolor="#3a2f28")


def glare(img: Image.Image, box: tuple[int, int, int, int], strength: int) -> Image.Image:
    """Adds a soft white hot spot, like a flash reflecting off glass."""
    mask = Image.new("L", img.size, 0)
    ImageDraw.Draw(mask).ellipse(box, fill=strength)
    mask = mask.filter(ImageFilter.GaussianBlur(40))
    return Image.composite(Image.new("RGB", img.size, "white"), img, mask)


def dim(img: Image.Image) -> Image.Image:
    """Darkens the image from left to right and softens focus, like poor indoor light."""
    w, h = img.size
    shade = Image.linear_gradient("L").rotate(90).resize((w, h))
    dark = Image.blend(img, Image.new("RGB", img.size, "black"), 0.55)
    return Image.composite(img, dark, shade).filter(ImageFilter.GaussianBlur(1.4))


def save(img: Image.Image, name: str) -> None:
    img.save(OUT / f"{name}.jpg", quality=70)
    print("wrote", name + ".jpg")


if __name__ == "__main__":
    good = Image.open(OUT / "old-tom-good.png").convert("RGB")
    title_case = Image.open(OUT / "old-tom-title-case-warning.png").convert("RGB")
    w, h = good.size
    tilt = (-60, 0, 0, h, w, h + 40, w + 60, -40)

    save(on_table(good, tilt, 6), "photo-old-tom-tilted")
    save(glare(on_table(good, tilt, -4), (250, 180, 650, 520), 235), "photo-old-tom-glare-on-brand")
    save(glare(good, (40, 700, 520, 940), 250), "photo-old-tom-glare-on-warning")
    save(dim(on_table(good, tilt, 3)), "photo-old-tom-dim-blurry")
    save(dim(on_table(title_case, tilt, -5)), "photo-old-tom-title-case-dim-blurry")
