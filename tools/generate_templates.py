"""Generate a local-only template cache from a supported installed Codex asset.

No original artwork, masks or generated reports are written into the repository.
"""
import argparse
import hashlib
import io
import json
import os
from pathlib import Path
import tempfile
import cv2
import numpy as np
from PIL import Image
from asar import find_archive, read_sprite

W, H = 192, 208
SCHEMA_VERSION = 1
GENERATOR_VERSION = "1.0.0"
# Fingerprints identify compatible local assets; no artwork or sampled pixels.
SUPPORTED_SHA256 = {"a816f7488c187ffe8b7f5d58319deb6cfa591f98c219ef06cbaf10d1f9f330db"}


def runs(mask):
    result = []
    for y in range(H):
        xs = np.flatnonzero(mask[y])
        if not len(xs):
            continue
        start = previous = int(xs[0])
        for x in map(int, xs[1:]):
            if x != previous + 1:
                result.append([y, start, previous - start + 1])
                start = x
            previous = x
        result.append([y, start, previous - start + 1])
    return result


def safe_rectangle(mask, red):
    # Keep the replacement glyphs inside the actual screen; the mask erases the
    # complete old glyphs separately, including on slanted frames.
    ys, xs = np.where(red)
    cx, cy = float(np.median(xs)), float(np.median(ys))
    safe = cv2.erode(mask.astype(np.uint8), np.ones((3, 3), np.uint8))
    integral = cv2.integral(safe)
    best = None
    for height in range(24, 13, -1):
        for width in range(60, 27, -1):
            for dy in (0, -2, 2, -4, 4, -6, 6):
                for dx in (0, -2, 2, -4, 4):
                    left, top = int(round(cx - width / 2 + dx)), int(round(cy - height / 2 + dy))
                    right, bottom = left + width, top + height
                    if left < 0 or top < 0 or right > W or bottom > H:
                        continue
                    total = integral[bottom, right] - integral[top, right] - integral[bottom, left] + integral[top, left]
                    if total != width * height:
                        continue
                    score = min(width / 2.3, height) * 100 - abs(dx) - abs(dy) + width * .02
                    if best is None or score > best[0]:
                        best = (score, [left, top, width, height])
    if best is None:
        raise ValueError("No safe readable text rectangle")
    return best[1]


def screen_mask(tile):
    r, g, b = [tile[:, :, i].astype(np.int16) for i in range(3)]
    a = tile[:, :, 3]
    yy = np.indices(a.shape)[0]
    face = (yy >= 60) & (yy < 150)
    red = (r > 80) & (r > g * 1.5) & (r > b * 1.5) & (a > 220) & face
    red_edges = (r > 25) & (r > g * 1.35) & (r > b * 1.35) & (a > 220) & face
    if not np.any(red):
        raise ValueError("No screen text found")
    dark = (r <= 32) & (g <= 24) & (b <= 32) & (r - g >= 2) & (b - g >= 1) & (a > 220)
    raw = (dark | red_edges).astype(np.uint8)
    opened = cv2.morphologyEx(raw, cv2.MORPH_OPEN, np.ones((3, 3), np.uint8))
    count, labels, stats, _ = cv2.connectedComponentsWithStats(opened, 8)
    if count <= 1:
        raise ValueError("No screen interior found")
    chosen = max(range(1, count), key=lambda i: int((red & (labels == i)).sum()))
    core = (labels == chosen).astype(np.uint8)
    red_y, red_x = np.where(red)
    center_x, center_y = float(np.median(red_x)), float(np.median(red_y))
    for component in range(1, count):
        sx, sy, sw, sh, area = stats[component]
        if area >= 20 and abs(sx + sw / 2 - center_x) <= 42 and abs(sy + sh / 2 - center_y) <= 33:
            core[labels == component] = 1
    x, y, width, height = cv2.boundingRect(cv2.findNonZero(red.astype(np.uint8)))
    old_text_box = np.zeros((H, W), np.uint8)
    old_text_box[y:y + height, x:x + width] = 1
    mask = core | red_edges.astype(np.uint8) | (raw & old_text_box)
    # Fill only enclosed holes, such as black letter outlines.
    outside = mask.copy()
    cv2.floodFill(outside, None, (0, 0), 1)
    mask |= 1 - outside
    assert np.all(mask[red]), "Original main red glyphs must all be erased"
    rect = safe_rectangle(mask, red)
    background_pixels = tile[:, :, :3][(mask > 0) & dark & ~red_edges]
    background = np.median(background_pixels, axis=0).astype(int).tolist() if len(background_pixels) else [17, 12, 17]
    return mask, red, rect, [x, y, width, height], background


