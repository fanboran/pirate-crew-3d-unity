# -*- coding: utf-8 -*-
"""L3 整帧逐像素差分（相位对齐、rt0 级）比较器。

口径（对齐 external/sandcastle-ref/rt/reports/L2-run.md §1.2/§5）:
  第一口径 rt0 级（主判据）:
    我方 ours-rt0-prepost.raw（2560×1440 R16G16B16A16 f16 位流，后处理前的线性
    HDR 相机色彩缓冲，WaterLabColorDump 落盘）vs 原作 rt0(3149) raw（同格式）。
    原作侧水区掩码 = rt0(3073) vs rt0(3149) 位差（水面真实覆盖，2560 原生直接算）；
    沙区 = 其余（L2 §5 已证全帧无天空：45° 俯角+FOV20）。统计在线性域做
    （均值差/MAD/分位数 + histL1：256-bin 线性 [0,2] 裁剪域）；另附 sRGB8 编码域
    均值差用于 K 报告「水区 G 命中 / B+41 / 沙 B 差」结论的 rt0 级复核。
    行序自检：我方 raw 对原作 raw 正/翻两个方向的整帧 MAD 取小者（报告两种取值）。
  第二口径 swapchain 级（后处理链验证）:
    我方最终 PNG vs 原作 swapchain raw（2560×1440 RGBA8，行序按 L2 §5 的
    PNG 首行对照法对齐）；尺寸一致直接比，不一致沿用 BOX 面积缩放。

用法（Python = 交接档 §1.4 指定解释器，带 numpy/PIL）:
  python l3_rt0_diff.py [--ours-dir <capture 目录>] [--thresh 0.05] [--out <json>]
详细参数见 --help。输出: JSON（全帧/水区/沙区逐通道统计 + worst20 + 1/8 网格粗定位）
+ l3_rt0_masks.npy（2560 原生 water/sand 布尔掩码）+ stdout 表格。
"""
import argparse
import json
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
# worktree 根（temp/water-lab）与主仓根（external 所在）
WORKTREE_ROOT = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
MAIN_ROOT = os.path.abspath(os.path.join(HERE, '..', '..', '..', '..'))

W, H = 2560, 1440
EXP = os.path.join(MAIN_ROOT, 'external', 'sandcastle-ref', 'rt', 'dumps',
                   'rd-frames', 'exports')
REF_RAW_AFTER = os.path.join(EXP, 'draws', '3149_water', 'rt0_2564.raw.bin')
REF_RAW_BEFORE = os.path.join(EXP, 'draws', '3073_water', 'rt0_2564.raw.bin')
REF_SWAPCHAIN = os.path.join(EXP, 'swapchain.raw.bin')
REF_SWAPCHAIN_PNG = os.path.join(EXP, 'swapchain_2560x1440.png')

LIN_HIST_RANGE = (0.0, 2.0)   # 线性域 histL1 的裁剪窗（HDR 溢出裁边，报告内注明）
GRID = 8                       # 粗定位网格：屏幕 1/8 → 8×8 格
WORST_N = 20


def load_rt0(path):
    """rt0 raw 位流 → (uint16 位流, float32 线性值)，形状 (H, W, 4)。"""
    u16 = np.fromfile(path, dtype=np.uint16)
    if u16.size != W * H * 4:
        sys.exit(f'rt0 raw 尺寸不符：{path} = {u16.size} u16，预期 {W * H * 4}（{W}×{H}×4）')
    u16 = u16.reshape(H, W, 4)
    # f16 解码注意（L2-run §4.5 踩坑）：uint16 位流直接 view float16，不得经 f32 绕行
    f32 = u16.view(np.float16).astype(np.float32)
    return u16, f32


def load_swapchain(path):
    raw = np.fromfile(path, dtype=np.uint8)
    if raw.size != W * H * 4:
        sys.exit(f'swapchain raw 尺寸不符：{path} = {raw.size} B，预期 {W * H * 4}')
    return raw.reshape(H, W, 4)


def srgb8_encode(c):
    """线性 [0,∞) → sRGB 8bit（裁剪 0..1）。用于 K 结论复核的显示域均值。"""
    c = np.clip(c, 0.0, 1.0)
    out = np.where(c <= 0.0031308, c * 12.92,
                   1.055 * np.power(np.maximum(c, 1e-8), 1.0 / 2.4) - 0.055)
    return np.round(out * 255.0)


