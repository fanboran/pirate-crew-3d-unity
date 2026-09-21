# 等距像素卡通 · 开工序列第 1 步实拍（r1）

> **这批图是什么**：渲染篇 [§8 实施顺序](../../../技术/渲染/渲染管线-等距像素卡通.md) 第 1 步
> （正交相机切换 + 像素化 RendererFeature）的播放器实拍验收。出图时间 2026-09-21，
> 提交基线见本目录同级提交。

## 本步改了什么

- **正交相机**：Battle vcam 与回退 Camera 均切 Orthographic（`ModeOverride: 1` + `OrthographicSize: 17`）；
  俯角统一 45°、Transposer 距离恒 30、旋转锁死（环绕输入退役，观察模式/中键自由锚保留为调试出口）。
  档位语义迁移到 OrthoSize：特写 5 / 全场 17 / 全景 clamp(span×0.3, 17, 60) / 滚轮 [3,60] 整数档。
  Scope/推近/旁观等 FOV 特效以当量比率映射到 size（`size = 手动档 × fov当量 / 60`）。
- **全屏像素化**：`PixelationRendererFeature`（两档 Renderer 均挂载），`AfterRenderingPostProcessing`
  抓相机颜色 → nearest blit 到高 360 的 Point RT（宽随屏幕宽高比，16:9 即 640×360）→ nearest blit
  放大回屏幕。blit 材质用 URP 自带 `Hidden/Universal/CoreBlit` 的 **pass 0 "Nearest"**。
- **URP 全局设置**（两档）：HDR 关、MSAA 1x、主光阴影关、附加光 0。

## 每张图应看到什么

| 图 | 预期 |
| --- | --- |
| battle-45.jpg | 主机位：45° 俯视等距构图 + 3D 画面全部 3×3 屏幕像素块；HUD 不块化 |
| unit-closeup.jpg | 特写档（size 5）：单位约 67 RT 像素高，剪影/描边可辨 |
| arena-overview.jpg | 全场档观感：整场构图，正交无透视收缩 |
| hud-weaponpanel.jpg | UI（Overlay Canvas）全分辨率锐利，与像素化 3D 同框——红线 2 的直观证据 |
| sea-shore.jpg | 海面在像素化下的块感（水 shader 未翻新，属步骤 3 资产域） |
| terrain-high.jpg | 高机位地形块感 |

## 程序化判据（AGENTS 判图纪律）

- **块化判据**：1920×1080 图逐 3×3 块与块均值差的绝对值平均。旧图（未像素化时代
  `docs/images/art-review/r12-l1/`）实测 **2.52–2.80**；本批 3D 画面图 **0.00**（完美块化），
  含 HUD 混合图 0.59–1.20（UI 区不块化=红线 2 满足）。复现：
  `python -c "..."`（块内方差 numpy 实现，见会话记录；判据先以旧图校准再判定）。
- **洋红 = 0**：11 张全部 0 像素（无材质缺失）。
- **shader error = 0**：播放器构建日志 grep 仅 3 条存量 warning（PirateOutlinePost 循环梯度 /
  PirateOcean pow 负数提示，均为 d3d11 编译器提示、非本次引入）。

## 已知事项

- 首版自写 `Hidden/PirateCrew/PixelBlit` 在播放器真实 GPU 会话报
  `not supported on this GPU (none of subshaders/fallbacks are suitable)`（编辑器与构建期均不暴露），
  已删除并换 URP 自带 CoreBlit pass 0——教训入 [交接与恢复指南](../../../项目/交接与恢复指南.md) §32。
- 阴影已全局关闭，单位脚下暂无 blob shadow（现有 `ContactShadowDecal` 未全量接线）——
  落地感由步骤 2（赛璐璐+反向壳描边试点）一并处理。
- 本批图是「第 1 步出整体观感」：材质/纹理仍是旧 PBR/平涂资产，像素化兜底全局观感
  （渲染篇 §3 的演进策略）；资产翻新走 [像素纹理资产管线](../../../技术/资产管线/像素纹理资产管线.md)。

## 复现

```bash
# 1) 安装 Feature（幂等，两档渲染器）
Unity.exe -batchmode -nographics -quit -projectPath pirate-crew \
  -executeMethod PirateCrew.EditorTools.PixelationInstaller.Install -logFile -
# 2) 四步装配链重烘场景（M2BattleSceneSetup.BuildAll → HudMinimapSceneSetup.WireMinimap
#    → M3SceneSetup.BuildAll → WorldMapAssetSetBuilder.BuildAll）
# 3) 构建播放器（grep "shader error" 必须 0）
# 4) external/build/PirateCrew3D.exe -artReviewOut export/pixel-review/r<N>
```

原始 PNG（1920×1080）在 `export/pixel-review/r1/`（不入库，本地留存）。
