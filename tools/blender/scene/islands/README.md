# tools/blender/scene/islands —— 关卡岛体「翻译式重做」管线(C# 几何 → Blender)

> **口径**:云端漫步 / 天空之岛两关的岛体视觉已过审(C# 程序化生成),本目录最初做的是
> **格式翻译**——把 C# 生成器的几何逐行移植到 Blender 无头脚本,布局/形制/构图照抄,
> 产物 FBX 替换场景引用。随机全部是确定性整数哈希(LowpolyHash / SceneArtHash 纯 uint32
> 位运算),Python 逐位复刻。
>
> **2026-11-09 地图审计后的形状层改动**(创始人要求"地形美观优化/与两套 shader 搭配"):
> 保持**可站台面与碰撞完全不变**,只改视觉几何——主 blob 分段 7/9→9/11、侧壁加一圈
> 边缘云球(外鼓剪影、顶部仍削平)、竞技场外 34–54 m 加 8 座远景云塔(仅视觉,不进逻辑
> 地形)。改前的纯翻译版几何可由 git 历史回看。
>
> **碰撞不在视觉件**:可站/落水判定走逻辑层地形数据(`BattleTerrainView` 运行时按
> terrain 逐格生成 BoxCollider),FBX 零碰撞职责。

## 文件与用法

| 文件 | 干什么 |
| --- | --- | --- |
| `translate_cloudfield.py` | 第 1 关云场:`CloudFieldGeometry` 的忠实翻译(11 朵环形 blob 云:主角云 + 环1×4 + 环2×6,WarmWhite/PaleGold 两槽按高度分带(色值见下「材质」),顶平底平削形,±4% 径向抖动) |

```bash
B="F:/SteamLibrary/steamapps/common/Blender/blender.exe"
"$B" -b --factory-startup -P tools/blender/scene/islands/translate_cloudfield.py -- --samples 48
# 可选:--no-render / --samples N / --res N
```

**产物**:`Assets/Art/Models/SceneKit/CloudWalk.fbx`(2 节点 WarmWhite/PaleGold)、
`docs/images/level-islands/cloud_field-{top,front34,side}` 预览。

## 坐标口径

C# 几何在 Unity 世界系(云场中心 = 原点);映射 Unity(x,y,z) → Blender(x,−z,y)
(与 `style_tokens.export_fbx` + `bakeAxisConversion` 链一致,保定向,面绕序原样保留)。
Unity 侧实例摆 (20, 0, 15)(关卡 bakedPieces 现值)。

## 材质

内嵌材质 = 旧运行时槽同名(`Lowpoly_CloudWarmWhite` / `Lowpoly_CloudPaleGold`,槽名出处
`LowpolyStageBuilder.cs` 的 SlotColor)。2026-10-07 创始人裁决「云彩应该是白色」之后,
两槽都不发黄;2026-11-09 地图审计把**分带方向**修正为"顶白、底冷":

- `WarmWhite`(高度阈值**以下** = 云台侧壁/底面)= #EDF4FB 冷白;
- `PaleGold`(高度阈值**以上** = 云台顶面)= #FFFFFF 纯白。

旧值(两个都近白、且底面比顶面更白)在实拍里读作"白顶 + 白墙",体积全靠光照硬撑。
Unity 侧以 `Assets/Art/Materials/Lowpoly/*.mat` 为**运行时基色单源**,本文件 `SLOTS`
只服务 Blender 预览渲染,两处同步改。

【为什么不是黄/金】`PaleGold` 只是历史槽名;配色仍守"白云"裁决,蓝色影调由
CellShade 的环境色与 PostLine 的 Palette(`_ShadowColor` 冷蓝灰)承担,不写进 albedo。

## 配套工具(可站面 / 关卡数据)

- `tools/level-design/patch_cloud_ring2.py`:第二环 6 朵补进 golden terrain(16 边形贴云台,
  边界收缩不出场;用户定夺「第二环做成可站立」)。
- 改 golden 后走 batchmode:`LevelDataMigrator.MigrateAll` → `WriteGoldenFromAssets`
  拉直 C# 权威格式;冻结签名测试(`LevelAssetTests.FrozenLevelSignatures`)随设计变更重冻。
