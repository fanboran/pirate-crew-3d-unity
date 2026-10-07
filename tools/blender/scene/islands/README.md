# tools/blender/scene/islands —— 关卡岛体「翻译式重做」管线(C# 几何 → Blender)

> **口径**:云端漫步 / 天空之岛两关的岛体视觉已过审(C# 程序化生成),本目录做的是
> **原封不动的格式翻译**——把 C# 生成器的几何逐行移植到 Blender 无头脚本,布局/形制/
> 配色/构图完全照抄,产物 FBX 替换场景引用。不重新设计、不改观感;随机全部是确定性
> 整数哈希(LowpolyHash / SceneArtHash 纯 uint32 位运算),Python 逐位复刻,几何与
> C# 版一致(顶点差仅 float32→double 三角函数精度,< 1e-4 m)。
>
> **碰撞不在视觉件**:可站/落水判定走逻辑层地形数据(`BattleTerrainView` 运行时按
> terrain 逐格生成 BoxCollider),FBX 零碰撞职责。

## 文件与用法

| 文件 | 干什么 |
| --- | --- | --- |
| `translate_cloudfield.py` | 第 1 关云场:`CloudFieldGeometry` 的忠实翻译(11 朵环形 blob 云:主角云 + 环1×4 + 环2×6,暖白/淡金按高度分带,顶平底平削形,±8% 径向抖动) |

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

内嵌材质 = 旧运行时槽同名同色(`Lowpoly_CloudWarmWhite` #FFF2DB / `Lowpoly_CloudPaleGold`
#FFD98F,出处 `LowpolyStageBuilder.cs` 的 SlotColor);Unity 侧 `PixelartContentConverter`
按源材质取色换血,色值单源不双写。

## 配套工具(可站面 / 关卡数据)

- `tools/level-design/patch_cloud_ring2.py`:第二环 6 朵补进 golden terrain(16 边形贴云台,
  边界收缩不出场;用户定夺「第二环做成可站立」)。
- 改 golden 后走 batchmode:`LevelDataMigrator.MigrateAll` → `WriteGoldenFromAssets`
  拉直 C# 权威格式;冻结签名测试(`LevelAssetTests.FrozenLevelSignatures`)随设计变更重冻。
