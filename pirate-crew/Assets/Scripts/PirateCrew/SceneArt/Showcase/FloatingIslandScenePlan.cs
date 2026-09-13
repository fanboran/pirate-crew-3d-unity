using PirateCrew.PirateCrew.Battle;
using PirateCrew.PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.PirateCrew.SceneArt.Showcase
{
    /// <summary>
    /// 空岛展示件的**落场景计划**（纯 C#，无头可测）：给 Editor 装配层
    /// （<c>Assets/Editor/FloatingIslandShowcaseMenu.cs</c>）提供资产路径、组命名、
    /// 摆放位置与投影口径的**唯一真源**。
    ///
    /// 【为什么单独一层】与 <see cref="FloatingIslandComposer"/>（只产几何）、
    /// <see cref="IslandMaterialCatalog"/>（只产配方）的分工一致：凡是"文件叫什么、
    /// 摆在哪、投不投影"这类**装配契约**，编辑器菜单与烘焙流程（ArtGate 接线）都要读同一份，
    /// 否则"菜单摆的岛"和"烘焙重建的岛"会悄悄长成两套命名/两套位置。
    /// 本类不含任何 Unity 对象操作，无头验证台可直接断言路径与位置公式。
    ///
    /// 【层级组织】根节点 <see cref="RootName"/> 挂场景根部，其下每个材质槽
    /// （<see cref="IslandMaterial"/>）一个子节点 <c>Island_&lt;槽位&gt;</c>——
    /// 与 SceneArt 的"根 + 每材质组一个子节点"惯例同构（每槽 = 1 网格 + 1 材质 = 1 DrawCall）。
    /// </summary>
    public static class FloatingIslandScenePlan
    {
        // ------------------------------------------------------------------
        // 命名与路径
        // ------------------------------------------------------------------

        /// <summary>场景根节点名（清除菜单与烘焙流程都按它定位，改名即破坏幂等重建）。</summary>
        public const string RootName = "FloatingIslandShowcase";

        /// <summary>材质资产目录（与 SceneArtBuilder 的 Scene_* 同目录，按 FloatingIsland_ 前缀区分）。</summary>
        public const string MaterialFolder = "Assets/Art/Materials/Scene";

        /// <summary>网格资产目录（同上）。</summary>
        public const string MeshFolder = "Assets/Art/Models/Scene";

        /// <summary>
        /// 碰撞代理子节点名（第 3 关可玩地面）：只有 MeshFilter + MeshCollider、**没有 renderer**
        /// ——不违反"renderer 必绑材质"红线（无 renderer 即无洋红风险）。
        /// </summary>
        public const string CollisionChildName = "Island_Collision";

        /// <summary>碰撞代理网格资产路径（与渲染网格分开，物理薄壳不背草叶/树冠的三角面）。</summary>
        public static string CollisionMeshPath()
        {
            return MeshFolder + "/FloatingIsland_Collision.asset";
        }

        /// <summary>槽位子节点名（层级组织：根 → 每槽一个子节点）。</summary>
        public static string GroupName(IslandMaterial material)
        {
            return "Island_" + material;
        }

        /// <summary>网格资产路径（幂等覆写：重摆不产生第 2 份资产）。</summary>
        public static string MeshAssetPath(IslandMaterial material)
        {
            return MeshFolder + "/" + IslandMaterialCatalog.AssetName(material) + ".asset";
        }

        /// <summary>材质资产路径（幂等覆写：重摆只改属性不换引用）。</summary>
        public static string MaterialAssetPath(IslandMaterial material)
        {
            return MaterialFolder + "/" + IslandMaterialCatalog.AssetName(material) + ".mat";
        }

        // ------------------------------------------------------------------
        // 投影口径
        // ------------------------------------------------------------------

        /// <summary>
        /// 该槽位是否接收阴影：实体档（PBR 表面 / 不透明 unlit）接收；半透明与加法发光不接收
        /// ——浮空发光件与云雾在地上投影/接影都会读成"脏"（与 RuntimeSceneArt 的实体/效果分组同口径）。
        /// </summary>
        public static bool ReceivesShadows(IslandMaterial material)
        {
            IslandShaderKind kind = IslandMaterialCatalog.For(material).Kind;
            return kind == IslandShaderKind.SurfaceSolid || kind == IslandShaderKind.UnlitOpaque;
        }

        // ------------------------------------------------------------------
        // 摆放位置
        // ------------------------------------------------------------------

        /// <summary>
        /// 岛体边缘与竞技场远缘之间的最小净空（世界单位）。【AI 提案】
        /// 足够让崖沿垂帘/层理散石的外扩轮廓不压到远缘的危险虚线，又不必把岛推出雾外。
        /// </summary>
        public const float FarEdgeGap = 6f;

        /// <summary>
        /// 展示位（背景位）：悬在**竞技场远缘外侧**的云海上方——根节点位置即岛局部原点
        /// （草皮穹顶中心平面），岛尖/悬瀑由 <see cref="FloatingIslandComposer.PlacementHeight"/>
        /// 保证高于竞技场 <see cref="FloatingIslandComposer.ArenaClearance"/>（9 单位）净空。
        ///
        /// 【为什么放远缘外侧而不是竞技场正上方（三条硬理由，都可在无头断言）】
        /// ① **遮挡**：战斗相机（pitch 45° / 距离 30 / FOV 60）的视野上缘只到"前方 15° 仰角"，
        ///    悬在竞技场正上方的岛（顶面 y≈33）必须把相机大幅上抬才可见——而任何"看得到岛"的
        ///    机位都隔着岛看竞技场，主视角全被挡；放远缘外侧则主视角完全不被干扰，
        ///    广角/拉远/摇镜时岛作为云海背景入画。
        /// ② **影子**：平行光 Euler(48°,140°,0°) 的影子朝 (+X, −Z)——岛在远缘（−Z）外侧时，
        ///    整片影子落进海里，不压竞技场的可玩采光（竞技场暗部透气是验收红线）。
        /// ③ **雾**：距战斗相机约 70 单位，线性雾（起点 50 / 终点 280）只吃约 9% ——
        ///    拿到一层天然的空气透视，而不是"贴脸的背景板"或"雾里一个影子"。
        ///
        /// 水平回收量取 1.25 × 岛体长半径 + <see cref="FarEdgeGap"/>：岛体半径带 ±13% 抖动、
        /// 外挂散石再外扩约 15%，1.25 倍确保**最坏情况**下岛体离远缘仍有 ≥<see cref="FarEdgeGap"/>。
        /// </summary>
        public static Vector3 BackdropRootPosition(
            FloatingIslandSpec spec, float arenaCenterX, float arenaFarEdgeZ, float arenaY)
        {
            if (spec == null)
                spec = FloatingIslandSpec.Default;

            float bodyRadius = Mathf.Max(spec.RadiusX, spec.RadiusZ);
            return new Vector3(
                arenaCenterX,
                FloatingIslandComposer.PlacementHeight(spec, arenaY),
                arenaFarEdgeZ - FarEdgeGap - bodyRadius * 1.25f);
        }

        /// <summary>
        /// 按 level_1 的竞技场取默认展示位（与 SceneArtBuilder 的构建口径同源：
        /// 场地中心 X / 远缘 Z = 0 / 地面 y = 0）。关卡未转写时退化为原点远侧的通用位。
        /// </summary>
        public static Vector3 DefaultBackdropRootPosition(FloatingIslandSpec spec)
        {
            if (!LevelCatalog.IsTranscribed(1))
            {
                // 关卡数据缺失的兜底：摆到原点远侧，仍然保证 PlacementHeight 的净空契约。
                return BackdropRootPosition(spec, 0f, 0f, LevelGeometry.GroundTopY);
            }

            LevelData level = LevelCatalog.Get(1);
            float halfWidth = LevelGeometry.TileToWorld(level.WidthTiles) * 0.5f;
            float farEdgeZ = 0f;   // GridToArena 的 Z 从 0 起（见 LevelGeometry 类头），远缘即 Z=0。
            return BackdropRootPosition(spec, halfWidth, farEdgeZ, LevelGeometry.GroundTopY);
        }
    }
}