def hist_l1(a, b, lo, hi, bins=256):
    ha, _ = np.histogram(np.clip(a, lo, hi), bins=bins, range=(lo, hi))
    hb, _ = np.histogram(np.clip(b, lo, hi), bins=bins, range=(lo, hi))
    ha = ha.astype(np.float64) / max(1, ha.sum())
    hb = hb.astype(np.float64) / max(1, hb.sum())
    return float(np.abs(ha - hb).sum() / 2.0)


def channel_stats(ours, ref, prefix):
    """单通道成对统计：均值差（我方-原作）/MAD/分位数/histL1（线性 + sRGB8）。"""
    diff = ours - ref
    rec = {
        f'{prefix}_mean_ours': float(ours.mean()),
        f'{prefix}_mean_ref': float(ref.mean()),
        f'{prefix}_mean_diff': float(diff.mean()),
        f'{prefix}_mad': float(np.abs(diff).mean()),
        f'{prefix}_p50': float(np.percentile(np.abs(diff), 50)),
        f'{prefix}_p90': float(np.percentile(np.abs(diff), 90)),
        f'{prefix}_p99': float(np.percentile(np.abs(diff), 99)),
        f'{prefix}_max': float(np.abs(diff).max()),
        f'{prefix}_histL1_lin': hist_l1(ours, ref, *LIN_HIST_RANGE),
    }
    o8, r8 = srgb8_encode(ours.astype(np.float64)), srgb8_encode(ref.astype(np.float64))
    rec[f'{prefix}_srgb8_mean_ours'] = float(o8.mean())
    rec[f'{prefix}_srgb8_mean_ref'] = float(r8.mean())
    rec[f'{prefix}_srgb8_mean_diff'] = float((o8 - r8).mean())
    rec[f'{prefix}_srgb8_mad'] = float(np.abs(o8 - r8).mean())
    rec[f'{prefix}_histL1_srgb8'] = hist_l1(o8, r8, 0, 255)
    return rec


def region_stats(ours, ref, mask):
    """区域×通道统计（线性域主口径 + sRGB8 复核域）。mask=None = 全帧。"""
    if mask is not None:
        o, r = ours[mask], ref[mask]
        n = int(mask.sum())
    else:
        o, r = ours.reshape(-1, 4), ref.reshape(-1, 4)
        n = o.shape[0]
    if n == 0:
        return {'n': 0, 'note': '空区'}
    rec = {'n': n, 'domain': f'linear f16（histL1 窗 {LIN_HIST_RANGE}）'}
    for i, ch in enumerate('RGB'):
        rec.update(channel_stats(o[:, i].astype(np.float64), r[:, i].astype(np.float64), ch))
    # A 通道：原作 rt0 的 alpha 语义未定案，只记录均值不判读
    rec['A_mean_ours'] = float(o[:, 3].mean())
    rec['A_mean_ref'] = float(r[:, 3].mean())
    rec['A_note'] = 'A 通道语义未定案，仅记录不判读'
    return rec


def worst_pixels(ours, ref, mask, n=WORST_N):
    """|Δ| 按三通道最大值排序的 worst 像素 top-n（坐标 + 两边取值）。"""
    absd = np.abs(ours - ref)  # (H,W,4) float32
    score = absd[..., :3].max(-1)          # RGB 最大通道 |Δ|
    score = np.where(mask, score, -1.0)
    flat = score.ravel()
    idx = np.argpartition(flat, -n)[-n:]
    idx = idx[np.argsort(flat[idx])[::-1]]
    out = []
    for k in idx:
        y, x = divmod(int(k), W)
        rec = {
            'x': x, 'y': y,
            'score_maxdelta': float(flat[k]),
        }
        for i, ch in enumerate('RGB'):
            rec[f'ours_{ch}'] = float(ours[y, x, i])
            rec[f'ref_{ch}'] = float(ref[y, x, i])
            rec[f'delta_{ch}'] = float(ours[y, x, i] - ref[y, x, i])
        out.append(rec)
    return out


