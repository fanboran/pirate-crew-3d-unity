# 中文字体样张（像素中文字体选型）

> **这份目录是什么**：UI 换装后"中文字体用哪款"的**对照样张**与推荐。
> 样张摄于 3:1 时代（12px 原生 + 最近邻放大 3×）；**现行显示口径为 2:1 恒定像素密度**，
> 裁决口径见 [架构总览 §8.1](../../技术/架构/架构总览.md)。底纹与配色取自 UI 面板。

## [`font-compare-1x.png`](font-compare-1x.png)

| 行 | 字体 | 许可 | 说明 |
| --- | --- | --- | --- |
| 1 | **缝合像素字体 Fusion Pixel 12px（比例版，简体）** | OFL-1.1 | **推荐**：颗粒与像素 UI 完全同频；笔画清楚、标点规整；简体字库完整（含常用汉字与拉丁数字） |
| 2 | 最像素 Zpix 12px | OFL-1.1 | 备选：更窄更密，一行塞字更多；小字号下个别笔画偏挤 |
| 3 | 当时现状：LXGW 文楷 Lite 36px（非像素） | OFL-1.1 | 选型时对照：细笔画矢量字，与像素 UI 的颗粒语言不搭 |

## 落地（现行四档，创始人裁决「原生档纪律」）

- **四档原生字体**：16 = **正格点黑16**（`ZhengGeDianHei16.ttf`，github.com/yzdnn/ZhengGeDianHei-16，
  OFL，简体 0 缺字）；12/10/8 = **缝合像素**（`FusionPixel12/10/8-zh_hans.ttf`，github.com/TakWolf/
  fusion-pixel-font，OFL；12 缺 1 字"毂"←10 兜、10 缺 2 字"胫舭"←8 兜，回退链已烘进资产）。
  淘汰记录：方舟 16px（缺 1413 字含巨火扫）、寒蝉 16px（简体约 40%）、LXGW 文楷（非像素）。
- **位图口径**：samplingPointSize = 原生档、`GlyphRenderMode.RASTER_HINTED`（不用 SDFAA，
  像素字形会糊出灰边）、atlasPadding=0、图集 Point、材质 `TextMeshPro/Bitmap`。
  烘制入口 `FontAssetBuilder.ForceRebuildAll`（遥控桥 `fonts` 命令）。
- **纪律**：只取原生设计档——**没有的档不硬凑、绝不放大**（禁翻倍）；**禁伪粗**
  （TMP 合成加粗在位图上 = 重影乱码，已全局禁用）；字号是独立体系，不锚定艺术像素。
  运行时按字号就近取档：`UiKit.ResolvePixelFont(字号, 族)`。

## 版权

现役字体均为 **OFL-1.1**（可随游戏分发；许可文件在 `Assets/Art/Fonts/Licenses/`）。
