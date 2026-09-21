# tools/blender/pixel —— 像素化导出模板（平滑法线 / 量化 / 纹素密度）

> **完整用法、参数、实测口径与版本差异见**
> [docs/技术/资产管线/Blender导出模板使用说明.md](../../../docs/技术/资产管线/Blender导出模板使用说明.md)。
> 本文件只做目录导航，避免两处文档各自漂移。

| 文件 | 作用 |
| --- | --- |
| `pixel_export.py` | **模块**（被 kit 脚本 import）：`bake_smooth_normals_to_vertex_colors` / `quantize_vertex_colors` / `check_texel_density` / `assert_texel_density` / `unwrap_and_lock_uv_density` / `export_fbx` / `load_palette_lab` / `oklab_selftest` |
| `step_asset.py` | **CLI**：单件资产过一遍三道工序并导出（`--self-test` 跑正反对照自证判据） |
| `check_worldkit_density.py` | **CLI**：对整个目录批量跑密度体检，出人读表格 + JSON 报告 |

## 两行复现

```bash
# 判据自证（不需要任何真实资产；正对照 + 两个负对照）
"F:/SteamLibrary/steamapps/common/Blender/blender.exe" -b --factory-startup \
    -P tools/blender/pixel/step_asset.py -- --self-test

# 全量密度体检（47 件 WorldKit + 报告）
"F:/SteamLibrary/steamapps/common/Blender/blender.exe" -b --factory-startup \
    -P tools/blender/pixel/check_worldkit_density.py -- \
    --root pirate-crew/Assets/Art/Models/WorldKit --tex-size 64
```

## 三条硬边界（改代码前先看

1. **顶点色两个用途互斥**：`SmoothNormal`（GBA=平滑法线、R=阈值偏移）是**数据**，禁止量化；
   `Col` 是平涂色，才可以上板。量化器对前者直接抛错。
2. **`colors_type` 必须 `SRGB`**（Blender 5.2 实测：`LINEAR` 会把数据通道强行线性化、`NONE` 会丢顶点色）。
3. **判据正反对照跑过才算数**（任务书 §9 判据三律）：`--self-test` 必须 PASS，否则批量体检结果不予采信。
