#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""palette_tool —— 全局调色板的校验 / 预览 / 量化工具（确定性，纯 Python + numpy + Pillow）。

【它解决什么问题】
    `pirate-crew/Assets/Data/Palette/pirate_palette.json` 是本作唯一的全局调色板真源
    （美术风格指南 §3.2 / 像素纹理资产管线 §5）。本工具把这张板变成可执行的约束：
    校验板自身结构、出人眼验收条带图、把任意 PNG 量化到板上（OkLab 空间最近邻），
    并用一组自证用例把「确定性 / 锁板 / 明度单调」三条性质变成可重跑的断言。

【使用者】像素纹理烘焙链的下游（tools/blender/pixel）、CI 式回归、人工抽查。

【为什么是 OkLab 而不是 RGB 欧氏距离】
    见 docs/技术/资产管线/调研-调色板量化与纹素密度.md §1：RGB 空间最近邻会系统性偏色，
    且会毁掉明度层次（一张渐变在 RGB 距离下会被切成明度乱序的色块）。OkLab 是感知均匀空间，
    L / C / H 正交解耦，明度阶梯在量化后基本保持单调。

【确定性纪律（资产篇 §4 末段）】
    - 量化是 (像素, x, y, 板) 的纯函数：无随机数、无迭代、无数据结构差异（暴力 O(N·64) 匹配）；
    - 抖动只用 ordered（Bayer 4×4 / 8×8），中点归一化 (m+0.5)/n²；误差扩散一律不实现；
    - 混合在线性光里做（γ=2.2 拆解），否则抖动区整体偏亮；
    - PNG 编码锁定 compress_level=9 + optimize=False（Pillow 默认不写 tIME 块）。
    以上合起来 ⇒ 同输入同输出，逐字节一致；`verify` 子命令用 sha256 当场证明。

【子命令】
    check                        校验板：id/hex 唯一、色数 32~64、组内明度单调、OkLCH 表
    oklch                        打印每槽的 OkLCh(L,C,H) 与 hex（人眼核色用）
    strip --out <png>            出参考条带图（供人眼验收；入 docs/images/palette/）
    quantize --in <png> --out <png> [--dither none|bayer4|bayer8] [--weight W]
                                 把任意 PNG 量化到板上
    verify                       自证：OkLab 官方测试表 + 确定性 hash + 锁板 + 明度单调

【用法（仓库根执行）】
    python tools/palette/palette_tool.py verify
    python tools/palette/palette_tool.py quantize --in a.png --out b.png --dither bayer4