def grid_localize(ours, ref, mask, thresh):
    """差>阈值的连通域粗定位：屏幕 1/8 网格（8×8 格，每格 320×180 px）。"""
    absd = np.abs(ours - ref)
    bad = (absd[..., :3] > thresh).any(-1) & mask     # 任一通道超阈 & 有效域
    maxd = absd[..., :3].max(-1)
    cells = []
    ch, cw = H // GRID, W // GRID
    for gy in range(GRID):
        for gx in range(GRID):
            sl = (slice(gy * ch, (gy + 1) * ch), slice(gx * cw, (gx + 1) * cw))
            n_bad = int(bad[sl].sum())
            cells.append({
                'cell': f'[gy{gy},gx{gx}]',
                'px': [gx * cw, gy * ch, (gx + 1) * cw - 1, (gy + 1) * ch - 1],
                'bad_px': n_bad,
                'bad_frac': float(n_bad / (ch * cw)),
                'mean_maxdelta': float(maxd[sl].mean()),
                'p99_maxdelta': float(np.percentile(maxd[sl], 99)),
            })
    cells.sort(key=lambda c: -c['bad_frac'])
    hot = [c for c in cells if c['bad_frac'] >= 0.2]
    return cells[:12], hot, float(bad.sum()), float(thresh)


def detect_row_order(ours, ref, finite):
    """我方 raw 行序自检：对原作 raw 正/翻两方向整帧 MAD，取小者。"""
    m = finite[..., None]
    d0 = float(np.abs((ours - ref) * m).sum() / max(1, m.sum() * 3))
    d1 = float(np.abs((ours[::-1] - ref) * m).sum() / max(1, m.sum() * 3))
    if d1 < d0:
        return ours[::-1], True, d0, d1
    return ours, False, d0, d1


def swapchain_compare(ours_png_path, out):
    """第二口径：我方最终 PNG vs 原作 swapchain raw（后处理链验证，显示域 8bit）。"""
    raw = load_swapchain(REF_SWAPCHAIN)
    png = np.asarray(Image.open(REF_SWAPCHAIN_PNG).convert('RGB'))
    # 行序按 L2 §5 的首行对照法（raw 导出顺序 vs PNG 判定，确定性强）
    if np.abs(raw[0, :, :3].astype(int) - png[0].astype(int)).mean() > \
       np.abs(raw[-1, :, :3].astype(int) - png[0].astype(int)).mean():
        raw = raw[::-1]
        out['swapchain_row_flip'] = True
    sc = raw[:, :, :3].astype(np.float64)

    ours = np.asarray(Image.open(ours_png_path).convert('RGB')).astype(np.float64)
    th, tw = ours.shape[:2]

    # 掩码：2560 原生水/沙；尺寸不一致时 BOX 缩到对方尺寸（沿用 L2 §5 手法）
    u16b, _ = load_rt0(REF_RAW_BEFORE)
    u16a, _ = load_rt0(REF_RAW_AFTER)
    wm = (u16b != u16a).any(-1)
    if (th, tw) != (H, W):
        sc_img = Image.fromarray(sc.astype(np.uint8)).resize((tw, th), Image.BOX)
        sc = np.asarray(sc_img).astype(np.float64)
        wm_img = Image.fromarray(wm.astype(np.uint8) * 255).resize((tw, th), Image.BOX)
        wm = np.asarray(wm_img) >= 128
        out['swapchain_scale'] = f'2560x1440 BOX -> {tw}x{th}'
    sand = ~wm

    rec = {}
    for name, msk in (('water', wm), ('sand', sand), ('full', None)):
        rec[name] = _display_region(sc, ours, msk)
    out['swapchain'] = rec


def _display_region(ref, ours, mask):
    """显示域 8bit 区域统计（L2-run §5 表式：均值差/MAD/histL1）。"""
    if mask is not None:
        a, b = ref[mask], ours[mask]
        n = int(mask.sum())
    else:
        a, b = ref.reshape(-1, 3), ours.reshape(-1, 3)
        n = a.shape[0]
    if n == 0:
        return {'n': 0, 'note': '空区'}
    rec = {'n': n, 'domain': 'sRGB 8bit'}
    for i, ch in enumerate('RGB'):
        diff = b[:, i] - a[:, i]
        rec[ch] = {
            'mean_ours': float(b[:, i].mean()),
            'mean_ref': float(a[:, i].mean()),
            'mean_diff': float(diff.mean()),
            'mad': float(np.abs(diff).mean()),
            'histL1': hist_l1(a[:, i], b[:, i], 0, 255),
        }
    return rec


