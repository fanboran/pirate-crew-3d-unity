# 中文字体样张（像素中文字体选型）

> **这份目录是什么**：UI 换装后"中文字体用哪款"的**对照样张**与推荐。
> 样张按 **3× 实际显示口径**渲染：像素字体 12px 原生 + 最近邻放大 3×
> （= 1080p 下游戏里的真实观感）；底纹与配色取自 UI 面板（板岩 `#1E252F` + 暖白字）。

## [`font-compare-1x.png`](font-compare-1x.png)

| 行 | 字体 | 许可 | 说明 |
| --- | --- | --- | --- |
| 1 | **缝合像素字体 Fusion Pixel 12px（比例版，简体）** | OFL-1.1 | **推荐**：颗粒与 3× UI 完全同频；笔画清楚、标点规整；简体字库完整（含常用汉字与拉丁数字） |
| 2 | 最像素 Zpix 12px | OFL-1.1 | 备选：更窄更密，一行塞字更多；小字号下个别笔画偏挤 |
| 3 | 现状：LXGW 文楷 Lite 36px（非像素） | OFL-1.1 | 现状对照：细笔画矢量字，与像素 UI 的颗粒语言不搭 |

## 落地（已照此实施，2026-09-22）

1. TTF 与 OFL 已入库：`Assets/Art/Fonts/FusionPixel12-zh_hans.ttf` +
   `Assets/Art/Fonts/Licenses/FusionPixel12-OFL.txt`；
   `FontAssetBuilder` 已加**位图口径档**：动态图集 + `GlyphRenderMode.RASTER_HINTED`
   （**不用 SDFAA**，像素字形会糊出灰边）+ 12px 原生采样 + 图集 Point 过滤 +
   `TextMeshPro/Bitmap` 材质。产物 `FusionPixel12-px`（Art 与 Resources 各一份），
   缺字 fallback → 楷体正文。
2. 显示字号**必须是 12 的整数倍**（36 = 3×12 = 12 艺术像素 @3×，48 = 4×12，72 = 6×12）；
   非整数倍会把 1 艺术像素拉宽/压窄，直接破坏 3:1 颗粒。
3. 已接入：组件展示实机窗口（`UIShowcase.unity` → `PixelShowcasePage`，整页文字 =
   像素字体）。**全局切换**（`UiKit.RuntimeFont` 三档全换 + `UiSkin.Font` 字号表改到
   12 的整数倍栅格）已登记在待办 4c——那一步会牵动所有屏的布局，需要逐屏过。

## 版权

两款候选字体均为 **OFL-1.1**（可随游戏分发；保留许可文件即可）。TTF 只放本地工作区
`external/font-ref/`（不入库），入库时把 TTF 与 OFL 文本一并放进 `Assets/Art/Fonts/`。