"""

import argparse
import hashlib
import json
import os
import sys

import numpy as np
from PIL import Image

# ---------------------------------------------------------------------------
# 路径与常量
# ---------------------------------------------------------------------------

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
DEFAULT_PALETTE = os.path.join(REPO_ROOT, "pirate-crew", "Assets", "Data", "Palette", "pirate_palette.json")

#: 板色数允许区间（美术风格指南 §3.2：32~64 色）
MIN_COLORS = 32
MAX_COLORS = 64

#: 行尾常量（写出侧恒 LF；读入侧把 CRLF 归一 —— 见 _read_text 的说明）
CRLF = "\r\n"
LF = "\n"

#: RGB 通道搬进 a/b 的权重（调色板量化 §1 第 2 条：d = ΔL² + w·(Δa²+Δb²)，w 从 1 起步）
DEFAULT_WEIGHT = 1.0

#: 两板色混合比的离散搜索档数上限（Yliluoma 式两色混合在本工具的确定性简化：1-D 定步长搜索）。
#: 实际档数取「抖动矩阵的格子数」—— n×n 的 Bayer 本身就只提供 n² 个阈值等级，
#: 搜索档多于 n² 是无意义的（4×4 → 1/16 粒度，8×8 → 1/64 粒度）。
#: 【实测踩过】若把两种矩阵都固定成 1/8 粒度，bayer4 与 bayer8 的输出会**逐像素完全相同**
#: （同一个 alpha 下两者选中的是同一组像素），等于白提供一个选项。
MAX_MIX_STEPS = 64

# ---------------------------------------------------------------------------
# OkLab（Björn Ottosson 2021 修订版矩阵；https://bottosson.github.io/posts/oklab/）
# ---------------------------------------------------------------------------

#: XYZ(D65) → LMS 的立方根后线性组合
_XYZ_TO_LMS = np.array([
    [0.8190224379967030, 0.3619062600528904, -0.1288737815209879],
    [0.0329836539323885, 0.9292868615863434, 0.0361446663506424],
    [0.0481771893596242, 0.2642395317527308, 0.6335478284694309],
])
_LMS_TO_LAB = np.array([
    [0.2104542683093140, 0.7936177747023054, -0.0040720430116193],
    [1.9779985324311684, -2.4285922420485799, 0.4505937096174110],
    [0.0259040424655478, 0.7827717124575296, -0.8086757549230774],
])
#: 线性 sRGB → XYZ(D65)（与上方同一份矩阵链，来源同上）
_SRGB_TO_XYZ = np.array([
    [0.4123907992659595, 0.3575843393838780, 0.1804807884018343],
    [0.2126390058715104, 0.7151686787677559, 0.0721923153607337],
    [0.0193308187155918, 0.1191947797946259, 0.9505321522496608],
])

#: Ottosson 原文公布的 XYZ→OkLab 参考值（verify 子命令的回归单测，容差 1e-3）
OKLAB_REFERENCE = [
    ((0.950, 1.000, 1.089), (1.000, 0.000, 0.000)),
    ((1.000, 0.000, 0.000), (0.450, 1.236, -0.019)),
    ((0.000, 1.000, 0.000), (0.922, -0.671, 0.263)),
    ((0.000, 0.000, 1.000), (0.153, -1.415, -0.449)),
]


def srgb_to_linear(u8):
    """sRGB 8bit（0~255，int/float 皆可）→ 线性光 0~1。

    本工程 ProjectSettings 的 m_ActiveColorSpace = 0（Gamma），纹理字节按 sRGB 原样显示，
    因此「屏幕上的颜色」就是 sRGB 编码值 —— 感知距离必须在 sRGB→线性→OkLab 这条链上算。
    """
    c = np.asarray(u8, dtype=np.float64) / 255.0
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def linear_to_oklab(linear_rgb):
    """线性 sRGB（0~1）→ OkLab。入参形状 (..., 3)。"""
    shape = np.shape(linear_rgb)
    flat = np.asarray(linear_rgb, dtype=np.float64).reshape(-1, 3)
    xyz = flat @ _SRGB_TO_XYZ.T
    lms = xyz @ _XYZ_TO_LMS.T
    # 不钳非负：XYZ 可以落在 Oklab 定义域外（如 (0,0,1)），负 LMS 的实立方根是合法且必需的
    # （钳 0 会让官方测试表第 4 组偏差飙到 0.999 —— 已实测踩过）。
    lms = np.cbrt(lms)
    lab = lms @ _LMS_TO_LAB.T
    return lab.reshape(shape)


def srgb8_to_oklab(rgb8):
    """sRGB 8bit → OkLab（量化链的主入口）。"""
    return linear_to_oklab(srgb_to_linear(rgb8))


def oklab_to_oklch(lab):
    """OkLab → OkLCh（L, C, H 度）。H 在 C≈0 时无意义，返回 0。"""
    lab = np.asarray(lab, dtype=np.float64)
    L = lab[..., 0]
    a = lab[..., 1]
    b = lab[..., 2]
    C = np.hypot(a, b)
    H = np.degrees(np.arctan2(b, a)) % 360.0
    H = np.where(C < 1e-6, 0.0, H)
    return np.stack([L, C, H], axis=-1)


def xyz_to_oklab(xyz):
    """XYZ(D65) → OkLab（只服务 verify 的官方测试表；生产链走 srgb8_to_oklab）。"""
    xyz = np.asarray(xyz, dtype=np.float64)
    lms = np.cbrt(xyz @ _XYZ_TO_LMS.T)
    return lms @ _LMS_TO_LAB.T


# ---------------------------------------------------------------------------
# Bayer 矩阵（中点归一化 (m+0.5)/n²；与 PirateToon.shader 的 4×4 同口径）
# ---------------------------------------------------------------------------

BAYER4 = np.array([
    [0, 8, 2, 10],
    [12, 4, 14, 6],
    [3, 11, 1, 9],
    [15, 7, 13, 5],
], dtype=np.float64)

BAYER8 = np.array([
    [0, 32, 8, 40, 2, 34, 10, 42],
    [48, 16, 56, 24, 50, 18, 58, 26],
    [12, 44, 4, 36, 14, 46, 6, 38],
    [60, 28, 52, 20, 62, 30, 54, 22],
    [3, 35, 11, 43, 1, 33, 9, 41],
    [51, 19, 59, 27, 49, 17, 57, 25],
    [15, 47, 7, 39, 13, 45, 5, 37],
    [63, 31, 55, 23, 61, 29, 53, 21],
], dtype=np.float64)

DITHER_MATRICES = {"bayer4": BAYER4, "bayer8": BAYER8}


def bayer_threshold(matrix, height, width):
    """铺满整图的 Bayer 阈值场，取值 [0,1)（中点归一化，不是 m/16）。"""
    n = matrix.shape[0]
    ys = (np.arange(height) % n)
    xs = (np.arange(width) % n)
    field = matrix[np.ix_(ys, xs)]
    return (field + 0.5) / float(n * n)


# ---------------------------------------------------------------------------
# 调色板
# ---------------------------------------------------------------------------

class Palette:
    """从 JSON 镜像读入的调色板（不可变视图 + 预计算的 OkLab 表）。"""

    def __init__(self, data, path="<memory>"):
        self.path = path
        self.data = data
        self.name = data["name"]
        self.version = data["version"]
        self.texel_density = data.get("texelDensityPxPerMeter")
        self.slots = list(data["slots"])
        self.ids = [s["id"] for s in self.slots]
        self.groups = [s.get("group", "") for s in self.slots]
        self.rgb8 = np.array([hex_to_rgb8(s["hex"]) for s in self.slots], dtype=np.uint8)
        self.lab = srgb8_to_oklab(self.rgb8)
        self.lch = oklab_to_oklch(self.lab)

    def __len__(self):
        return len(self.slots)

    def index_of(self, slot_id):
        try:
            return self.ids.index(slot_id)
        except ValueError:
            raise KeyError("调色板里没有槽位 %r（合法槽见 %s）" % (slot_id, self.path))


def hex_to_rgb8(hex_str):
    h = str(hex_str).lstrip("#")
    if len(h) != 6:
        raise ValueError("hex 必须是 6 位 RRGGBB：%r" % hex_str)
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def load_palette(path=DEFAULT_PALETTE):
    with open(path, "r", encoding="utf-8") as f:
        return Palette(json.load(f), path)


#: 槽位字段的**固定**写出顺序（改这里必须同步改 Unity 侧 PaletteJson.cs，否则两边 diff 不 dry）
SLOT_KEYS = ("id", "group", "hex", "usage", "status", "source")
#: 顶层字段的固定写出顺序
ROOT_KEYS = ("name", "version", "texelDensityPxPerMeter", "note")


def canonical_text(data):
    """把板写成**规范文本**：顶层键各占一行 2 空格缩进，slots 数组一槽一行。

    【为什么要有这个函数】板文件同时被三个消费者读：Python 量化工具、Unity 侧
    PaletteAssetBuilder（JSON↔.asset 双向）、人眼 code review。三方各自 dumps 会产生
    毫无意义的格式噪声（缩进/空格差异），把真正的改动淹没在 diff 里。规范文本把布局钉死：
    「同一份板 ⇒ 同一串字节」，Unity 侧 PaletteJson.ToCanonicalText 是它的逐字节镜像实现。
    """
    lines = ["{"]
    for key in ROOT_KEYS:
        if key in data:
            lines.append("  %s: %s," % (json.dumps(key), json.dumps(data[key], ensure_ascii=False)))
    lines.append('  "slots": [')
    slots = data["slots"]
    for i, slot in enumerate(slots):
        parts = ", ".join("%s: %s" % (json.dumps(k), json.dumps(slot[k], ensure_ascii=False))
                          for k in SLOT_KEYS if k in slot)
        lines.append("    {%s}%s" % (parts, "" if i == len(slots) - 1 else ","))
    lines.append("  ]")
    lines.append("}")
    return "\n".join(lines) + "\n"


def write_canonical(palette, path=None):
    """按规范文本写回板文件（LF + 末尾换行，Windows 上也必须是 LF —— 否则 git 每次都报整文件改）。"""
    path = path or palette.path
    text = canonical_text(palette.data)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
    return text


def _read_text(path):
    """读文本并归一行尾到 LF。

    【为什么要这一层】本仓 core.autocrlf=true，而 .gitattributes 的 eol=lf 白名单只覆盖 Unity
    序列化扩展名（.meta/.unity/.prefab/.asset/.mat/.controller）—— **.json/.md 不在其中**，
    别的机器或新克隆签出这份板时可能拿到 CRLF。规范文本的定义是 LF，但"读到的 CRLF"不该被报成
    "板漂移了"（那是 git 的行尾策略，不是内容差异）。写出侧永远写 LF。
    """
    with open(path, "r", encoding="utf-8", newline="") as f:
        return f.read().replace(CRLF, LF)


def is_canonical(palette):
    """磁盘上的文本是否已是规范文本（verify 用来防格式漂移；行尾容错见 _read_text）。"""
    return _read_text(palette.path) == canonical_text(palette.data)


def validate_palette(palette):
    """结构性校验；返回 (errors, warnings) 两个字符串列表。"""
    errors, warnings = [], []
    n = len(palette)
    if not (MIN_COLORS <= n <= MAX_COLORS):
        errors.append("色数 %d 不在 %d~%d 区间（美术风格指南 §3.2）" % (n, MIN_COLORS, MAX_COLORS))

    seen_id, seen_hex = {}, {}
    for i, slot in enumerate(palette.slots):
        sid = slot["id"]
        if not sid or not all(c.isupper() or c.isdigit() or c == "_" for c in sid):
            errors.append("槽位 id %r 不合规（要求 A-Z/0-9/_，全大写）" % sid)
        if sid in seen_id:
            errors.append("槽位 id 重复：%s" % sid)
        seen_id[sid] = i

        try:
            rgb = hex_to_rgb8(slot["hex"])
        except ValueError as exc:
            errors.append("槽位 %s 的 hex 非法：%s" % (sid, exc))
            continue
        key = "%02X%02X%02X" % rgb
        if key in seen_hex:
            # 板内重复色 = 量化时的死色，白白占一个板位
            errors.append("色值重复：%s 与 %s 同为 #%s" % (seen_hex[key], sid, key))
        seen_hex[key] = sid

        for field in ("group", "usage", "status", "source"):
            if not slot.get(field):
                warnings.append("槽位 %s 缺 %s 字段（项目规范要求标注出处与提案状态）" % (sid, field))

    # 组内必须连续声明（同一材质族不许被别的组打断 —— 板文件的可读性契约）
    seen_groups = []
    for group in palette.groups:
        if not seen_groups or seen_groups[-1] != group:
            seen_groups.append(group)
    if len(seen_groups) != len(set(seen_groups)):
        errors.append("同一 group 的槽位没有连续声明（出现 %d 段，重复组：%s）"
                      % (len(seen_groups),
                         sorted({g for g in seen_groups if seen_groups.count(g) > 1})))

    # 组内明度单调：同一材质族的亮/中/暗必须真的按 L 排序，否则「阶梯」是假的
    by_group = {}
    for slot, lch in zip(palette.slots, palette.lch):
        by_group.setdefault(slot.get("group", ""), []).append((slot["id"], float(lch[0])))
    for group, items in sorted(by_group.items()):
        ls = [v for _, v in items]
        if any(ls[i] > ls[i + 1] + 1e-6 for i in range(len(ls) - 1)):
            errors.append("组 %s 的槽位未按 OkLCH 明度升序声明：%s"
                          % (group, ", ".join("%s(L=%.3f)" % (i, v) for i, v in items)))
    return errors, warnings


# ---------------------------------------------------------------------------
# 量化
# ---------------------------------------------------------------------------

def nearest_indices(pixels_lab, palette, weight=DEFAULT_WEIGHT):
    """每像素到板上最近色的下标。d = ΔL² + w·(Δa²+Δb²)，暴力全表比较（无数据结构差异）。"""
    flat = np.asarray(pixels_lab, dtype=np.float64).reshape(-1, 3)
    p_lab = palette.lab
    dL = flat[:, 0:1] - p_lab[None, :, 0]
    da = flat[:, 1:2] - p_lab[None, :, 1]
    db = flat[:, 2:3] - p_lab[None, :, 2]
    dist = dL * dL + weight * (da * da + db * db)
    return np.argmin(dist, axis=1)


def _two_nearest(pixels_lab, palette, weight=DEFAULT_WEIGHT):
    """每像素的最近色与次近色下标（次近用于 ordered 抖动的两色混合）。"""
    flat = np.asarray(pixels_lab, dtype=np.float64).reshape(-1, 3)
    p_lab = palette.lab
    dL = flat[:, 0:1] - p_lab[None, :, 0]
    da = flat[:, 1:2] - p_lab[None, :, 1]
    db = flat[:, 2:3] - p_lab[None, :, 2]
    dist = dL * dL + weight * (da * da + db * db)
    order = np.argsort(dist, axis=1, kind="stable")  # stable = 并列时取板序在前者（确定性）
    return order[:, 0], order[:, 1]


def _alpha_candidates(steps=MAX_MIX_STEPS):
    return np.arange(steps + 1, dtype=np.float64) / float(steps)


def quantize_array(rgb8, palette, dither="none", weight=DEFAULT_WEIGHT, keep_alpha=None):
    """把 RGB（uint8, HxWx3）量化到板上；返回 (量化后 RGB uint8, 统计 dict)。

    抖动实现（Yliluoma 两板色混合模型的确定性简化）：像素的最近色 c1、次近色 c2，
    在 {0,1/8,...,1} 的 9 档混合比里选「线性光混合后 OkLab 误差最小」的 α，
    再由 Bayer 阈值决定该像素画 c1 还是 c2 —— 局部呈现的比例≈α，宏观即抗色带渐变。
    """
    rgb8 = np.asarray(rgb8, dtype=np.uint8)
    h, w = rgb8.shape[0], rgb8.shape[1]
    lab = srgb8_to_oklab(rgb8)
    idx1, idx2 = _two_nearest(lab, palette, weight)

    if dither == "none":
        out = palette.rgb8[idx1].reshape(h, w, 3)
        stats = {"dither": "none", "mix_steps": 0, "mixed_pixels": 0,
                 "colors_used": int(np.unique(idx1).size)}
        return out, stats

    matrix = DITHER_MATRICES[dither]
    thresholds = bayer_threshold(matrix, h, w).reshape(-1)
    steps = min(int(matrix.size), MAX_MIX_STEPS)

    lin = srgb_to_linear(palette.rgb8)      # (P,3)
    lin1 = lin[idx1]                        # (N,3)
    lin2 = lin[idx2]                        # (N,3)
    best_alpha = np.zeros(idx1.shape[0], dtype=np.float64)
    best_err = None
    for alpha in _alpha_candidates(steps):
        mixed = lin1 * (1.0 - alpha) + lin2 * alpha
        mixed_lab = linear_to_oklab(mixed)
        flat = lab.reshape(-1, 3)
        err = ((mixed_lab - flat) ** 2).sum(axis=1)
        if best_err is None:
            best_err = err
        else:
            better = err < best_err
            best_err = np.where(better, err, best_err)
            best_alpha = np.where(better, alpha, best_alpha)

    # ordered 决策：阈值 < α 的像素取次近色 —— 纯函数（依赖像素坐标与板，无随机数）
    use_second = thresholds < best_alpha
    chosen = np.where(use_second, idx2, idx1)
    out = palette.rgb8[chosen].reshape(h, w, 3)
    stats = {
        "dither": dither,
        "mix_steps": steps,
        "mixed_pixels": int(np.count_nonzero(use_second)),
        "colors_used": int(np.unique(chosen).size),
    }
    return out, stats


def quantize_image(image, palette, dither="none", weight=DEFAULT_WEIGHT):
    """PIL 图像 → 量化后的 PIL 图像（RGBA 时保留 alpha 原样：透明是画出来的，不参与锁板）。"""
    if image.mode == "RGBA":
        rgb = image.convert("RGB")
        out_rgb, stats = quantize_array(np.array(rgb), palette, dither, weight)
        alpha = np.array(image)[:, :, 3:4]
        out = np.concatenate([out_rgb, alpha], axis=2)
        return Image.fromarray(out.astype(np.uint8), "RGBA"), stats
    rgb = image.convert("RGB")
    out_rgb, stats = quantize_array(np.array(rgb), palette, dither, weight)
    return Image.fromarray(out_rgb.astype(np.uint8), "RGB"), stats


# ---------------------------------------------------------------------------
# 确定性 IO
# ---------------------------------------------------------------------------

PNG_SAVE_KWARGS = {"format": "PNG", "optimize": False, "compress_level": 9}


def save_png(image, path):
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    image.save(path, **PNG_SAVE_KWARGS)
    return path


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(65536), b""):
            h.update(chunk)
    return h.hexdigest()


def rel(path):
    """相对仓库根展示路径；仓库外的路径（如系统临时目录）原样返回 —— 别为一行日志崩掉工具。"""
    try:
        return os.path.relpath(path, REPO_ROOT)
    except ValueError:
        return path


# ---------------------------------------------------------------------------
# 参考条带图（人眼验收）
# ---------------------------------------------------------------------------

SWATCH_W = 96
SWATCH_H = 64
LABEL_H = 26
GRID_COLS = 8


def render_strip(palette, out_path):
    """按组分块出参考条带图：每格 = 色块 + 槽位 id + hex。

    用途（资产篇 §8）：把「板长什么样」固定成一张可入库的 PNG，评审时对着它判色；
    也用于 diff 回归 —— 板改一个色，这张图的 hash 就变。
    """
    from PIL import ImageDraw, ImageFont

    n = len(palette)
    cols = min(GRID_COLS, n)
    rows = (n + cols - 1) // cols
    width = cols * SWATCH_W
    height = rows * (SWATCH_H + LABEL_H)
    img = Image.new("RGB", (width, height), (24, 24, 28))
    draw = ImageDraw.Draw(img)
    try:
        font = ImageFont.load_default()
    except Exception:                                        # pragma: no cover
        font = None

    for i, slot in enumerate(palette.slots):
        cx = (i % cols) * SWATCH_W
        cy = (i // cols) * (SWATCH_H + LABEL_H)
        rgb = tuple(int(v) for v in palette.rgb8[i])
        draw.rectangle([cx, cy, cx + SWATCH_W - 1, cy + SWATCH_H - 1], fill=rgb)
        # 墨色描边：用板里的 INK，保证条带图本身也在板内取色
        ink = tuple(int(v) for v in palette.rgb8[palette.index_of("INK")])
        draw.rectangle([cx, cy, cx + SWATCH_W - 1, cy + SWATCH_H - 1], outline=ink)
        label = "%s\n#%s" % (slot["id"], slot["hex"].upper())
        if font is not None:
            draw.multiline_text((cx + 3, cy + SWATCH_H + 2), label, fill=(230, 232, 236), font=font,
                                spacing=1)
    return save_png(img, out_path)


def print_oklch_table(palette):
    print("%-20s %-10s %-8s %8s %8s %8s  %s" % ("slot", "group", "hex", "L", "C", "H", "status"))
    for slot, lch in zip(palette.slots, palette.lch):
        print("%-20s %-10s #%-7s %8.4f %8.4f %8.2f  %s"
              % (slot["id"], slot.get("group", ""), slot["hex"].upper(),
                 lch[0], lch[1], lch[2], slot.get("status", "")))


# ---------------------------------------------------------------------------
# 样品像素贴图（口径样板 + 导入规则的门禁靶子）
# ---------------------------------------------------------------------------

SAMPLE_SIZE = 64


def render_sample_tile(palette, out_path, slot_light="SAND_LIGHT", slot_mid="SAND_MID",
                       slot_dark="SAND_DARK", slot_ink="OUTLINE_INK"):
    """出一张 64×64 的**样品像素贴图**，完全由板内色构成、无随机数。

    它同时是三件事：
      ① 资产篇 §5「内部结构线画进纹理」的样板（结构线 = 2px 的 OUTLINE_INK，写在贴图里，
         不靠 shader 判定）；
      ② 像素纹理导入规范（Point / mip off / Uncompressed）的**门禁靶子** ——
         Assets/Art/Tests/PixelArtTextureImportTests.cs 断言这张图的导入设置，
         没有真实资产时那条用例是空转的；
      ③ 量化链的端到端演示（唯一色数必然 ≤ 板色数）。
    图案：上 2/3 亮沙、下 1/3 中沙，中缝一条 2px 的 INK 结构线（世界宽 2/32 = 6.25cm），
    另有 4 条 1px 的暗沙接缝（砖缝感）。全部取板内色。
    """
    n = SAMPLE_SIZE
    light = np.array(hex_to_rgb8(palette.slots[palette.index_of(slot_light)]["hex"]), dtype=np.uint8)
    mid = np.array(hex_to_rgb8(palette.slots[palette.index_of(slot_mid)]["hex"]), dtype=np.uint8)
    dark = np.array(hex_to_rgb8(palette.slots[palette.index_of(slot_dark)]["hex"]), dtype=np.uint8)
    ink = np.array(hex_to_rgb8(palette.slots[palette.index_of(slot_ink)]["hex"]), dtype=np.uint8)

    tile = np.zeros((n, n, 3), dtype=np.uint8)
    tile[:, :] = light
    tile[n * 2 // 3:, :] = mid                     # 下 1/3 中沙
    tile[n * 2 // 3 - 1:n * 2 // 3 + 1, :] = ink   # 2px 结构线
    for x in range(0, n, 16):                      # 竖向砖缝（1px 暗沙）
        tile[:n * 2 // 3, x] = dark
    tile[0:1, :] = ink                             # 四边墨线：平铺后读作砖缝
    tile[-1:, :] = ink
    tile[:, 0:1] = ink
    tile[:, -1:] = ink
    return save_png(Image.fromarray(tile, "RGB"), out_path)


# ---------------------------------------------------------------------------
# 自证用例
# ---------------------------------------------------------------------------

def _gray_ramp(n=256):
    ramp = np.arange(n, dtype=np.uint8)
    return np.stack([ramp] * 3, axis=1).reshape(1, n, 3)


def _two_color_gradient(c0, c1, n=256):
    """在**线性光**里 n 等分两个板色（模拟烘焙出来的渐变面），返回 1×n×3 uint8。"""
    a = srgb_to_linear(np.array(c0, dtype=np.float64))
    b = srgb_to_linear(np.array(c1, dtype=np.float64))
    t = np.linspace(0.0, 1.0, n).reshape(-1, 1)
    mixed = a[None, :] * (1 - t) + b[None, :] * t
    # 线性 → sRGB8（量化器的输入是 8bit sRGB，必须走完整往返）
    srgb = np.where(mixed <= 0.0031308, mixed * 12.92, 1.055 * np.power(np.maximum(mixed, 0.0), 1 / 2.4) - 0.055)
    return np.clip(np.round(srgb * 255.0), 0, 255).astype(np.uint8).reshape(1, n, 3)


def _synthetic_field(n=64):
    """确定性人造 2D 色场（无随机数）：R 沿 x、G 沿 y、B = 反比混合。

    比单行渐变更能压到抖动决策的边角（行/列下标都跨过 Bayer 矩阵的多个周期），
    用于确定性与锁板用例。
    """
    x = np.arange(n) * (255.0 / (n - 1))
    y = np.arange(n) * (255.0 / (n - 1))
    r, g = np.meshgrid(x, y)
    b = 255.0 - 0.5 * (r + g)
    return np.stack([r, g, np.clip(b, 0, 255)], axis=-1).astype(np.uint8)


def verify(palette, verbose=True):
    """跑全部自证用例；返回 (ok, report_lines)。"""
    lines = []
    ok = True

    def emit(text):
        lines.append(text)
        if verbose:
            print(text)

    # 1) OkLab 转换对齐 Ottosson 官方测试表
    max_err = 0.0
    for xyz, ref in OKLAB_REFERENCE:
        got = xyz_to_oklab(np.array(xyz))
        max_err = max(max_err, float(np.max(np.abs(got - np.array(ref)))))
    if max_err > 1e-3:
        ok = False
        emit("[FAIL] OkLab 官方测试表回归：最大偏差 %.6f > 1e-3" % max_err)
    else:
        emit("[ok] OkLab 官方测试表回归：4 组 XYZ 最大偏差 %.6f ≤ 1e-3" % max_err)

    # 2) 板结构
    errors, warnings = validate_palette(palette)
    if errors:
        ok = False
        for e in errors:
            emit("[FAIL] 板结构：" + e)
    else:
        emit("[ok] 板结构：%d 色 / %d 组，id 与色值唯一、组内明度升序" % (len(palette), len(set(palette.groups))))
    for w in warnings:
        emit("[warn] " + w)

    # 2b) 磁盘文本是否仍是规范文本（防三方写出者产生格式噪声）
    if not is_canonical(palette):
        ok = False
        emit("[FAIL] 板文件不是规范文本（跑 `palette_tool.py fmt` 归一化；Unity 侧 PaletteAssetBuilder 写出的也必须是同一布局）")
    else:
        emit("[ok] 板文件是规范文本（与 Unity 侧 PaletteJson.ToCanonicalText 同一布局）")

    # 3) 量化确定性：同一输入跑两次，逐字节一致。
    #    输入用 64×64 人造 2D 色场（行/列都跨 Bayer 矩阵多周期，bayer4 与 bayer8 会走出不同图案）。
    gen = _synthetic_field(64)
    passes = []
    for label, dither in (("none", "none"), ("bayer4", "bayer4"), ("bayer8", "bayer8")):
        h1 = hashlib.sha256(quantize_array(gen, palette, dither)[0].tobytes()).hexdigest()
        h2 = hashlib.sha256(quantize_array(gen, palette, dither)[0].tobytes()).hexdigest()
        if h1 != h2:
            ok = False
            emit("[FAIL] 确定性（%s）：两次运行不一致 %s vs %s" % (label, h1[:16], h2[:16]))
        else:
            emit("[ok] 确定性（%s）：重跑 sha256 一致 %s" % (label, h1))
        passes.append(h1)

    # 4) 锁板：所有输出色 100% ∈ 板
    board = {tuple(int(v) for v in rgb) for rgb in palette.rgb8}
    for dither in ("none", "bayer4", "bayer8"):
        out, stats = quantize_array(gen, palette, dither)
        uniq = {tuple(int(v) for v in px) for px in out.reshape(-1, 3)}
        stray = sorted(uniq - board)
        if stray:
            ok = False
            emit("[FAIL] 锁板（%s）：%d 个板外色，例 %s" % (dither, len(stray), stray[:3]))
        else:
            emit("[ok] 锁板（%s）：%d×%d 输出 %d 个唯一色，100%% ∈ 板；两色混合像素 %d/%d（混合档 %d）"
                 % (dither, out.shape[0], out.shape[1], len(uniq), stats["mixed_pixels"],
                    out.shape[0] * out.shape[1], stats["mix_steps"]))

    # 5) 明度单调：灰度 ramp 量化后输出明度必须非降（RGB 距离量化会破坏这条）
    ramp = _gray_ramp(256)
    out, _ = quantize_array(ramp, palette, "none")
    L = srgb8_to_oklab(out.reshape(-1, 3))[:, 0]
    drops = int(np.count_nonzero(np.diff(L) < -1e-9))
    if drops:
        ok = False
        emit("[FAIL] 明度单调（256 级灰 ramp）：出现 %d 处明度回退，最大回退 %.5f"
             % (drops, float(-np.min(np.diff(L)))))
    else:
        emit("[ok] 明度单调（256 级灰 ramp）：输出 L %.4f→%.4f 严格非降，0 处回退"
             % (float(L[0]), float(L[-1])))

    # 6) 明度单调（线性渐变，非灰轴）：量化后明度仍非降、且颜色全在板内
    for slot_a, slot_b in (("SEA_DEEP", "SEA_FOAM"), ("WOOD_DEEP", "SAND_LIGHT"),
                           ("SHADOW_DEEP", "LAMP_WARM")):
        grad = _two_color_gradient(hex_to_rgb8(palette.slots[palette.index_of(slot_a)]["hex"]),
                                   hex_to_rgb8(palette.slots[palette.index_of(slot_b)]["hex"]))
        g_out, _ = quantize_array(grad, palette, "none")
        gL = srgb8_to_oklab(g_out.reshape(-1, 3))[:, 0]
        g_drops = int(np.count_nonzero(np.diff(gL) < -1e-9))
        stray = {tuple(int(v) for v in px) for px in g_out.reshape(-1, 3)} - board
        if stray:
            ok = False
            emit("[FAIL] 锁板（%s→%s 渐变）：板外色 %s" % (slot_a, slot_b, sorted(stray)[:3]))
        elif g_drops:
            ok = False
            emit("[FAIL] 明度单调（%s→%s 渐变）：%d 处回退，最大 %.5f"
                 % (slot_a, slot_b, g_drops, float(-np.min(np.diff(gL)))))
        else:
            emit("[ok] 明度单调（%s→%s 渐变）：%d 档输出 L 非降、全在板内" % (slot_a, slot_b, gL.size))

    # 7) 幂等：已在板内的图再量化一次应完全不变
    src = palette.rgb8.reshape(1, -1, 3)
    once, _ = quantize_array(src, palette, "none")
    twice, _ = quantize_array(once, palette, "none")
    if not np.array_equal(once, src) or not np.array_equal(twice, once):
        ok = False
        emit("[FAIL] 幂等：板内色输入经量化发生变化")
    else:
        emit("[ok] 幂等：板内色输入量化后逐像素不变（%d 色）" % len(palette))

    # 8) 真实资产抽查：把仓库里现成的一张 64px 贴图量化一遍（有则跑，无则跳过）
    sample = os.path.join(REPO_ROOT, "pirate-crew", "Assets", "Art", "Textures", "Fx",
                          "ToonPilot_GlowHalo.png")
    if os.path.isfile(sample):
        src_img = Image.open(sample)
        before = len({tuple(int(v) for v in px)
                      for px in np.array(src_img.convert("RGB")).reshape(-1, 3)})
        q_img, _ = quantize_image(src_img, palette, "none")
        after = {tuple(int(v) for v in px) for px in np.array(q_img.convert("RGB")).reshape(-1, 3)}
        stray = sorted(after - board)
        if stray:
            ok = False
            emit("[FAIL] 真实资产抽查（%s）：板外色 %s" % (os.path.basename(sample), stray[:3]))
        else:
            emit("[ok] 真实资产抽查（%s）：%d 色 → %d 色，全部 ∈ 板"
                 % (os.path.basename(sample), before, len(after)))
    else:
        emit("[skip] 真实资产抽查：%s 不在仓库（跳过）" % rel(sample))

    emit("== verify %s ==" % ("通过" if ok else "失败"))
    return ok, lines

# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------

def cmd_check(args):
    palette = load_palette(args.palette)
    errors, warnings = validate_palette(palette)
    print("[palette] %s v%d  %d 色  %d 组  纹素密度 %s px/m"
          % (palette.name, palette.version, len(palette), len(set(palette.groups)), palette.texel_density))
    for w in warnings:
        print("[warn] " + w)
    for e in errors:
        print("[FAIL] " + e)
    if not errors:
        print("[ok] 板结构校验通过（id/色值唯一、色数 %d∈[%d,%d]、组内明度升序）"
              % (len(palette), MIN_COLORS, MAX_COLORS))
    return 0 if not errors else 1


def cmd_oklch(args):
    print_oklch_table(load_palette(args.palette))
    return 0


def cmd_fmt(args):
    palette = load_palette(args.palette)
    before = _read_text(palette.path)
    text = write_canonical(palette)
    changed = text != before
    print("[fmt] %s：%s  sha256=%s" % (rel(palette.path),
                                       "已归一化" if changed else "已是规范文本（无变化）",
                                       hashlib.sha256(text.encode("utf-8")).hexdigest()))
    return 0


def cmd_strip(args):
    palette = load_palette(args.palette)
    path = render_strip(palette, args.out)
    print("[strip] %s  %dx%d  sha256=%s"
          % (rel(path), SWATCH_W * min(len(palette), GRID_COLS),
             (SWATCH_H + LABEL_H) * ((len(palette) + min(len(palette), GRID_COLS) - 1) // min(len(palette), GRID_COLS)),
             sha256_file(path)))
    return 0


def cmd_sample(args):
    palette = load_palette(args.palette)
    path = render_sample_tile(palette, args.out)
    board = {tuple(int(v) for v in rgb) for rgb in palette.rgb8}
    uniq = {tuple(int(v) for v in px) for px in np.array(Image.open(path).convert("RGB")).reshape(-1, 3)}
    stray = sorted(uniq - board)
    print("[sample] %s  %dx%d  唯一色=%d  板外色=%d  sha256=%s"
          % (rel(path), SAMPLE_SIZE, SAMPLE_SIZE, len(uniq), len(stray), sha256_file(path)))
    if stray:
        print("[FAIL] 样品含板外色：%s" % stray[:5])
        return 1
    print("[ok] 样品唯一色 %d ≤ 板色数 %d（资产篇 §8 判据）；导入设置由约定目录的 "
          "AssetPostprocessor 强制。" % (len(uniq), len(palette)))
    return 0


def cmd_quantize(args):
    palette = load_palette(args.palette)
    src = Image.open(args.input)
    out, stats = quantize_image(src, palette, args.dither, args.weight)
    save_png(out, args.output)
    board = {tuple(int(v) for v in rgb) for rgb in palette.rgb8}
    uniq = {tuple(int(v) for v in px) for px in np.array(out.convert("RGB")).reshape(-1, 3)}
    stray = sorted(uniq - board)
    print("[quantize] %s -> %s  模式=%s dither=%s 两色混合像素=%d 唯一色=%d 板外色=%d"
          % (rel(args.input), rel(args.output),
             src.mode, stats["dither"], stats["mixed_pixels"], len(uniq), len(stray)))
    if stray:
        print("[FAIL] 输出含板外色：%s" % stray[:5])
        return 1
    print("[ok] 锁板成立；out sha256=%s" % sha256_file(args.output))
    return 0


def cmd_verify(args):
    palette = load_palette(args.palette)
    ok, _ = verify(palette)
    return 0 if ok else 1


def build_parser():
    p = argparse.ArgumentParser(description="全局调色板校验 / 预览 / 量化工具（确定性）")
    p.add_argument("--palette", default=DEFAULT_PALETTE, help="调色板 JSON（默认 Assets/Data/Palette/pirate_palette.json）")
    sub = p.add_subparsers(dest="cmd", required=True)

    sub.add_parser("check", help="校验板结构").set_defaults(func=cmd_check)
    sub.add_parser("oklch", help="打印 OkLCh 表").set_defaults(func=cmd_oklch)
    sub.add_parser("fmt", help="把板文件归一化为规范文本").set_defaults(func=cmd_fmt)

    p_strip = sub.add_parser("strip", help="出参考条带图")
    p_strip.add_argument("--out", default=os.path.join(REPO_ROOT, "docs", "images", "palette", "pirate_palette_strip.png"))
    p_strip.set_defaults(func=cmd_strip)

    p_q = sub.add_parser("quantize", help="把 PNG 量化到板上")
    p_q.add_argument("--in", dest="input", required=True)
    p_q.add_argument("--out", dest="output", required=True)
    p_q.add_argument("--dither", default="none", choices=["none", "bayer4", "bayer8"])
    p_q.add_argument("--weight", type=float, default=DEFAULT_WEIGHT, help="a/b 通道权重（默认 1.0）")
    p_q.set_defaults(func=cmd_quantize)

    p_s = sub.add_parser("sample", help="出样品像素贴图（口径样板 + 导入规则的门禁靶子）")
    p_s.add_argument("--out", default=os.path.join(REPO_ROOT, "pirate-crew", "Assets", "Art",
                                                   "Textures", "Pixel", "Diagnostics",
                                                   "PixelSpecSample_64.png"))
    p_s.set_defaults(func=cmd_sample)

    sub.add_parser("verify", help="自证：官方测试表 / 确定性 / 锁板 / 明度单调").set_defaults(func=cmd_verify)
    return p


def main(argv=None):
    args = build_parser().parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())