def main():
    ap = argparse.ArgumentParser(description='L3 rt0 级整帧逐像素差分（相位对齐）')
    capdir = os.environ.get('PC3D_CAPTURE_DIR') or os.path.join(
        WORKTREE_ROOT, 'export', 'waterlab-l3-rt0')
    ap.add_argument('--ours-dir', default=capdir, help='我方 capture 输出目录（含 ours-rt0-prepost.raw / battle-hud-1.png）')
    ap.add_argument('--ours-raw', default=None, help='我方 rt0 raw（默认 <ours-dir>/ours-rt0-prepost.raw）')
    ap.add_argument('--ours-png', default=None, help='我方最终 PNG（默认 <ours-dir>/battle-hud-1.png）')
    ap.add_argument('--ref-raw', default=REF_RAW_AFTER, help='原作 rt0(3149) raw')
    ap.add_argument('--ref-raw-before', default=REF_RAW_BEFORE, help='原作 rt0(3073) raw（水掩码位差基线）')
    ap.add_argument('--thresh', type=float, default=0.05, help='网格粗定位阈值（线性域，默认 0.05）')
    ap.add_argument('--out', default=None, help='输出 JSON（默认 <ours-dir>/l3_rt0_diff.json）')
    args = ap.parse_args()

    ours_raw_path = args.ours_raw or os.path.join(args.ours_dir, 'ours-rt0-prepost.raw')
    ours_png_path = args.ours_png or os.path.join(args.ours_dir, 'battle-hud-1.png')
    out_path = args.out or os.path.join(args.ours_dir, 'l3_rt0_diff.json')

    for p in (ours_raw_path, args.ref_raw, args.ref_raw_before):
        if not os.path.exists(p):
            sys.exit(f'缺输入文件：{p}')

    print(f'我方 rt0 : {ours_raw_path}')
    print(f'原作 rt0 : {args.ref_raw}（水掩码位差基线 {args.ref_raw_before}）')

    _, ours = load_rt0(ours_raw_path)
    u16_ref, ref = load_rt0(args.ref_raw)
    u16_before, _ = load_rt0(args.ref_raw_before)

    out = {
        'ours_raw': os.path.abspath(ours_raw_path),
        'ref_raw': os.path.abspath(args.ref_raw),
        'water_mask_source': 'rt0(3073) vs rt0(3149) 位差（2560 原生）',
        'thresh': args.thresh,
    }

    # NaN/Inf 审计（L2 §1.2：原作 rt0 实测无 NaN 位型；我方 HDR 溢出会现 Inf）
    finite = np.isfinite(ours).all(-1) & np.isfinite(ref).all(-1)
    out['nonfinite'] = {
        'ours_px': int((~np.isfinite(ours).all(-1)).sum()),
        'ref_px': int((~np.isfinite(ref).all(-1)).sum()),
        'note': '统计只在两边均有限的像素上做',
    }
    print(f'非有限像素：我方 {out["nonfinite"]["ours_px"]} / 原作 {out["nonfinite"]["ref_px"]}')

    # 水掩码（位精确比较，不受 NaN 影响）
    wm = (u16_before != u16_ref).any(-1)
    sand = ~wm
    out['water_px'] = int(wm.sum())
    out['water_frac'] = float(wm.mean())
    print(f'水区（位差掩码）：{out["water_px"]} px（{out["water_frac"] * 100:.1f}%）')
    np.save(os.path.join(os.path.dirname(out_path), 'l3_rt0_masks.npy'),
            np.stack([wm, sand], -1))

    # 行序自检
    ours, flipped, d0, d1 = detect_row_order(ours, ref, finite)
    out['row_order'] = {'flipped': flipped, 'mad_identity': d0, 'mad_flipped': d1}
    print(f'行序自检：{"翻转" if flipped else "原序"}（MAD 正/翻 = {d0:.5f} / {d1:.5f}）')

    # rt0 口径统计（线性域 + sRGB8 复核域）
    rec = {}
    rec['water'] = region_stats(ours, ref, wm & finite)
    rec['sand'] = region_stats(ours, ref, sand & finite)
    rec['full'] = region_stats(ours, ref, finite)
    out['rt0'] = rec

    for name in ('water', 'sand', 'full'):
        r = rec[name]
        if r.get('n', 0) == 0:
            continue
        print(f'\n## rt0 线性域 · {name}（n={r["n"]}）')
        print('通道 | mean_ref | mean_ours | Δlin | MAD | histL1 | Δsrgb8 | MADsrgb8')
        for ch in 'RGB':
            print('%s | %.4f | %.4f | %+.4f | %.4f | %.3f | %+.1f | %.1f' % (
                ch, r[f'{ch}_mean_ref'], r[f'{ch}_mean_ours'], r[f'{ch}_mean_diff'],
                r[f'{ch}_mad'], r[f'{ch}_histL1_lin'],
                r[f'{ch}_srgb8_mean_diff'], r[f'{ch}_srgb8_mad']))

    # worst20 + 1/8 网格粗定位
    out['worst20'] = worst_pixels(ours, ref, finite)
    cells, hot, bad_total, thresh = grid_localize(ours, ref, finite, args.thresh)
    out['grid'] = {'thresh': thresh, 'bad_px': bad_total,
                   'bad_frac': bad_total / max(1, int(finite.sum())),
                   'cells_top12': cells, 'cells_ge_02': hot}
    print(f'\nworst {WORST_N} 像素（RGB 最大通道 |Δ|）：')
    for p in out['worst20'][:5]:
        print(f'  ({p["x"]},{p["y"]}) score={p["score_maxdelta"]:.3f} '
              f'ours=({p["ours_R"]:.3f},{p["ours_G"]:.3f},{p["ours_B"]:.3f}) '
              f'ref=({p["ref_R"]:.3f},{p["ref_G"]:.3f},{p["ref_B"]:.3f})')
    print(f'\n1/8 网格粗定位（阈值 {thresh}，超阈像素 {bad_total}，'
          f'占比 {out["grid"]["bad_frac"] * 100:.1f}%），top12：')
    for c in cells[:8]:
        print(f'  {c["cell"]} px={tuple(c["px"])} bad={c["bad_frac"] * 100:.1f}% '
              f'mean={c["mean_maxdelta"]:.3f} p99={c["p99_maxdelta"]:.3f}')

    # K 结论复核摘要（sRGB8 域均值差——直接对照 L2-run §5 / K 报告的 +41/G 命中读法）
    k = {}
    for name in ('water', 'sand'):
        if rec[name].get('n', 0) == 0:
            continue
        k[name] = {ch: rec[name][f'{ch}_srgb8_mean_diff'] for ch in 'RGB'}
    out['k_recheck_srgb8_mean_diff'] = k
    print('\nK 结论 rt0 级复核（sRGB8 编码域均值差，我方-原作）：')
    print('  水: R%+.1f G%+.1f B%+.1f   沙: R%+.1f G%+.1f B%+.1f' % (
        k['water']['R'], k['water']['G'], k['water']['B'],
        k['sand']['R'], k['sand']['G'], k['sand']['B']))

    # 第二口径：最终 PNG vs swapchain（后处理链验证）
    if os.path.exists(ours_png_path):
        swapchain_compare(ours_png_path, out)
        sc = out['swapchain']
        for name in ('water', 'sand', 'full'):
            r = sc[name]
            if r['n'] == 0:
                continue
            print(f'\n## swapchain 口径 · {name}（n={r["n"]}，{r["domain"]}）')
            print('通道 | mean_ref | mean_ours | Δ | MAD | histL1')
            for ch in 'RGB':
                c = r[ch]
                print('%s | %.1f | %.1f | %+.1f | %.1f | %.3f' % (
                    ch, c['mean_ref'], c['mean_ours'], c['mean_diff'], c['mad'], c['histL1']))
    else:
        out['swapchain'] = {'note': f'缺我方 PNG（{ours_png_path}），第二口径跳过'}
        print(f'\n（缺 {ours_png_path}，swapchain 第二口径跳过）')

    with open(out_path, 'w', encoding='utf8') as f:
        json.dump(out, f, ensure_ascii=False, indent=1)
    print(f'\nJSON → {out_path}；掩码 → {os.path.join(os.path.dirname(out_path), "l3_rt0_masks.npy")}')


if __name__ == '__main__':
    main()
