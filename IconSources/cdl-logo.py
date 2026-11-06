# The icon of the mod's tab in the game's Roads menu (0.12.85): the CDL MOD badge of the listing logo (Properties/Thumbnail.png, the owner's own), cut out of its picture and packed as the
# other tab icons are packed: 128 px, a palette of 256 colours with transparency, into Icons/cdl-logo.png.
#
# python -I IconSources/cdl-logo.py <repo root>   (a Python with Pillow and numpy: Desktop/vision-tools/venv has them)
#
# The badge has a dark navy outline all round, so what is outside it is found by filling from the corners through everything that is not that navy; what the fill does not reach is the
# badge. The edge is eroded by a pixel and feathered so that no fringe of the photo behind the badge is left.
import sys
from collections import deque
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter

root = Path(sys.argv[1] if len(sys.argv) > 1 else ".").resolve()
source = Image.open(root / "Properties" / "Thumbnail.png").convert("RGBA")
# The badge is in the top left corner of the 1152 px picture; the box has some photo round it.
box = (14, 14, 292, 262)
crop = source.crop(box)
rgb = np.asarray(crop.convert("RGB")).astype(int)
r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
navy = (b - r >= 30) & (b - g >= 15) & (b < 170) & (r < 70)

height, width = navy.shape
outside = np.zeros_like(navy)
queue = deque()
for x in range(width):
    for y in (0, height - 1):
        if not navy[y, x]:
            outside[y, x] = True
            queue.append((y, x))
for y in range(height):
    for x in (0, width - 1):
        if not navy[y, x] and not outside[y, x]:
            outside[y, x] = True
            queue.append((y, x))
while queue:
    y, x = queue.popleft()
    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        ny, nx = y + dy, x + dx
        if 0 <= ny < height and 0 <= nx < width and not outside[ny, nx] and not navy[ny, nx]:
            outside[ny, nx] = True
            queue.append((ny, nx))

# Only the largest piece of what is left is the badge: a speck of the photo that looked like the outline is dropped.
inside = ~outside
label = np.zeros(inside.shape, dtype=int)
sizes = [0]
for sy in range(height):
    for sx in range(width):
        if inside[sy, sx] and label[sy, sx] == 0:
            sizes.append(0)
            label[sy, sx] = len(sizes) - 1
            queue.append((sy, sx))
            while queue:
                y, x = queue.popleft()
                sizes[-1] += 1
                for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    ny, nx = y + dy, x + dx
                    if 0 <= ny < height and 0 <= nx < width and inside[ny, nx] and label[ny, nx] == 0:
                        label[ny, nx] = label[y, x]
                        queue.append((ny, nx))
inside = label == int(np.argmax(sizes))

mask = Image.fromarray((inside * 255).astype(np.uint8), "L")
mask = mask.filter(ImageFilter.MinFilter(3)).filter(ImageFilter.GaussianBlur(0.8))
crop.putalpha(mask)

# The tight box of what is left, fitted into the square with a small margin.
bbox = crop.getchannel("A").point(lambda v: 255 if v > 40 else 0).getbbox()
badge = crop.crop(bbox)
size = 128
margin = 4
scale = (size - 2 * margin) / max(badge.size)
fitted = badge.resize((max(1, round(badge.width * scale)), max(1, round(badge.height * scale))), Image.LANCZOS)
canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
canvas.paste(fitted, ((size - fitted.width) // 2, (size - fitted.height) // 2), fitted)

out = root / "Icons" / "cdl-logo.png"
canvas.quantize(colors=256, method=Image.Quantize.FASTOCTREE, dither=Image.Dither.NONE).save(out, optimize=True)
print(f"{out} {out.stat().st_size} bytes, badge {badge.size} in the picture at {box[0] + bbox[0]},{box[1] + bbox[1]}")
