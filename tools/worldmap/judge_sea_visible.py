#!/usr/bin/env python3
"""海面可见性程序化判据（二分定位用）。

判据：world-pano.png 下半幅（y > 55% 高度，避开天空与地平线带）里
「蓝色占优」（B > R + 15 且 B > 110）的像素占比。海面在画 → 数千分之几起步；
海面缺席（露天空盒地面半，土黄）→ 接近 0。

用法：python judge_sea_visible.py <png 路径> [阈值=0.5%]
退出码 0 = 海面可见，1 = 海面缺席，2 = 用法错误。
"""
import sys
import zlib
import struct


def read_png_rgb(path):
    """极简 PNG 解码（8-bit RGB/RGBA 无隔行，够本项目出图用）。"""
    with open(path, "rb") as f:
        data = f.read()
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError("not a png")
    pos, width, height, bitd, color, idat = 8, 0, 0, 0, 0, b""
    while pos < len(data):
        length, ctype = struct.unpack(">I4s", data[pos:pos + 8])
        chunk = data[pos + 8:pos + 8 + length]
        if ctype == b"IHDR":
            width, height, bitd, color = struct.unpack(">IIBB", chunk[:10])
        elif ctype == b"IDAT":
            idat += chunk
        elif ctype == b"IEND":
            break
        pos += 12 + length
    if bitd != 8 or color not in (2, 6):
        raise ValueError(f"unsupported png bitdepth={bitd} colortype={color}")
    raw = zlib.decompress(idat)
    stride = width * (4 if color == 6 else 3)
    rows, prev = [], bytearray(stride)
    ofs = 0
    for _ in range(height):
        ft = raw[ofs]
        line = bytearray(raw[ofs + 1:ofs + 1 + stride])
        ofs += 1 + stride
        if ft == 1:
            for i in range(3, stride):
                line[i] = (line[i] + line[i - 3]) & 0xFF
        elif ft == 2:
            for i in range(stride):
                line[i] = (line[i] + prev[i]) & 0xFF
        elif ft == 3:
            for i in range(stride):
                left = line[i - 3] if i >= 3 else 0
                line[i] = (line[i] + ((left + prev[i]) >> 1)) & 0xFF
        elif ft == 4:
            for i in range(stride):
                a = line[i - 3] if i >= 3 else 0
                b = prev[i]
                c = prev[i - 3] if i >= 3 else 0
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                pr = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                line[i] = (line[i] + pr) & 0xFF
        rows.append(bytes(line))
        prev = line
    return width, height, rows, (4 if color == 6 else 3)


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    path = sys.argv[1]
    threshold = float(sys.argv[2]) if len(sys.argv) > 2 else 0.005
    width, height, rows, bpp = read_png_rgb(path)
    y0 = int(height * 0.55)
    blue = total = 0
    for y in range(y0, height, 4):          # 采样步长 4，速度足够
        row = rows[y]
        for x in range(0, width, 4):
            o = x * bpp
            r, g, b = row[o], row[o + 1], row[o + 2]
            total += 1
            if b > r + 15 and b > 110:
                blue += 1
    ratio = blue / max(total, 1)
    verdict = "VISIBLE" if ratio >= threshold else "ABSENT"
    print(f"{path}: lower-half bluish ratio = {ratio * 100:.2f}% -> sea {verdict}")
    return 0 if verdict == "VISIBLE" else 1


if __name__ == "__main__":
    sys.exit(main())
