#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把美术稿（showcase）上的文字用**游戏实际字体的像素字体**盖上。

【为什么有这一步】美术稿由 BeveledPixelSpriteBuilder 纯 CPU 像素合成——它能画件，
但带不了字体光栅化。文字要显示**游戏里真正会用的中文字体**（缝合像素 Fusion Pixel 12px，
OFL-1.1），所以拆成两步：
  ① 烘焙：出件 + 文字清单 `export/ui-pixel-4a/showcase-labels.json`（文本/左上锚/字号/颜色语义）；
  ② 本脚本：按清单用真字体盖章，**原地**写回 `showcase-1x.png`（1× = 实际屏幕像素）。

【字号口径】清单里的 size 是**艺术像素**；本脚本按 3× 渲染（`size * 3` 屏幕像素），
与 3× 口径一致（一个艺术像素 = 3 屏幕像素）。像素字体取 12 的整数倍才不糊。

【依赖】Pillow；字体 TTF 默认取 `external/font-ref/`（不入库，下载见
docs/images/ui-font/README.md）。用法：
    python tools/ui-review/stamp_showcase_text.py [字体TTF路径]
"""
import json
import os
import sys

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
# 需要盖章的（图, 文字清单）对：烘焙产出什么就盖什么
SHEETS = [
    (os.path.join(ROOT, "export", "ui-pixel-4a", "showcase-1x.png"),
     os.path.join(ROOT, "export", "ui-pixel-4a", "showcase-labels.json")),
    (os.path.join(ROOT, "export", "ui-pixel-4a", "components-1x.png"),
     os.path.join(ROOT, "export", "ui-pixel-4a", "components-labels.json")),
]
PALETTE = os.path.join(ROOT, "pirate-crew", "Assets", "Data", "Palette", "pirate_palette.json")
FONT_DEFAULT = os.path.join(ROOT, "external", "font-ref", "fusion-pixel-12px-proportional-zh_hans.ttf")
SCALE = 3          # 一个艺术像素 = 3 屏幕像素（与 PixelSkin.Unit 同口径）


def hex_to_rgb(h):
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def mix(a, b, t):
    return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(3))


def palette_roles():
    """从调色板真源取三种文字色（与生成器同源；derivation 与 C# 侧一致）。"""
    slots = {s["id"]: hex_to_rgb(s["hex"]) for s in json.load(open(PALETTE, encoding="utf-8"))["slots"]}
    ink = slots["INK"]
    white = slots["WHITE_HOT"]
    backdrop = mix(slots["SEA_DEEP"], ink, 0.45)   # 展示板底色（C# 侧同式）
    return {
        "white": white,
        "ink": ink,
        "dim": mix(white, backdrop, 0.45),
    }


def main():
    font_path = sys.argv[1] if len(sys.argv) > 1 else FONT_DEFAULT
    if not os.path.exists(font_path):
        raise SystemExit("找不到字体：%s（下载见 docs/images/ui-font/README.md）" % font_path)

    roles = palette_roles()
    stamped = 0
    for sheet, labels_path in SHEETS:
        if not os.path.exists(sheet) or not os.path.exists(labels_path):
            print("跳过（缺文件）：%s" % os.path.basename(sheet))
            continue
        image = Image.open(sheet).convert("RGB")
        draw = ImageDraw.Draw(image)
        data = json.load(open(labels_path, encoding="utf-8"))
        cache = {}
        for item in data["labels"]:
            size = int(item["size"]) * SCALE                 # 艺术像素 → 屏幕像素
            if size not in cache:
                cache[size] = ImageFont.truetype(font_path, size)
            color = roles.get(item.get("color", "white"), roles["white"])
            draw.text((int(item["x"]) * SCALE, int(item["y"]) * SCALE), item["text"],
                      font=cache[size], fill=color)
        image.save(sheet)
        stamped += 1
        print("已盖字 %d 条 → %s（%dx%d）"
              % (len(data["labels"]), os.path.basename(sheet), image.size[0], image.size[1]))
    if stamped == 0:
        raise SystemExit("没有可盖的图（先跑烘焙：BeveledPixelSpriteBuilder.BuildFromCommandLine）")


if __name__ == "__main__":
    main()
