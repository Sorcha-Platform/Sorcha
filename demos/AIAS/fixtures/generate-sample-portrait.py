# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Generates demos/AIAS/fixtures/sample-portrait.jpg — a real, decodable JPEG used by
# demos/AIAS/rehearse.ps1 in place of a hand-written 14-byte SOI/EOI-only stub. The old
# stub had zero pixel data: it passed the agent's FieldPresentCheck (presence/non-empty
# only) but would render broken or blank on the M2 verdict screen (~70x88px portrait
# next to the credential level).
#
# This is a DELIBERATELY SYNTHETIC placeholder — a flat background with a simple
# geometric head-and-shoulders silhouette — not a photo and not photorealistic. It only
# needs to be a valid, visibly-rendering image, well under the server's F107 ~27,000
# base64-char size gate.
#
# Regenerate with: python demos/AIAS/fixtures/generate-sample-portrait.py
# Requires Pillow (verified against 8.4.0, the version available in this environment).

from PIL import Image, ImageDraw
import os

WIDTH, HEIGHT = 240, 320  # the portrait-token size the platform's client-side resizer targets
OUT_PATH = os.path.join(os.path.dirname(__file__), "sample-portrait.jpg")

BACKGROUND = (176, 196, 214)   # flat neutral blue-grey
SILHOUETTE = (74, 90, 108)     # contrasting darker tone for the head-and-shoulders shape

img = Image.new("RGB", (WIDTH, HEIGHT), BACKGROUND)
draw = ImageDraw.Draw(img)

# Head: a simple circle centered in the upper-middle of the frame.
head_cx, head_cy, head_r = WIDTH // 2, int(HEIGHT * 0.32), int(WIDTH * 0.22)
draw.ellipse(
    [head_cx - head_r, head_cy - head_r, head_cx + head_r, head_cy + head_r],
    fill=SILHOUETTE,
)

# Shoulders: a trapezoid rising from the bottom of the frame toward the neck.
shoulder_top_y = int(HEIGHT * 0.58)
shoulder_half_top = int(WIDTH * 0.14)
shoulder_half_bottom = int(WIDTH * 0.42)
draw.polygon(
    [
        (head_cx - shoulder_half_top, shoulder_top_y),
        (head_cx + shoulder_half_top, shoulder_top_y),
        (head_cx + shoulder_half_bottom, HEIGHT),
        (head_cx - shoulder_half_bottom, HEIGHT),
    ],
    fill=SILHOUETTE,
)

img.save(OUT_PATH, format="JPEG", quality=70, optimize=True)

size_bytes = os.path.getsize(OUT_PATH)
print(f"Wrote {OUT_PATH} ({size_bytes} bytes)")
