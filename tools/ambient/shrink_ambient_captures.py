#!/usr/bin/env python3
"""把天空盒三档实拍的原始 PNG 压成 1280 宽 JPEG 入库（同 art-review / worldmap-captures-r2 惯例）。

用法:
    python tools/ambient/shrink_ambient_captures.py export/ambient-skybox-debug

原始 PNG 留在原地（.gitignore 已排除 export/ambient-skybox-debug/*/*.png），
JPEG 与 PNG 同名同目录，重复执行安全（已存在且不比 PNG 旧则跳过）。
"""
import glob
import os
import sys

from PIL import Image

MAX_WIDTH = 1280
QUALITY = 88


def main():
    root = sys.argv[1] if len(sys.argv) > 1 else "export/ambient-skybox-debug"
    made = 0
    for path in sorted(glob.glob(os.path.join(root, "*", "*.png"))):
        jpg = os.path.splitext(path)[0] + ".jpg"
        if os.path.exists(jpg) and os.path.getmtime(jpg) >= os.path.getmtime(path):
            continue

        img = Image.open(path).convert("RGB")
        if img.width > MAX_WIDTH:
            height = round(img.height * MAX_WIDTH / img.width)
            img = img.resize((MAX_WIDTH, height), Image.LANCZOS)

        img.save(jpg, "JPEG", quality=QUALITY, optimize=True)
        made += 1
        print(f"{jpg}  {os.path.getsize(jpg) // 1024} KB")

    print(f"共转换 {made} 张。")


if __name__ == "__main__":
    main()
