#!/usr/bin/env python3
from __future__ import annotations

import math
from pathlib import Path
import struct
import sys
import zlib


ICON_FILES = {
    "icon_16x16.png": 16,
    "icon_16x16@2x.png": 32,
    "icon_32x32.png": 32,
    "icon_32x32@2x.png": 64,
    "icon_128x128.png": 128,
    "icon_128x128@2x.png": 256,
    "icon_256x256.png": 256,
    "icon_256x256@2x.png": 512,
    "icon_512x512.png": 512,
    "icon_512x512@2x.png": 1024,
}

BG = (237, 247, 246, 255)
WHITE = (255, 255, 255, 255)
GRID = (255, 255, 255, 150)
GREEN = (8, 127, 91, 255)
MINT = (85, 199, 163, 230)
BORDER = (214, 228, 230, 255)
SHADOW = (22, 33, 38, 42)


def clamp(value: float, low: float = 0.0, high: float = 1.0) -> float:
    return max(low, min(high, value))


def blend(dst: tuple[int, int, int, int], src: tuple[int, int, int, int], alpha_scale: float = 1.0) -> tuple[int, int, int, int]:
    src_alpha = clamp((src[3] / 255.0) * alpha_scale)
    dst_alpha = dst[3] / 255.0
    out_alpha = src_alpha + dst_alpha * (1.0 - src_alpha)
    if out_alpha <= 0:
        return (0, 0, 0, 0)
    channels = []
    for index in range(3):
        value = (src[index] * src_alpha + dst[index] * dst_alpha * (1.0 - src_alpha)) / out_alpha
        channels.append(round(value))
    channels.append(round(out_alpha * 255))
    return tuple(channels)


def round_rect_sdf(x: float, y: float, cx: float, cy: float, width: float, height: float, radius: float) -> float:
    px = abs(x - cx) - width / 2 + radius
    py = abs(y - cy) - height / 2 + radius
    outside = math.hypot(max(px, 0.0), max(py, 0.0))
    inside = min(max(px, py), 0.0)
    return outside + inside - radius


def line_alpha(distance: float, width: float, aa: float) -> float:
    return clamp((width - distance) / aa)


def segment_distance(px: float, py: float, ax: float, ay: float, bx: float, by: float) -> float:
    dx = bx - ax
    dy = by - ay
    length_sq = dx * dx + dy * dy
    if length_sq == 0:
        return math.hypot(px - ax, py - ay)
    t = clamp(((px - ax) * dx + (py - ay) * dy) / length_sq)
    return math.hypot(px - (ax + t * dx), py - (ay + t * dy))


def draw_r_alpha(x: float, y: float, aa: float) -> float:
    shapes = [
        round_rect_sdf(x, y, 0.36, 0.53, 0.15, 0.50, 0.03),
        round_rect_sdf(x, y, 0.49, 0.34, 0.31, 0.12, 0.03),
        round_rect_sdf(x, y, 0.62, 0.44, 0.12, 0.23, 0.03),
        round_rect_sdf(x, y, 0.50, 0.55, 0.29, 0.11, 0.03),
        segment_distance(x, y, 0.49, 0.56, 0.68, 0.77) - 0.055,
    ]
    return max(clamp(0.5 - shape / aa) for shape in shapes)


def render(size: int) -> list[tuple[int, int, int, int]]:
    pixels: list[tuple[int, int, int, int]] = []
    aa = 1.5 / size
    for py in range(size):
        for px in range(size):
            x = (px + 0.5) / size
            y = (py + 0.5) / size
            color = (0, 0, 0, 0)

            shadow_dist = round_rect_sdf(x, y - 0.018, 0.5, 0.5, 0.86, 0.86, 0.18)
            color = blend(color, SHADOW, clamp(0.5 - shadow_dist / (aa * 2.5)) * 0.6)

            icon_dist = round_rect_sdf(x, y, 0.5, 0.5, 0.86, 0.86, 0.18)
            icon_alpha = clamp(0.5 - icon_dist / aa)
            color = blend(color, BG, icon_alpha)

            if icon_alpha > 0:
                for radius in (0.11, 0.22, 0.33):
                    grid_alpha = line_alpha(abs(math.hypot(x - 0.5, y - 0.47) - radius), 0.0045, aa) * icon_alpha
                    color = blend(color, GRID, grid_alpha)
                for ax, ay, bx, by in (
                    (0.18, 0.47, 0.82, 0.47),
                    (0.50, 0.15, 0.50, 0.80),
                    (0.25, 0.22, 0.74, 0.71),
                    (0.74, 0.22, 0.25, 0.71),
                ):
                    grid_alpha = line_alpha(segment_distance(x, y, ax, ay, bx, by), 0.0045, aa) * icon_alpha
                    color = blend(color, GRID, grid_alpha)

                r_alpha = draw_r_alpha(x, y, aa) * icon_alpha
                color = blend(color, GREEN, r_alpha)
                stem_alpha = clamp(0.5 - round_rect_sdf(x, y, 0.33, 0.53, 0.10, 0.50, 0.02) / aa) * icon_alpha
                color = blend(color, MINT, stem_alpha)

                inner_border = line_alpha(abs(icon_dist), 0.006, aa) * icon_alpha
                color = blend(color, WHITE, inner_border)
                outline = line_alpha(abs(icon_dist + 0.008), 0.0025, aa) * icon_alpha
                color = blend(color, BORDER, outline)

            pixels.append(color)
    return pixels


def write_png(path: Path, size: int, pixels: list[tuple[int, int, int, int]]) -> None:
    def chunk(kind: bytes, data: bytes) -> bytes:
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    rows = []
    for y in range(size):
        start = y * size
        row = bytearray([0])
        for rgba in pixels[start : start + size]:
            row.extend(rgba)
        rows.append(bytes(row))
    raw = b"".join(rows)
    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(raw, 9))
    png += chunk(b"IEND", b"")
    path.write_bytes(png)


def main() -> int:
    if len(sys.argv) != 2:
        print("usage: generate_app_icon.py /path/to/RedLanSync.iconset", file=sys.stderr)
        return 2
    iconset = Path(sys.argv[1])
    iconset.mkdir(parents=True, exist_ok=True)
    cache: dict[int, list[tuple[int, int, int, int]]] = {}
    for filename, size in ICON_FILES.items():
        cache.setdefault(size, render(size))
        write_png(iconset / filename, size, cache[size])
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
