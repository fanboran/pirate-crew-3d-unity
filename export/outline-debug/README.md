# 描边调试截图（M2 单位悬停/选中描边）

> 本目录是 AGENTS.md「图形学调试截图规范」要求的产物页：**每张图 + 应看到什么 + 怎么复现**。
> 调参指南（参数含义/区间/常见症状速查）在 [docs/描边Shader调试.md](../../docs/描边Shader调试.md)，
> 本文只管"这批图是什么、说明了什么"。
>
> 采集时间：2026-09-13；采集方式：图形界面编辑器 + play mode（MCP `manage_camera` / `execute_menu_item`）。
> 采集时编辑器分辨率 2560×1440，正交相机（OrthographicSize = 10），整关可见。

## 文件清单

| 文件 | 是什么 | 应看到什么 |
| --- | --- | --- |
| `debug-mode-0-normal.png` | `_DebugMode=0` 正常合成 | 单位本体（红队暗红/蓝队蓝）+ 轮廓外一圈**青色虚线**描边。采集时用 `UnitOutlineBinder.DebugForcedState=2` 把全场单位强制为"选中态"，所以**每个单位都有描边**；正常游玩时只有被选中/悬停的那一个会有 |
| `debug-mode-1-base-only.png` | `_DebugMode=1` 只显示本体 | 单位本体正常着色，**完全没有描边**（描边 Pass 整段 `discard`） |
| `debug-mode-2-expanded-hull.png` | `_DebugMode=2` 只显示法线外扩壳 | 本体消失，单位位置变成**纯品红实心块**（外扩后的背面壳）。此处是**实心剪影而不是"一圈环"**——对实心凸体（本例是 Cube），背面壳的投影并集就是整个剪影，环要等本体 Pass 盖住中间才看得出来 |
| `debug-mode-3-outline-mask.png` | `_DebugMode=3` 只显示描边掩码 | 单位位置是**不透明、不流动的纯青色实心剪影**（忽略虚线、忽略 hover 半透明）。同为实心，原因同档 2 |
| `debug-mode-4-depth-normal-raw.png` | `_DebugMode=4` 深度/法线原始数据 | 单位本体换成数据可视化：**R=世界法线.x 映射、G=世界法线.y 映射、B=视空间深度**（越远越亮，50m 封顶）。正交相机在 z=-10、单位在 z=0，故 B≈0.2；正面法线朝相机 → R=G≈0.5。本图无描边 |
| `01-live-selection-cyan-outline.png` | **真实选中通路**（非强制档） | 画面里**只有 1 个单位**带青色虚线描边，其余同名单位没有——证明 `选中角色 → BattleTeam.Select → PirateBase.SetSelected → UnitOutlineBinder → MaterialPropertyBlock → shader` 整条链接通 |
| `00-evidence-shader-compile-error-magenta.png` | **故障证据**（着色器编译失败） | 全场单位呈**品红**（URP 的错误材质），HUD/地形正常。对应 `PirateOutline.shader` 只 include `GlobalIllumination.hlsl` 导致 `unrecognized identifier 'BRDFData'` 的那次事故，详见 [docs/描边Shader调试.md](../../docs/描边Shader调试.md) §八 |

## 程序化判据（不是"看图觉得对"）

对每张图做一次颜色普查（2560×1440 全图扫描），结果与上表的预期逐条吻合：

| 图 | 青色系像素（`g-r>60 && b-r>60 && g>150`） | 品红像素（`r>200 && g<80 && b>200`） |
| --- | --- | --- |
| `debug-mode-0-normal` | **1188**（全部落在单位带 y∈(500,900)） | 0 |
| `debug-mode-1-base-only` | **0** | 0 |
| `debug-mode-2-expanded-hull` | 0 | **5346** |
| `debug-mode-3-outline-mask` | **5346** | 0 |
| `debug-mode-4-depth-normal-raw` | 0 | 0 |

同一单位区域（x 1255–1305, y 675–730）在各档的主色：

| 图 | 该区域主色 |
| --- | --- |
| 0 | 本体 `(161,63,62)` + 描边 `(69,205,203)` |
| 1 | 本体 `(161,63,62)`，无描边色 |
| 2 | 品红 `(255,0,255)`（本体已 discard） |
| 3 | 青 `(73,215,211)`（本体已 discard） |
| 4 | 数据色 `(127,127,50)`（≈ 法线 0.5/0.5 + 深度 0.2） |

> 注意：用宽松容差（如 `|r-150|<45` 等）扫"青色"会把 HUD 里浅灰/淡蓝文字的抗锯齿像素算进来（实测 451 px 假阳性）。
> 判据要带**色相**条件（`g-r>60 && b-r>60`）才能区分真实的青色描边与灰白文字。

## 复现方式

1. 用图形界面编辑器打开 `pirate-crew/`（**不要** `-batchmode -nographics`，那样没有渲染路径）。
2. 打开 `Assets/Scenes/Battle.unity`，进 play mode，**确认没有暂停**（暂停时画面不重绘，采集会明确报错退出）。
3. 菜单 **`PirateCrew/Rendering/采集描边调试截图`** → 5 张图写到本目录（采集脚本会先把全场单位强制为选中态，结束后复位）。
4. 想单独看"真实选中通路"：play mode 下点选一个己方角色后自行截图即可（本目录的 `01-*` 就是这么来的）。

## 已知观感问题（留给观感验收，未擅自改美术）

- **虚线偏碎**：`_DashFrequency = 50`（Godot 默认值）在 NDC 上是每 ~0.126 个 NDC 一个周期，
  1440p 下约 45px 实线 + 45px 空白；而单位在屏幕上只有约 27×36px，周长约 126px，
  于是描边看起来是"两三段短线"而不是完整一圈。**这是相机取景太远（整关可见）+ 单位太小的合成结果**，
  不是描边失效。要改观感有两个独立杠杆：调小 `_DashFrequency`（更长虚线）、或让战斗相机拉近（`BattleCameraController` / 正交尺寸）。
  这两条都涉及美术取向，**由观感验收决定，本次未擅自改**。
- 单位材质换成 `PirateOutline` 后**没有 ShadowCaster Pass**，单位不投影；本体着色也从 URP/Lit 换成了
  shader 内的简单 Lambert + SH。若观感验收要求阴影/更丰富的光照，见 `docs/描边Shader调试.md` §七-2。
