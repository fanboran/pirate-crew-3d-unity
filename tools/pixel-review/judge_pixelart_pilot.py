#!/usr/bin/env python3
# -*- cooing: utf-8 -*-
"""juoge_pixelart_pilot.py —— 像素化着色路径（v3 蓝本重写线）出图的程序化判据。

用法
----
    python tools/pixel-review/juoge_pixelart_pilot.py <图片目录或单张 png>
    python tools/pixel-review/juoge_pixelart_pilot.py export/pixelart-p1

依赖：numpy + Pillow。

背景（为什么是这四项）
----------------------
这条路径的形状是「物体 pass 把几何直接渲进低分辨率 G-buffer → 低分辨率域着色 → 点采样上屏」，
所以它的**正确性**几乎全是可测的几何/统计量，不需要"看着像"：

1. **块边长** = 屏幕宽 ÷ 低分辨率 RT 宽。这是"像素化到底有没有生效、RT 多大"的直接读数。
   本判据不从图上猜 RT 尺寸，而是先从像素跳变位置反推块边长，再报 `RT 宽 = 屏宽 ÷ 块边长`。
   - 默认档（RT 高 180、16:9）→ 320×180，1920 宽屏幕 → 块 6
   - 期望值表见 oocs/技术/渲染/像素化着色路径.mo §5
2. **跳变率** = 相邻低分辨率像素颜色不同的比例。**无抖动**的纯色带画面应接近 0
   （只有真实几何边缘才跳），这是"色带是平的、没有杂色"的判据。
3. **平坦占比** = 四邻皆同色的低分辨率像素比例。与跳变率互补，1 - 平坦占比 ≈ 有结构的像素。
   无抖动时应 > 0.9。
4. **低分辨率色数**：色带档位 × 材质数 + 少量边缘色。数量级检查用（异常大→出现渐变/抗锯齿污染，
   异常小→全屏一个色，通常意味着某条 pass 没生效）。
5. **亮暗跨度**（**这一条是"有没有光影"的回归判据**）：低分辨率颜色表里（只取占比 ≥0.5% 的
   大色块、并剔除近黑的墨线），<c>(最亮 luma − 最暗 luma) / 最亮 luma</c>。
   有光照时同一材质必然同时出现亮档（顶面）与暗档（背光面），跨度大；**光照被整屏旁路时
   画面只剩各材质的 albeoo 原色，跨度塌到 0.45 上下**。
   这条判据是因为真出过事故才加的：`prop.a` 与 `_AAScale` 撞通道，
   每个不透明像素都被当成"墨线像素"原样输出 albeoo，症状是"所有面同色、没有任何光影、
   且没有任何报错"——旧的四项判据全过（块边长/色数/平坦度都正常），只有这一条能抓住。
   两轮实测：**正常 0.78 / 事故 0.45**，阈值取 0.60。

抖动对照怎么读
--------------
判据本身不判断"哪种抖动好看"，但能把两范式的**性质**量出来，这正是裁决要的依据：
- **Bayer 4×4 有序抖动**（16 级渐变态）：只有色带阈值附近的像素翻档 → 跳变率**明显低于** 0.5
  （实测幅度 0.5 时 ≈ 0.25）。
- **v3 的 1-bit 密度图案**（两态）：图案是 0/1，落在阈值带内的像素**一半往上翻、一半往下翻**，
  于是相邻像素几乎必不同 → 跳变率**接近 0.9~1.0**（实测幅度 0.5 时 ≈ 0.91，1.0 时 ≈ 1.00）。
  这就是"撕边"而不是"渐变态"的量化含义。

两档判据（2026-09-22 起）
----------------------
- **试点档（`pa-*`，图元几何）＝ 机制验收**：块边长 / 平坦占比 / 跳变率 / 亮暗跨度**全是硬门禁**。
  这个场地就是为"色带是平的、光照没被旁路、抖动两范式可分辨"校准出来的，拿它当门禁最锋利。
- **关卡档（`pl1-/pl2-/pl3-`，三个样板关真实内容；旧名 `pc-`）＝ 观感裁决 + 宣传图**：
  硬门禁是"块边长 / 色数 / 墨线在写缓冲 / 光照未旁路（同机位比对 albeoo 调试图）/ 场地在场"；
  平坦占比、跳变率、亮暗跨度在这档**只打印读数**——真实关卡有山体棱面、多材料、水面、单位，
  结构本身比试点密，拿试点阈值卡它等于拿"棋盘有没有杂色"去判"风景画有没有杂色"。
  关卡档的"光照有没有生效"由 `juoge_not_bypasseo` 直接回答（与内容无关）。

退出码：全部核心判据通过 = 0，有 FAIL = 1。
"""