def sample_points(tile, mask):
    rgb = tile[:, :, :3].astype(np.int16)
    opaque = cv2.erode((tile[:, :, 3] >= 250).astype(np.uint8), np.ones((3, 3), np.uint8)).astype(bool)
    screen_exclusion = cv2.dilate(mask.astype(np.uint8), np.ones((9, 9), np.uint8)).astype(bool)
    r, g, b = rgb[:, :, 0], rgb[:, :, 1], rgb[:, :, 2]
    red = (r > 30) & (r > g * 1.35) & (r > b * 1.35)
    local_max = cv2.dilate(rgb.astype(np.uint8), np.ones((3, 3), np.uint8)).astype(np.int16)
    local_min = cv2.erode(rgb.astype(np.uint8), np.ones((3, 3), np.uint8)).astype(np.int16)
    eligible = opaque & ~screen_exclusion & ~red & (rgb.max(2) >= 26) & ((local_max - local_min).max(2) <= 45)
    # Spatial stratification preserves pose details instead of mostly sampling the body.
    points = []
    for top in range(0, H, 13):
        for left in range(0, W, 12):
            ys, xs = np.where(eligible[top:top + 13, left:left + 12])
            if not len(xs):
                continue
            candidates = sorted(zip(xs + left, ys + top), key=lambda p: (-int(rgb[p[1], p[0]].max()), p[1], p[0]))
            selected = []
            for x, y in candidates:
                if all(abs(int(x) - px) + abs(int(y) - py) >= 5 for px, py in selected):
                    selected.append((int(x), int(y)))
                if len(selected) == 2:
                    break
            for x, y in selected:
                points.append([x, y] + list(map(int, rgb[y, x])))
    if len(points) > 250:
        indices = np.linspace(0, len(points) - 1, 250).astype(int)
        points = [points[i] for i in indices]
    assert len(points) >= 40
    return points


def build_frames(image):
    if image.shape != (2288, 1536, 4):
        raise ValueError("Unsupported sprite dimensions")
    frames = []
    for row in range(11):
        for col in range(8):
            tile = image[row * H:(row + 1) * H, col * W:(col + 1) * W]
            if int((tile[:, :, 3] > 200).sum()) < 100:
                continue
            supported = row < 8
            if supported:
                try:
                    mask, _, text, _, background = screen_mask(tile)
                except (ValueError, AssertionError):
                    supported = False
            if not supported:
                mask, text, background = np.zeros((H, W), np.uint8), [0, 0, 0, 0], [17, 12, 17]
            frames.append(dict(index=row * 8 + col, supported=supported,
                               samples=sample_points(tile, mask), mask=runs(mask),
                               textRectangle=text, background=background))
    if not frames:
        raise ValueError("Sprite contains no usable frames")
    return frames


def make_document(content):
    fingerprint = hashlib.sha256(content).hexdigest()
    if fingerprint not in SUPPORTED_SHA256:
        raise ValueError("Unsupported Null Signal sprite fingerprint; a compatibility update is needed")
    with Image.open(io.BytesIO(content)) as source:
        if source.size != (1536, 2288):
            raise ValueError("Unsupported sprite dimensions")
        image = np.array(source.convert("RGBA"))
    frames = build_frames(image)
    if len(frames) != 74 or sum(frame["supported"] for frame in frames) != 46:
        raise ValueError("Template validation failed: unexpected supported frame count")
    return dict(schemaVersion=SCHEMA_VERSION, generatorVersion=GENERATOR_VERSION,
                spriteSha256=fingerprint, frameWidth=W, frameHeight=H, frames=frames)


def write_cache(document):
    local = os.environ.get("LOCALAPPDATA")
    if not local:
        raise ValueError("LOCALAPPDATA is required on Windows")
    directory = Path(local) / "CodexQuotaPetData" / "cache"
    directory.mkdir(parents=True, exist_ok=True)
    destination = directory / ("templates-" + document["spriteSha256"] + ".json")
    # Atomic replacement prevents the running reader from seeing a partial file.
    fd, temporary = tempfile.mkstemp(prefix="templates-", suffix=".tmp", dir=directory)
    try:
        with os.fdopen(fd, "w", encoding="utf-8", newline="\n") as stream:
            json.dump(document, stream, separators=(",", ":"), ensure_ascii=True)
            stream.write("\n")
        os.replace(temporary, destination)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)
    return destination


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--codex-path", required=True, help="Desktop install directory, executable or app.asar")
    args = parser.parse_args()
    try:
        _, content = read_sprite(find_archive(args.codex_path))
        document = make_document(content)
        destination = write_cache(document)
        print("Local templates ready: %d frames, %d supported; %s" %
              (len(document["frames"]), sum(frame["supported"] for frame in document["frames"]), destination))
    except (ValueError, OSError, KeyError, TypeError) as error:
        parser.exit(1, "Template preparation failed: %s\n" % error)


if __name__ == "__main__":
    main()
