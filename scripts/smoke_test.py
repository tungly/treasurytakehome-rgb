"""Runs every sample label through a running Label Check app and compares the overall result with the expected one.

Usage:
    python3 scripts/smoke_test.py                                          # local app from `dotnet run`
    python3 scripts/smoke_test.py https://labelverify-16883.azurewebsites.net

Exits with status 1 if any result differs. Needs Python 3 and curl. Each label costs one Azure OpenAI call.
"""

import csv
import re
import subprocess
import sys
import tempfile
from pathlib import Path

SAMPLES = Path(__file__).resolve().parent.parent / "samples"
TARGET_SECONDS = 5.0

# Overall result each sample should get. "judgment" marks samples whose result rests on the model's
# judgment of bold, readability, or type size, which can flip now and then (see README limitations).
EXPECTED = {
    "old-tom-good.png": ("Match", ""),
    "old-tom-title-case-warning.png": ("Mismatch", ""),
    "stones-throw-wrong-abv.png": ("Mismatch", ""),
    "highland-import-bold-body.png": ("NeedsReview", "judgment"),
    "photo-old-tom-tilted.jpg": ("Match", ""),
    "photo-old-tom-glare-on-brand.jpg": ("Match", ""),
    "photo-old-tom-glare-on-warning.jpg": ("NeedsReview", "judgment"),
    "photo-old-tom-dim-blurry.jpg": ("Match", ""),
    "photo-old-tom-title-case-dim-blurry.jpg": ("Mismatch", ""),
    "old-tom-tiny-warning.png": ("NeedsReview", "judgment"),
    "old-tom-front.png|old-tom-back.png": ("Match", ""),
}


def curl(*args: str) -> str:
    return subprocess.run(["curl", "-s", "--max-time", "60", *args], capture_output=True, text=True).stdout


def check(url: str, jar: str, token: str, row: dict) -> tuple[str, float]:
    """Posts one label through the single-label page and returns its overall result and reading time."""
    images = []
    for name in row["file"].split("|"):
        kind = "image/jpeg" if name.endswith(".jpg") else "image/png"
        images += ["-F", f"LabelImages=@{SAMPLES / name};type={kind}"]
    fields = {"BrandName": "brand_name", "ClassType": "class_type", "AlcoholContent": "alcohol_content",
              "NetContents": "net_contents", "Bottler": "bottler", "CountryOfOrigin": "country_of_origin"}
    form = [arg for key, column in fields.items() for arg in ("-F", f"App.{key}={row[column]}")]
    page = curl("-b", jar, "-X", "POST", url, "-F", f"__RequestVerificationToken={token}", *images, *form)

    verdict = re.search(r'class="banner (\w+)"', page)
    error = re.search(r'class="error"[^>]*>([^<]+)', page)
    seconds = re.search(r"label took ([\d.]+) seconds", page)
    if verdict is None:
        return f"Error: {error.group(1).strip() if error else 'no result on page'}", 0.0
    return verdict.group(1), float(seconds.group(1)) if seconds else 0.0


def main() -> int:
    url = (sys.argv[1] if len(sys.argv) > 1 else "http://localhost:5262").rstrip("/") + "/"
    with tempfile.NamedTemporaryFile(suffix=".txt") as jar:
        token = re.search(r'name="__RequestVerificationToken" type="hidden" value="([^"]+)"', curl("-c", jar.name, url))
        if token is None:
            print(f"Could not load {url}. Is the app running?")
            return 1

        failures, slowest = 0, 0.0
        print(f"Checking samples against {url}\n")
        for row in csv.DictReader(open(SAMPLES / "applications.csv", newline="")):
            expected, note = EXPECTED[row["file"]]
            got, seconds = check(url, jar.name, token.group(1), row)
            slowest = max(slowest, seconds)
            ok = got == expected
            failures += not ok
            print(f"{'PASS' if ok else 'FAIL'}  {row['file']:42} expected {expected:11} got {got:11} {seconds:4.1f}s  {note}")

    print(f"\n{failures} of {len(EXPECTED)} differ. Slowest reading: {slowest:.1f}s"
          + (f" (over the {TARGET_SECONDS:.0f}-second target)" if slowest > TARGET_SECONDS else "."))
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