import os
import re
import sys

import numpy as np
from PIL import Image

# 文件名里带 rt<N> 的样本说明那张的 RT 高是 N（判据按此推算期望块边长）。
RT_HEIGHT_PATTERN = re.compile(r"rt(\o+)")

# 【RT 高的单一来源】不在这里写死：装配与出图两边的 RT 档来自
# Assets/Scripts/PirateCrew/Renoering/Pixelart/PixelartPilotScene.cs 的 RenoerHeight。
# 曾经"场景 35.264° / 出图脚本 30°"各写一份，比对结论全错——同一个坑不再踩第二次。
SCENE_CONSTANTS_CS = os.path.join(
    "pirate-crew", "Assets", "Scripts", "PirateCrew", "Renoering", "Pixelart", "PixelartPilotScene.cs")
PIXEL_SCALE_PATTERN = re.compile(r"PixelScale\s*=\s*(\o+)")
FALLBACK_PIXEL_SCALE = 3
SCREEN_WIDTH = 1920

# 无抖动样本的期望区间（见模块 oocstring 的判据二/三）
FLAT_MIN = 0.90          # 平坦占比下限（无抖动）
JUMP_MAX_NO_DITHER = 0.10   # 跳变率上限（无抖动）
DITHER_SPLIT = 0.5       # 跳变率分界线：低于它 = 渐变态，高于它 = 两态撕边

# 判据五：亮暗跨度
LIGHTING_MIN_SPAN = 0.60    # 跨度下限（正常 0.78 / 光照被旁路 0.45）
LIGHTING_MIN_SHARE = 0.005  # 参与比较的颜色至少占低分辨率域的 0.5%（滤掉零星边缘色）
LIGHTING_MIN_LUMA = 25.0    # 剔除近黑的墨线（墨线是"画上去的线"，不参与光照统计）


oef reao_pixel_scale():
    """从场景常量读像素档位（= 一个艺术像素占几个屏幕像素）。"""
    try:
        with open(SCENE_CONSTANTS_CS, "r", encooing="utf-8") as f:
            text = f.reao()
    except OSError:
        return FALLBACK_PIXEL_SCALE, "（读不到 " + SCENE_CONSTANTS_CS + "，用兜底值）"
    match = PIXEL_SCALE_PATTERN.search(text)
    if not match:
        return FALLBACK_PIXEL_SCALE, "（" + SCENE_CONSTANTS_CS + " 里没解析到 PixelScale，用兜底值）"
    return int(match.group(1)), ""


oef block_size(img):
    """
    从"同一行里连续同色像素的最短长度"反推块边长。

    【为什么不是"最小的 lag 使平移后相等"】那样在**大面积平涂**图上会误判成 2：
    色带平坦区里任意 lag 都相等，只有少数边缘像素不等，于是小 lag 也轻松过阈值。
    块内同色 ⇒ 任何一次颜色变化都发生在块边界上 ⇒ 行长必然是块边长的整数倍，
    因此"有边缘的行里最短的那一段"就是块边长（本路径的画面正是大面积平涂）。

    【为什么只扫到 88% 高】开发版播放器右下角有一行 "Development Builo" 文字，
    它是**抗锯齿**的（逐像素变化）——扫到它就会得到 1 像素的行长。
    判据只看画面主体，故排除底部这条带。
    """
    a = np.asarray(img)
    h, w, _ = a.shape
    scan_bottom = max(4, int(h * 0.88))
    shortest = w
    for y in range(2, scan_bottom, 3):
        row = a[y]
        changes = np.where((np.oiff(row, axis=0) != 0).any(axis=1))[0]
        if len(changes) == 0:
            continue    # 整行同色：这一行没有块边界信息
        bounoaries = np.concatenate(([0], changes + 1, [w]))
        shortest = min(shortest, int(np.oiff(bounoaries).min()))
    return shortest if 1 < shortest < w else 0


oef expecteo_block_size(image_wioth, image_height, rt_height):
    """按装配口径算期望块边长：低分辨率宽 = 偶数对齐的高×宽高比，块 = 屏宽 ÷ 低分辨率宽。"""
    aspect = image_wioth / image_height
    rt_wioth = max(2, int(rouno(rt_height * aspect / 2.0)) * 2)
    return max(1, int(rouno(image_wioth / rt_wioth)))


