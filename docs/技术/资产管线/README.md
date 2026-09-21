# 技术/资产管线

> 资产生产域文档：像素纹理规格、全局调色板与量化工具链、导入约定、Blender 导出模板。
> 归属「烘焙器岗位」（[管线合并任务书](../架构/管线合并-糖豆人式资产架构任务书.md) 三层分离不变）。

## 规格与口径（先读这两篇）

| 文档 | 说明 |
| --- | --- |
| [像素纹理资产管线.md](像素纹理资产管线.md) | **现行资产生产口径**：纹素密度 / 量化流程 / 导入设置 / Blender 适配 / 调色板纪律 / 资产翻新策略 |
| [调研-调色板量化与纹素密度.md](调研-调色板量化与纹素密度.md) | 量化调研：OkLab 锁板映射、Yliluoma 任意板抖动、密度校验公式、BC7 除名裁决、行业先例（已核 17 来源） |

## 操作手册（按角色）

| 文档 | 谁读它 | 说明 |
| --- | --- | --- |
| [调色板与量化手册.md](调色板与量化手册.md) | 美术 / 管线工程师 / 任何改板的人 | 59 色板的**族结构与两条结构规则**；唯一真源（JSON）与 Unity 镜像的一致性路径与三条对账命令；OkLab 加权最近邻与 ordered-only 抖动的**口径**（含两个实测踩过的坑）；`tools/palette/palette_tool.py` 全部子命令；确定性证据（sha256）与验收判据 |
| [像素纹理导入规范.md](像素纹理导入规范.md) | 把贴图放进工程的人 | 像素纹理的**唯一判定规则**（约定目录 `Assets/Art/Textures/Pixel/**`）与为什么不用后缀；导入五项设置逐条理由（含 `AlphaIsTransparency` 这个隐藏坑）；为什么必须是 AssetPostprocessor 而不是一次性脚本；EditMode 门禁用例的位置与理由；新增资产的接入步骤与自查清单 |
| [Blender导出模板使用说明.md](Blender导出模板使用说明.md) | 写 kit 脚本的人 | `tools/blender/pixel/` 三件事（平滑法线烘顶点色 / 顶点色量化 / 纹素密度校验）的用法、参数、退出码；**顶点色两个互斥用途**的硬边界；判据的硬失败 vs 只计数口径；正反对照自证；Blender 5.2 的两条版本差异（`colors_type=SRGB`、没有 "Normals Only"）；实测发现（49 件 FBX 无 UV） |
| [像素海面技术方案.md](像素海面技术方案.md) | M2f 实施者 | `PirateOcean` 退役后海面用什么替代：三层设计（色带波纹 / 量化泡沫 / 岸线带）、与 ppm 密度方程的耦合（决定 tile 尺寸）、岸距用高度场解析算而**不用深度图**、7 步实现清单与风险；**刻意不落半成品 shader** |
| [泰拉瑞亚参照资产提取.md](泰拉瑞亚参照资产提取.md) | 做 UI 像素化、需要风格参照的人 | 参照从哪来：**wiki 优先**（成就图标每张独立 64×64 PNG，一把抓完 137 张），wiki 不提供的 UI 原始件走 **XNB 解包**（41 件）。含实测的 XNB 字段布局、两个卡了很久的坑（7 位变长整数是**小端**；版本后那 2 字节必须先跳）、自校验判据、解码器选型（libmspack 的 lzxd 解不了，要用 MonoGame 的 LzxDecoderStream） |

## 与其它域的交界

| 主题 | 去哪 |
| --- | --- |
| 渲染期色彩口径（色带 / 暗部 / 抖动 / 描边） | [渲染管线-等距像素卡通.md](../渲染/渲染管线-等距像素卡通.md) §4/§5 |
| 美术方向与调色板策略 | [美术风格指南.md](../../设计/美术风格指南.md) §3.2 |
| 写实栈退役的逐项依据（后处理 / Depth / Opaque / Renderer Feature） | [Assets/Art/Rendering/README.md](../../../pirate-crew/Assets/Art/Rendering/README.md) |
| 里程碑归属与裁决点 | [美术翻新-等距像素卡通立项任务书.md](../美术翻新-等距像素卡通立项任务书.md) |

## 本域的文件边界

| 域 | 路径 |
| --- | --- |
| 板与工具 | `pirate-crew/Assets/Data/Palette/`、`tools/palette/`、`tools/blender/pixel/` |
| 渲染资产 | `pirate-crew/Assets/Art/`、`pirate-crew/Assets/Settings/` |
| 美术向编辑器脚本 | `pirate-crew/Assets/Editor/Art/`、`pirate-crew/Assets/Editor/` 下的美术渲染向脚本 |
| 本域测试 | `pirate-crew/Assets/Art/Tests/`（理由见[导入规范](像素纹理导入规范.md) §4.1） |
