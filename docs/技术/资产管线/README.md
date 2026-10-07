# 技术/资产管线

> 资产生产域文档：像素纹理规格与导入落地、全局调色板与量化工具链、Blender 导出模板、UI 九宫格。
> 归属「烘焙器岗位」（[架构总览 §8.2](../架构/架构总览.md) 三层分离不变）。

## 规格与口径

| 文档 | 说明 |
| --- | --- |
| [像素纹理.md](像素纹理.md) | **现行资产生产口径**：纹素密度 / 尺寸 / 导入落地（目录判定规则、导入五项设置、AssetPostprocessor 强制、EditMode 门禁、新资产接入步骤）；§4/§5 量化链已随 锁板定案取消、保留存档 |
| [调研.md](调研.md) | 量化调研：OkLab 锁板映射、Yliluoma 任意板抖动、密度校验公式、BC7 除名定案、行业先例（已核 17 来源） |
| [UI九宫格.md](UI九宫格.md) | **UI 换装的实现口径**：九段几何（u 基本单位，现役 3px）、七 tone 令牌派生、九宫格切片契约、与参照的逐段实测对照（UI 像素参照档案见 [images/ui-pixel-ref](../../images/ui-pixel-ref/README.md)） |

## 操作手册（按角色）

| 文档 | 谁读它 | 说明 |
| --- | --- | --- |
| [调色板.md](调色板.md) | 美术 / 管线工程师 / 任何改板的人 | 68 槽板的**族结构与阶梯族规则**（现役职能 = 取色令牌真源，限色机制已取消）；唯一真源（JSON）与 Unity 镜像的一致性路径与三条对账命令；`tools/palette/palette_tool.py` 全部子命令；量化工具链存档（已退出生产链）与确定性证据（sha256） |
| [Blender导出.md](Blender导出.md) | 写 kit 脚本的人 | `tools/blender/pixel/` 三件事（平滑法线烘顶点色 / 顶点色量化 / 纹素密度校验）的用法、参数、退出码；**顶点色两个互斥用途**的硬边界；判据的硬失败 vs 只计数口径；正反对照自证；Blender 5.2 的两条版本差异（`colors_type=SRGB`、没有 "Normals Only"）；实测发现（49 件 FBX 无 UV） |
| （WorldKit 场景套件） | 建 kit 资产的人 | `tools/blender/scene/` 的 kit 建模与站面 manifest 机制（`style_tokens.py` / `sync_standables.py`）：**文档已随 M4 归档**，机制见 [归档/M4-世界化](../../项目/归档/M4-世界化/大海域世界化.md) §4；基建去留定案挂 [world-海图101-108删除](../../项目/待办/world-海图101-108删除.md) §三 |

## 与其它域的交界

| 主题 | 去哪 |
| --- | --- |
| 像素海面（海面像素化前置设计：色带波纹 / 量化泡沫 / 岸线带） | [渲染/管线/像素海面.md](../渲染/管线/像素海面.md)（本域只占泡沫贴图一行，主体是 shader 设计，归渲染域） |
| 渲染期色彩口径（色带 / 暗部 / 抖动 / 描边） | [渲染管线.md](../渲染/管线/渲染管线.md) §4/§5 |
| 美术方向与调色板策略 | [美术.md](../../设计/美术.md) §3.2 |
| 写实栈退役的逐项依据（后处理 / Depth / Opaque / Renderer Feature） | [Assets/Art/Rendering/README.md](../../../pirate-crew/Assets/Art/Rendering/README.md) |

## 本域的文件边界

| 域 | 路径 |
| --- | --- |
| 板与工具 | `pirate-crew/Assets/Data/Palette/`、`tools/palette/`、`tools/blender/pixel/` |
| 渲染资产 | `pirate-crew/Assets/Art/`、`pirate-crew/Assets/Settings/` |
| 美术向编辑器脚本 | `pirate-crew/Assets/Editor/Art/`、`pirate-crew/Assets/Editor/` 下的美术渲染向脚本 |
| 本域测试 | `pirate-crew/Assets/Art/Tests/`（理由见[像素纹理.md](像素纹理.md) §3.4） |