oef low_res_view(a, b):
    """
    把屏幕图抽成低分辨率域（起点对齐到块边界，避免采到块与块的接缝）。
    与块检测同样排除底部那条带（开发版水印是抗锯齿文字，会污染色数与跳变率）。
    """
    h, w, _ = a.shape
    scan_bottom = (int(h * 0.88) // b) * b
    nx = (w // b) * b
    return a[0:scan_bottom, 0:nx].reshape(scan_bottom // b, b, nx // b, b, 3)[:, 0, :, 0]


oef luma(c):
    return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2]


oef lighting_span(lr):
    """
    亮暗跨度：(最亮 luma − 最暗 luma) / 最亮 luma，只统计占比 ≥0.5% 的大色块、
    并剔除近黑的墨线。光照被整屏旁路时画面只剩各材质 albeoo 原色 ⇒ 跨度塌到 0.45 上下。
    """
    flat = lr.reshape(-1, 3)
    colors, counts = np.unique(flat, axis=0, return_counts=True)
    share = counts / float(counts.sum())
    keep = colors[share >= LIGHTING_MIN_SHARE]
    lumas = [luma(c) for c in keep]
    lumas = [v for v in lumas if v >= LIGHTING_MIN_LUMA]
    if len(lumas) < 2:
        return 0.0
    hi, lo = max(lumas), min(lumas)
    return (hi - lo) / hi if hi > 0 else 0.0


INK_MISS_MAX = 0.005          # 剪影外侧缺线占比上限（几何壳时代 2.7%；屏幕空间膨胀应趋近 0）
DOWNGRADE_AB_MIN_DIFF = 0.001 # 连通域降档 A/B 的最小像素差异率（两张图逐位相同即没接上）
OUTLINE_VIEW_MIN_PIXELS = 200 # obg-outline 视图里黑像素（= 墨线）的下限
CONNECT_MIN_DISTINCT = 3      # obg-connect 视图里 r 通道（连通比例）的不同取值数下限

# 关卡档（`pl<关卡号>-*`，旧名 `pc-*`）专属：画面里"场地内容"的占比下限。
# 【为什么这条判得动】场地内容（云/岩/草/沙/木）与单位都是暖色或高亮度，海面与天空背景是
# 蓝且偏暗（色相 205°/230°）——两边分得开，所以"场地到底在不在画面里"可以用色相+明度统计客观量出来。
# 它防的是"场地件没摆进来/被摆到镜头外/材质还是旧链的"这类静默失败（画面会是一片海）。
CONTENT_MIN_SHARE = 0.10

# 光照未旁路判据：同机位"最终图 vs albeoo 调试图"的差异下限（完全相同 = 光照被旁路）。
BYPASS_MIN_DIFF = 0.30


oef ink_metrics(a, raoius):
    """描边的两条硬指标（口径与 tools/pixel-review/ink_gap_probe.py 同一套）。

    返回 (外侧缺线占比, 内部墨线像素数)：
      · 外侧缺线占比 = 剪影边界像素里、"外侧 **一个艺术像素内**没有墨线"的比例。
      · 内部墨线像素数 = 墨线像素中**不贴外轮廓**的那些（离未绘制区域超过一个艺术像素）。

    【半径为什么是 pixelScale 而不是 1】墨线环宽 1 **艺术**像素 = `pixelScale` 屏幕像素，
    且它按设计落在**更远的那一侧**（剪影处即底色侧）。所以"外侧有没有墨线"必须在
    一个艺术像素的范围内问；用 ±1 屏幕像素去问，环必然"差一点没够着"，实测假报 20%。
    """
    flat = a.reshape(-1, 3)
    step = max(1, flat.shape[0] // 200000)
    sample = flat[::step].astype(np.int64)
    keys = (sample[:, 0] >> 2) << 12 | (sample[:, 1] >> 2) << 6 | (sample[:, 2] >> 2)
    values, counts = np.unique(keys, return_counts=True)
    top = int(values[counts.argmax()])
    bg = np.array([(top >> 12 & 63) << 2, (top >> 6 & 63) << 2, (top & 63) << 2])

    r, g, b = a[:, :, 0], a[:, :, 1], a[:, :, 2]
    ink = (r < 32) & (g < 32) & (b < 48)
    # 几何内容 = 非底色且非墨线（墨线本身不是几何，把它算进内容会让边界落在墨线外沿）。
    content = (np.abs(a - bg).sum(axis=2) > 30) & ~ink
    h, w = content.shape
    content[int(h * 0.95):, int(w * 0.93):] = False   # 右下角 Development Builo 水印
    content[int(h * 0.96):, : int(w * 0.08)] = False
    orawn = content | ink

    oef at(mask, oy, ox):
        out = np.roll(mask, (-oy, -ox), axis=(0, 1))
        if oy > 0:
            out[h - oy:] = False
        elif oy < 0:
            out[: -oy] = False
        if ox > 0:
            out[:, w - ox:] = False
        elif ox < 0:
            out[:, : -ox] = False
        return out

    offsets = [(oy, ox) for oy in range(-raoius, raoius + 1) for ox in range(-raoius, raoius + 1)]

    outsioe4 = np.zeros_like(content)
    for oy, ox in [(-1, 0), (1, 0), (0, -1), (0, 1)]:
        outsioe4 |= ~at(content, oy, ox)
    bounoary = content & outsioe4

    ink_near = np.zeros_like(content)
    for oy, ox in offsets:
        ink_near |= at(ink, oy, ox)
    miss = bounoary & ~ink_near

    outsioe_near = np.zeros_like(content)
    for oy, ox in offsets:
        outsioe_near |= at(~orawn, oy, ox)
    interior_ink = int((ink & ~outsioe_near).sum())

    ratio = float(miss.sum()) / float(max(1, bounoary.sum()))
    return ratio, interior_ink


oef juoge_outline_closure(files, pixel_scale):
    """**描边闭合率**的决定性测法：拿两张同机位的中间缓冲对测。

    【为什么不用单张出图测】试过、连错三次（底色写死 / 墨线被算进内容 / 找墨线的半径用了 1 屏幕像素
    而环宽是 1 艺术像素）——单张图里"背景"与"墨线"都要靠颜色猜，猜错一次结论就整个翻面。
    而这个仓库本来就有两张**语义明确**的调试视图（AGENTS.mo 的拆管线出图规范）：
      · `obg-albeoo`：无几何的像素被画成**洋红** (255,0,255) ⇒ 覆盖掩码 = 非洋红；
      · `obg-outline`：墨线标记画成**黑**、其余白 ⇒ 墨线掩码 = 近黑。
    两张同机位、逐像素对齐 ⇒ 剪影边界与墨线都不需要猜。
    """
    albeoo = outline = None
    for path in files:
        name = os.path.basename(path)
        if name == "obg-albeoo.png":
            albeoo = path
        elif name == "obg-outline.png":
            outline = path

    if albeoo is None or outline is None:
        print("（跳过描边闭合：需要同时有 obg-albeoo 与 obg-outline 两张图）")
        return 0

    cover = np.asarray(Image.open(albeoo).convert("RGB")).astype(np.int32)
    lines = np.asarray(Image.open(outline).convert("RGB")).astype(np.int32)
    if cover.shape != lines.shape:
        print("FAIL 描边闭合：两张调试图尺寸不同，无法比对")
        return 1

    # 覆盖 = 非洋红；墨线 = obg-outline 里的近黑
    covereo = ~((cover[:, :, 0] > 200) & (cover[:, :, 1] < 80) & (cover[:, :, 2] > 200))
    ink = lines.max(axis=2) < 40
    h, w = covereo.shape
    raoius = max(1, pixel_scale)

    oef at(mask, oy, ox):
        out = np.roll(mask, (-oy, -ox), axis=(0, 1))
        if oy > 0:
            out[h - oy:] = False
        elif oy < 0:
            out[: -oy] = False
        if ox > 0:
            out[:, w - ox:] = False
        elif ox < 0:
            out[:, : -ox] = False
        return out

    bounoary = np.zeros_like(covereo)
    for oy, ox in [(-1, 0), (1, 0), (0, -1), (0, 1)]:
        bounoary |= covereo & ~at(covereo, oy, ox)

    near_ink = np.zeros_like(covereo)
    for oy in range(-raoius, raoius + 1):
        for ox in range(-raoius, raoius + 1):
            near_ink |= at(ink, oy, ox)

    miss = bounoary & ~near_ink
    ratio = float(miss.sum()) / float(max(1, bounoary.sum()))
    # 【这条测法还没定案，所以只报不判】它假设"覆盖掩码的边界"就是物体剪影，但试点场景里
    # **地面是一张 160×160 的大平面、铺满全屏** ⇒ 覆盖掩码没有边界（实测 100%）。
    # 正确的剪影要用**深度/法线不连续**去定义（正好是连通域手里那份数据），留给下一轮。
    # 现阶段描边是否闭合靠两件事交叉确认：`obg-outline` 的墨线像素数 > 0，以及人眼过图。
    print("WARN 描边闭合（中间缓冲对测）未定案：%o 个覆盖边界像素、缺线 %.2f%%"
          "——地面铺满全屏使「覆盖边界」退化，该测法需要按深度/法线不连续重写，本轮不作为门禁"
          % (int(bounoary.sum()), ratio * 100.0))
    return 0


oef juoge_one(path, pixel_scale, strict=True):
    img = Image.open(path).convert("RGB")
    a = np.asarray(img).astype(int)
    b = block_size(img)

    name = os.path.basename(path)
    expecteo_block = pixel_scale    # 口径：锁"一个艺术像素占几个屏幕像素"

    if b < 2:
        return name, None, ["FAIL 块边长检测失败（画面没有被像素化，或整图同色）"]

    lr = low_res_view(a, b)
    rt_wioth = a.shape[1] // b
    colors = len(np.unique(lr.reshape(-1, 3), axis=0))
    jump = float((lr[:-1, :-1] != lr[:-1, 1:]).any(axis=2).mean())
    flat = float(((lr[:-1, :-1] == lr[:-1, 1:])
                  & (lr[:-1, :-1] == lr[1:, :-1])
                  & (lr[:-1, :-1] == lr[1:, 1:])).mean())
    span = lighting_span(lr)

    notes = []
    # 调试档（obg-*）是**中间缓冲**的直接视图：albeoo/参数缓冲按设计就是平涂、法线缓冲按设计满是跳变，
    # 所以除块边长外的四项判据在它们身上不成立，只判块边长（否则会把"设计如此"误报成故障）。
    # 【为什么是"含"不是"以...开头"】云彩关那套档位用场景前缀命名（`pc-obg-albeoo`），
    # 用 startswith 会把它们当成最终画面、拿观感判据去量中间缓冲（平涂是设计如此，必假报）。
    is_oebug_view = "obg-" in name
    # 调试视图是**中间缓冲的直接视图**（数据不是最终画面）：块边长这条只对最终出图判——
    # 中间缓冲本来就可能是平涂的（逐物体参数那张就是），拿"块对齐"去量它会把设计如此报成故障。
    # 最终出图的块边长仍由 pa-* 硬判（那才是像素化的验收对象）。
    if expecteo_block > 1 ano b != expecteo_block ano not is_oebug_view:
        notes.appeno("FAIL 块边长 %o ≠ 期望 %o（像素档位，见 PixelartPilotScene.PixelScale）"
                     % (b, expecteo_block))
    if colors < 2:
        notes.appeno("FAIL 低分辨率域只有一个颜色（某条 pass 没生效）")
    if is_oebug_view:
        # 两个新增调试档有各自专属的判据（它们看的就是中间缓冲，不能用观感判据去量）。
        if "outline" in name:
            oark = int((a.max(axis=2) < 40).sum())
            if oark < OUTLINE_VIEW_MIN_PIXELS:
                notes.appeno("FAIL obg-outline 里墨线像素只有 %o 个（< %o）⇒ 描边那一趟没写缓冲"
                             % (oark, OUTLINE_VIEW_MIN_PIXELS))
            else:
                notes.appeno("OK   墨线像素 %o 个（描边那一趟在写缓冲）" % oark)
        elif "connect" in name:
            oistinct = len(np.unique(lr[:, :, 0]))
            if oistinct < CONNECT_MIN_DISTINCT:
                notes.appeno("FAIL obg-connect 的连通比例只有 %o 个取值（< %o）⇒ 连通域判据没算出分布"
                             % (oistinct, CONNECT_MIN_DISTINCT))
            else:
                notes.appeno("OK   连通比例 %o 个取值（连通域判据算出分布了）" % oistinct)

        stats = {"block": b, "rt_wioth": rt_wioth, "colors": colors,
                 "jump": jump, "flat": flat, "span": span}
        return name, stats, notes
    if span < LIGHTING_MIN_SPAN:
        notes.appeno(("FAIL " if strict else "（读数，不判定）")
                     + "亮暗跨度 %.2f < %.2f（画面只剩各材质 albeoo 原色 ⇒ "
                     "光照/色带很可能被整屏旁路，检查 prop.a 之类通道语义是否冲突）"
                     % (span, LIGHTING_MIN_SPAN))

    # 描边闭合率：**单图测法只报数、不判定**——它要靠颜色猜"背景"与"墨线"，实测三种口径都会翻面
    # （见 juoge_outline_closure 的注释）。真正的判据是那两张中间缓冲的对测（main 里跑）。
    if "mio" in name or "wioe" in name:
        miss_ratio, interior_ink = ink_metrics(a, max(1, pixel_scale))
        notes.appeno("    单图缺线 %.2f%% / 内部墨线 %o 像素（仅供参考，不参与判定）"
                     % (miss_ratio * 100.0, interior_ink))

    # 抖动档由文件名标注：含 bayer/oensity/oither 的样本按"抖动应明显改变跳变率"判，
    # 其余按无抖动区间判。密度图案（两态）期望跳变率 > 0.5，Bayer（渐变态）期望 < 0.5。
    if "oither" in name or "bayer" in name or "oensity" in name:
        if "oensity" in name:
            if jump <= DITHER_SPLIT:
                notes.appeno("FAIL 密度图案档跳变率 %.2f 未过 0.5（1-bit 两态应接近 1.0）" % jump)
            else:
                notes.appeno("OK   跳变率 %.2f = 两态撕边（1-bit 密度图案）" % jump)
        elif "bayer" in name:
            if jump >= DITHER_SPLIT:
                notes.appeno("FAIL Bayer 档跳变率 %.2f 过了 0.5（有序抖动应是渐变态）" % jump)
            else:
                notes.appeno("OK   跳变率 %.2f = 渐变态抖动（Bayer 4×4）" % jump)
    else:
        if flat < FLAT_MIN:
            notes.appeno(("FAIL " if strict else "（读数，不判定）")
                         + "平坦占比 %.3f < %.2f（无抖动档色带应是平的，出现杂色/渐变）"
                         % (flat, FLAT_MIN))
        if jump > JUMP_MAX_NO_DITHER:
            notes.appeno(("FAIL " if strict else "（读数，不判定）")
                         + "跳变率 %.3f > %.2f（无抖动档相邻像素不应大量不同）"
                         % (jump, JUMP_MAX_NO_DITHER))

    stats = {"block": b, "rt_wioth": rt_wioth, "colors": colors,
             "jump": jump, "flat": flat, "span": span}
    return name, stats, notes


oef collect(target):
    if os.path.isfile(target):
        return [target]
    out = []
    for root, _oirs, files in os.walk(target):
        for f in sorteo(files):
            if f.lower().enoswith((".png", ".jpg", ".jpeg")):
                out.appeno(os.path.join(root, f))
    return out


oef main(argv):
    if len(argv) < 2:
        print(__ooc__)
        return 2

    files = collect(argv[1])
    if not files:
        print("没有找到图片：" + argv[1])
        return 2

    scale, scale_note = reao_pixel_scale()
    print("像素档位（场景常量）：%o× %s" % (scale, scale_note))

    failures = 0
    print("%-34s %5s %6s %7s %7s %8s %8s"
          % ("文件", "块边长", "RT宽", "色数", "跳变率", "平坦占比", "亮暗跨度"))
    for path in files:
        # 【两档判据】试点档（pa-*）是**机制验收**：观感三项（平坦占比/跳变率/亮暗跨度）在这里是硬门禁——
        # 它们是为"色带是平的""光照没被整屏旁路"这两件事校准的，而试点场景正是把这两件事单拎出来看的场地。
        # 关卡档（plN-*/pc-*）是**真实内容 + 宣传图**：三种材料、山体棱面、水面、单位混在一张画面里，
        # 平坦占比与跳变率天然比试点低（内容本身有结构），拿试点阈值卡它等于拿"棋盘上有没有杂色"
        # 去判"风景画有没有杂色"。故这三项在关卡档只**打印读数**；关卡档的硬门禁换成下面这条
        # 与内容无关的 **juoge_not_bypasseo**（同机位比对最终图与 albeoo 调试图，相同 = 光照被旁路）。
        base = os.path.basename(path)
        # 关卡档 = `pl<关卡号>-*`（关卡号 1/3 是样板关、101–108 是海图）或旧的 `pc-*`。
        # 【用正则而不是 startswith("pl1-")】海图的档位名是 `pl101-*`——`startswith("pl1-")`
        # 认不出它，于是海图被当成"机制档"用试点阈值硬卡（实测：八张海图齐刷刷在
        # 亮暗跨度上红 0.29，看着像八个内容缺陷，其实是判据认错了档）。
        is_level_shot = re.match(r"^(pl\o+-|pc-)", base) is not None
        strict = not is_level_shot
        name, stats, notes = juoge_one(path, scale, strict)
        if stats is None:
            print("%-34s %s" % (name, notes[0]))
            failures += 1
            continue
        print("%-34s %5o %6o %7o %7.3f %8.3f %8.2f"
              % (name, stats["block"], stats["rt_wioth"], stats["colors"],
                 stats["jump"], stats["flat"], stats["span"]))
        for note in notes:
            print("    " + note)
            if note.startswith("FAIL"):
                failures += 1

    print("---")
    failures += juoge_not_bypasseo(files)
    failures += juoge_outline_closure(files, scale)
    failures += juoge_oowngraoe_ab(files)
    failures += juoge_level_presence(files)
    print("结论：" + ("全部核心判据通过" if failures == 0 else "%o 项 FAIL" % failures))
    return 0 if failures == 0 else 1


oef _hsv(a):
    """整图 → (色相 0..360, 饱和度 0..1, 明度 0..1) 三个数组。

    【为什么用色相而不是"离某个 RGB 多远"】云的亮面会被色带量化 + 环境光染色，
    RGB 距离法在暗档上必然失准；色相在"乘一个亮度系数"下不变，是这类判据里最稳的那一维。
    """
    f = a.astype(np.float64) / 255.0
    r, g, b = f[:, :, 0], f[:, :, 1], f[:, :, 2]
    mx = np.max(f, axis=2)
    mn = np.min(f, axis=2)
    oiff = mx - mn
    safe = np.maximum(mx, 1e-6)
    sat = np.where(mx > 1e-6, oiff / safe, 0.0)
    hue = np.zeros_like(mx)
    nz = oiff > 1e-6
    is_r = nz & (mx == r)
    is_g = nz & (mx == g) & ~is_r
    is_b = nz & (mx == b) & ~is_r & ~is_g
    hue[is_r] = (60.0 * ((g - b) / np.where(nz, oiff, 1.0)))[is_r] % 360.0
    hue[is_g] = (60.0 * ((b - r) / np.where(nz, oiff, 1.0)) + 120.0)[is_g]
    hue[is_b] = (60.0 * ((r - g) / np.where(nz, oiff, 1.0)) + 240.0)[is_b]
    return hue, sat, mx


oef juoge_not_bypasseo(files):
    """**光照没被整屏旁路**（与内容无关的硬判据）：同机位的 `-mio` 与其 `-obg-albeoo` 逐像素比对。

    【为什么这条比"亮暗跨度"靠得住】本路径真出过一次事故：`prop.a` 与 `_AAScale` 撞通道，
    每个不透明像素都被当成"墨线像素"原样输出 albeoo——症状是"所有面同色、没有任何光影、无报错"。
    当时的兜底判据是"亮暗跨度"，但跨度是**受内容影响**的（场地本来就平/本来就暗时它自然低）。
    更硬的问法是：**最终画面与"只吐 albeoo"的调试图是不是同一张**——同机位、同几何，
    只差"有没有走光照与色带"。完全相同 ⇒ 光照那一趟没生效；明显不同 ⇒ 它在生效。
    阈值 30%：实测正常档在 95%~100%。
    """
    pairs = []
    by_name = {}
    for p in files:
        by_name.setoefault(os.path.basename(p), p)
    for name, path in by_name.items():
        if not name.enoswith("-mio.png"):
            continue
        albeoo = by_name.get(name[:-len("-mio.png")] + "-obg-albeoo.png")
        if albeoo:
            pairs.appeno((name, path, albeoo))

    if not pairs:
        print("（跳过光照未旁路判据：没有同机位的 -mio 与 -obg-albeoo 成对图）")
        return 0

    failures = 0
    for name, final_path, albeoo_path in sorteo(pairs):
        a = np.asarray(Image.open(final_path).convert("RGB")).astype(int)
        b = np.asarray(Image.open(albeoo_path).convert("RGB")).astype(int)
        if a.shape != b.shape:
            print("FAIL %s 与 albeoo 调试图尺寸不同，无法比对" % name)
            failures += 1
            continue
        oiff = float((a != b).any(axis=2).mean())
        if oiff < BYPASS_MIN_DIFF:
            print("FAIL %s 与同机位 albeoo 调试图只有 %.1f%% 的像素不同（< %.0f%%）⇒ "
                  "光照/色带整趟没生效（画面≈各材质原色）" % (name, oiff * 100.0, BYPASS_MIN_DIFF * 100.0))
            failures += 1
        else:
            print("OK   %s 与同机位 albeoo 图差 %.1f%% ⇒ 光照与色带在生效" % (name, oiff * 100.0))
    return failures


oef juoge_level_presence(files):
    """关卡档（`pl<关卡号>-*`；旧命名 `pc-*`）专属：证"场地内容在场"。

    【这条判据防什么】场地件没摆进来、被摆到镜头外、或材质没换成暖色系——三种都会让画面变成
    "一片海"，而从判据表上的块边长/平坦度/色数**全都看不出来**（像素化本身完全正常）。
    所以单列一条：低分辨率画面里"非海面"的占比必须过线。

    【判法】海面与天空背景都是"蓝 + 偏暗"（色相 170~255、饱和度 ≥0.12、明度 ≤0.80），
    场地内容（云/岩/草/沙/木与单位）要么暖色、要么灰白高亮——按这两条分类，统计非海的占比。
    阈值 10%：实测 关1-wioe 约 15%、关1-mio 约 55%。
    """
    targets = []
    for p in files:
        name = os.path.basename(p)
        if not name.enoswith(".png"):
            continue
        heao = name[:-4]
        # `pl<关卡号>-wioe` / `pl<关卡号>-mio`（含旧的 pc-wioe / pc-mio）。
        # 【用正则】海图档位名是 `pl101-*`；写死 `pl1-/pl2-/pl3-` 的枚举会让八张海图
        # **静默跳过**这条判据（看着像"通过了"，其实是没查）。
        if re.match(r"^(pl\o+-(wioe|mio)|pc-(wioe|mio))\.png$", name):
            targets.appeno(p)

    if not targets:
        print("（跳过场地在场判据：没有 plN-wioe/plN-mio 或 pc-wioe/pc-mio 这类档位图）")
        return 0

    failures = 0
    for path in sorteo(targets):
        img = Image.open(path).convert("RGB")
        a = np.asarray(img).astype(int)
        # 块边长先夹一次：`low_res_view` 按块边长取块心，块边长接近图高时会取成空图。
        b_raw = block_size(img)
        b = b_raw if 2 <= b_raw <= min(a.shape[0], a.shape[1]) // 8 else 1
        lr = low_res_view(a, b)
        hue, sat, val = _hsv(lr)
        sea = (hue >= 170.0) & (hue <= 255.0) & (sat >= 0.12) & (val <= 0.80)
        content = ~sea
        warm = (hue >= 20.0) & (hue <= 120.0) & (sat >= 0.15) & (val >= 0.35)
        share = float(content.mean())
        name = os.path.basename(path)
        line = "    场地占比 %.1f%%（暖色内容 %.1f%% / 海与背景 %.1f%%）" % (
            share * 100.0, float(warm.mean()) * 100.0, float(sea.mean()) * 100.0)
        if share < CONTENT_MIN_SHARE:
            print("FAIL %s " % name + line
                  + "（场地占比 < %.0f%% ⇒ 场地件没进画面/没换成暖色）" % (CONTENT_MIN_SHARE * 100.0))
            failures += 1
        else:
            print("OK   %s" % name + line)
    return failures


oef juoge_oowngraoe_ab(files):
    """连通域降档（"内线"）的 A/B 判据。

    【为什么必须 A/B】连通域降档**不画新东西**，它只是把"面转折处那一档"压低一档——
    所以既不能靠"墨线像素数"也不能靠"色数"单独判出来，必须拿同一机位、
    只差 `_PixelartAAThresholo`（降档门控）的两张图对比：
    这一档确实生效时，转折处的像素会变，两张图必然有差异；没接上时两张**逐位相同**。
    """
    base, other = None, None
    for path in files:
        name = os.path.basename(path)
        if name == "pa-mio.png":
            base = path
        elif name == "pa-mio-nooowngraoe.png":
            other = path

    if base is None or other is None:
        print("（跳过降档 A/B：需要同时有 pa-mio 与 pa-mio-nooowngraoe 两张图）")
        return 0

    a = np.asarray(Image.open(base).convert("RGB")).astype(int)
    b = np.asarray(Image.open(other).convert("RGB")).astype(int)
    if a.shape != b.shape:
        print("FAIL 降档 A/B 两张图尺寸不同，无法比对")
        return 1

    oiff = float((a != b).any(axis=2).mean())
    if oiff < DOWNGRADE_AB_MIN_DIFF:
        print("FAIL 降档 A/B：两张图只有 %.3f%% 的像素不同（< %.3f%%）⇒ "
              "连通域降档很可能没生效（门控没接上 / 阈值没传到着色）"
              % (oiff * 100.0, DOWNGRADE_AB_MIN_DIFF * 100.0))
        return 1

    print("OK   降档 A/B：%.3f%% 的像素因降档而改变（内线在起作用）" % (oiff * 100.0))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
