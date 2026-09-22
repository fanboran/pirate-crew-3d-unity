using System.Collections.Generic;
using System.IO;
using PirateCrew.Battle;
using PirateCrew.Visual;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 角色视觉预制体生成器：程序化产出 7 个职业预制体（六职业 + 船长），
    /// 配套网格资产（<c>Assets/Art/Models/Generated/</c>）与共享材质（<c>Assets/Art/Materials/Crew/</c>）。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/角色/生成职业视觉预制体
    ///   无头: -batchmode -nographics -quit -executeMethod PirateCrew.EditorTools.CrewVisualPrefabBuilder.BuildAll
    ///
    /// 【造型口径 — 用户裁决 2026-09-14；尺度口径 2026-09-14 修正（用户裁决②"角色为什么比木筏子小这么多"）】
    ///   7 个职业的视觉装配**完全相同**：一个圆台柱 Body + 一个圆球 Head，逐值对齐 Godot 基准
    ///   `../game-3/pirate-crew-3d/modules/pirate_crew/scenes/pirate.tscn`
    ///   （该场景全文只有 Body / Head 两个 MeshInstance3D）。
    ///
    ///   **尺度推导链（先证 Godot 的格子世界尺寸，再定目标比例）**——
    ///   <list type="number">
    ///   <item>Godot `battle.tscn` 地面 = <c>PlaneMesh(size = Vector2(50, 50))</c> → 50×50 Godot 世界单位；</item>
    ///   <item>`docs/M2-3D空间模型对齐.md` §2 把该地面定义为「尺寸 = 关卡 widthTiles × heightTiles」，
    ///         而该演示场景对应的关卡宽 50 格（`LevelCatalog.level_1` widthTiles = 50）
    ///         → **50 格 ↔ 50 Godot 世界单位 → Godot 1 格 = 1 Godot 单位**（<see cref="GodotUnitsPerTile"/>）；</item>
    ///   <item>Godot `pirate.tscn` 角色总高 = Body 圆柱 h1.2（y −0.8..+0.4）与 Head 球 d0.7（y +0.35..+1.05）
    ///         在轴上重叠 0.05 → **1.85 Godot 单位**（<see cref="GodotReferenceHeight"/>）；</item>
    ///   <item>本工程 1 格 = 1 世界单位（`LevelGeometry.PixelsPerUnit = 32`、`WorldWidth = widthTiles`，
    ///         见 <see cref="UnityUnitsPerTile"/>）→ 换算系数 = 1 本工程单位 / 1 Godot 单位 = 1；</item>
    ///   <item>→ **目标视觉总高 = 1.85 世界单位**（<see cref="TargetUnitHeight"/>）。</item>
    ///   </list>
    ///   于是等比缩放系数 k = <see cref="TargetUnitHeight"/> / 1.85 = 1，两件式尺寸即 Godot 原值：
    ///   <list type="bullet">
    ///   <item>Body 圆台柱 = 顶 r <see cref="BodyTopRadius"/> 0.35（Godot <c>top_radius 0.35</c>）
    ///         / 底 r <see cref="BodyBottomRadius"/> **0.46**（Godot 是 0.40——这一项按创始人 2026-09-22
    ///         裁决有意放大，理由见该常量的注释）/ 高 <see cref="BodyHeight"/> 1.20，底面贴脚底 y=0；</item>
    ///   <item>Head 圆球 = Godot <c>SphereMesh(radius 0.35, height 0.7)</c> × k → r <see cref="HeadSphereRadius"/> 0.35，
    ///         球心 y <see cref="HeadSphereCenterY"/> 1.50（球底 1.15 与柱顶 1.20 微叠 0.05，与 Godot 的
    ///         `1.2/2 − 0.7/2 = 0.05` 一致；总高 = 1.50 + 0.35 = 1.85）。</item>
    ///   </list>
    ///   材质：Head = 木色 <c>#D4A76A</c>（CrewWood，与 Godot <c>cel_wood</c> 0.83/0.65/0.42 = #D4A66B 同值），
    ///   Body = 阵营色（运行时由 UnitOutlineBinder 逐队写 <c>_BaseColor</c>）。
    ///   职业差异只在数据（攻/防/技能）与 HUD，**不体现在造型上**（见 docs/角色造型规范.md）。
    ///
    /// 【为什么删掉了腿/靴/臂/掌/三角帽/头巾/发/鼻/眼/手持武器】那些零件是此前多轮复验里
    ///   **AI 自主加件**的产物（"评委"式跑偏），用户从未要求；用户原话是"一个球加一个梯形"，
    ///   与 Godot 版比对后明确"我要和 Godot 里面一模一样那种圆球+圆台柱"。故装配代码整段移除
    ///   （见 <see cref="ApplyGodotTwoPieceSilhouette"/>）；网格资产与 <c>CrewMeshLibrary</c> 的键
    ///   **不删库**（生成链路与三角面预算镜像表仍在），只是预制体里不再装配它们。
    ///
    /// 【预制体结构】
    /// <code>
    /// Crew_&lt;Profession&gt;（根）
    /// ├ Transform.scale = (0.375, 0.5, 0.375)   ← 与 PirateBase.prefab 一致
    /// ├ BoxCollider size=(1,1,1)                  ← 世界 AABB 0.375×0.5×0.375（碰撞契约不变）
    /// ├ Rigidbody mass=1 drag=0 angularDrag=0.05 useGravity=on
    /// ├ PirateBase / UnitOutlineBinder / CrewVisualAnimator
    /// ├ ContactShadow（接触阴影面片，脚底上方 0.02；见 §N7）
    /// └ Visual（CrewVisualRig）
    ///    └ BodyPivot（脚底枢轴：整身 bob / 倒地 / 落水下沉）
    ///       └ TorsoPivot（与 BodyPivot 同在脚底：呼吸缩放 + 前倾）
    ///          ├ Body（圆台柱 renderer，阵营色）
    ///          └ HeadPivot（球心 y=1.50：点头 / 呼吸浮动）
    ///             └ Head（圆球 renderer，木色）
    /// </code>
    ///   BodyPivot / TorsoPivot / HeadPivot 三个动画枢轴是 <c>CrewVisualAnimator</c> 的唯一接口
    ///   （它只读写这三个枢轴并对臂/腿/手持物挂点做 null 判断），故两件式下动画链路零改动。
    ///
    /// 【为什么不替换 PirateBase.prefab 的 Cube 外观】<c>M2BattleSceneSetup.BuildAll</c> 会重建
    ///   PirateBase.prefab 为 Cube；把职业外观塞进去会与该重建互相覆盖。这里改为**独立职业预制体**，
    ///   由 <c>BattleController.crewVisualPrefabs</c> 按 <c>CrewVisualCatalog</c> 映射选择，
    ///   未命中/未接线时回落 PirateBase.prefab（对方块外观做兜底，不破坏既有场景）。
    ///
    /// 【坐标口径】单位根是 0.375/0.5/0.375 的非均匀缩放，Visual 用 (1/0.375, 1/0.5, 1/0.375) 抵消，
    ///   于是 **Visual 局部 1 单位 = 世界 1 单位、XZ 不畸变**；又因 Visual.localPosition.y = −0.5 而
    ///   rootScale.y = 0.5，得 `worldY = visualLocalY − 0.25` → 以"脚底 = 0、总高 = 1.85"的口径看，
    ///   **Visual 局部 y 就等于脚底起算的世界 y**，故本文件里的尺寸常量可直接用世界值。
    ///
    /// 【幂等】
    ///   · 网格资产存在则**就地刷新**（<c>ApplyToMesh</c>，不换 GUID、不丢引用）；
    ///   · 材质存在则就地更新参数（不新建重名副本）；
    ///   · 预制体按固定路径覆盖保存。
    ///
    /// 【保留的材质级参数】<see cref="DashFrequencySelected"/> / <see cref="OutlineWidthSelected"/>
    ///   只写进 Crew 系列材质（选中虚线的周期与宽度），与几何无关，两件式下继续生效。
    /// </summary>
    public static class CrewVisualPrefabBuilder
    {
        // ------------------------------------------------------------------
        // 路径
        // ------------------------------------------------------------------

        const string MeshFolder = "Assets/Art/Models/Generated";
        const string CrewMaterialFolder = "Assets/Art/Materials/Crew";
        const string CrewTextureFolder = "Assets/Art/Textures/Crew";
        const string CrewPrefabFolder = "Assets/Prefabs/PirateCrew/Crew";

        const string OutlineShaderName = "PirateCrew/PirateOutline";
        const string UnlitShaderName = "Universal Render Pipeline/Unlit";

        /// <summary>单位根缩放：与 PirateBase.prefab 一致，使 BoxCollider(1,1,1) 的世界 AABB = 0.375×0.5×0.375。</summary>
        static readonly Vector3 UnitRootScale = new Vector3(12f / 32f, 16f / 32f, 12f / 32f);

        // ------------------------------------------------------------------
        // 造型尺寸（用户裁决 2026-09-14：与 Godot pirate.tscn 逐值一致）
        // ------------------------------------------------------------------

        /// <summary>
        /// Godot 基准总高 = 1.85：pirate.tscn 的 Body 圆柱 h1.2（y −0.8..+0.4）与 Head 球 d0.7（y +0.35..+1.05）
        /// 在轴上重叠 0.05（0.4−0.35）→ 总高 1.2 + 0.7 − 0.05 = 1.85。
        /// </summary>
        const float GodotReferenceHeight = 1.85f;

        /// <summary>
        /// Godot 1 格 = 1 Godot 世界单位。推导见类头「尺度推导链」：
        /// Godot `battle.tscn` 地面 <c>PlaneMesh(50, 50)</c>（50 Godot 单位）↔ 关卡宽 50 格。
        /// </summary>
        const float GodotUnitsPerTile = 1f;

        /// <summary>
        /// 本工程 1 格 = 1 世界单位（`LevelGeometry.PixelsPerUnit = 32`，`WorldWidth = widthTiles`）。
        /// </summary>
        const float UnityUnitsPerTile = 1f;

        /// <summary>
        /// **目标视觉总高（世界单位）= 1.85**。由 Godot 角色总高按"格"归一：
        ///   <c>1.85 Godot 单位 × (1 格 / <see cref="GodotUnitsPerTile"/> Godot 单位)
        ///   × (<see cref="UnityUnitsPerTile"/> 世界单位 / 1 格)</c>
        ///   = <c>1.85 × UnityUnitsPerTile / GodotUnitsPerTile</c> = **1.85**。
        ///
        /// 【为什么不再用旧的 0.55】旧口径把整身压到 0.55 世界单位（≈0.55 格高），
        ///   与 Godot 角色的 1.85 格高差了 3.36 倍，用户一眼看出"角色比木筏子小这么多"。
        ///   碰撞足迹仍由根级 BoxCollider 决定（0.375×0.5×0.375，Flash 12×16px 契约，见
        ///   <see cref="UnitRootScale"/>），**与视觉高度无关**，故本次只放大视觉、不动碰撞体
        ///   （PirateBase.prefab 的碰撞盒由禁改的 `M2BattleSceneSetup.BuildPiratePrefab` 生成）。
        /// </summary>
        const float TargetUnitHeight = GodotReferenceHeight * UnityUnitsPerTile / GodotUnitsPerTile;

        /// <summary>等比缩放系数 k = 1.85 / 1.85 = **1**（Godot 单位 → 本工程世界单位；两件式即 Godot 原值）。</summary>
        static readonly float GodotScale = TargetUnitHeight / GodotReferenceHeight;

        /// <summary>Body 圆台柱顶半径 = 0.35 × k = **0.35**（Godot <c>CylinderMesh.top_radius 0.35</c>）。</summary>
        static readonly float BodyTopRadius = 0.35f * GodotScale;

        /// <summary>
        /// Body 圆台柱底半径 = **0.46**（顶 r 0.35 / 高 1.20）→ 上窄下宽。
        ///
        /// 【与 Godot 基准的**有意偏离**（创始人裁决 2026-09-22）】Godot <c>pirate.tscn</c> 是
        /// <c>bottom_radius 0.4</c>（锥度 0.35/0.40 = 0.875），像素化路径下**读不出台柱形**：
        /// 中机位（可见 18m）里角色只有 15 个艺术像素高、9 个宽，上下口径差 2 个艺术像素不变；
        /// 实测结论见 <c>docs/images/pixelart-path/r6/README.md</c> §2（"角色没台柱感"= 取景/锥度问题）。
        /// 创始人原话「台柱下直径稍微改大一圈」⇒ 只放大底径、顶径与总高不动：
        /// **底径 0.80 → 0.92（+15%）、锥度 0.35/0.46 = 0.761**（上下口径差 3.7 个艺术像素）。
        ///
        /// 【为什么只动底径】角色造型的其余部分（球头 r 0.35、总高 1.85、脚底贴地）是
        /// 2026-09-14 的裁决项；本次是创始人**就"台柱感"这一条**给出的定向修正，不是重开造型。
        /// 碰撞足迹仍由根级 BoxCollider 决定（0.375×0.5×0.375），**与视觉宽度无关**。
        /// </summary>
        static readonly float BodyBottomRadius = 0.46f * GodotScale;

        /// <summary>Body 圆台柱高 = 1.20 × k = **1.20**，底面贴脚底（局部 y 0..1.20）。</summary>
        static readonly float BodyHeight = 1.20f * GodotScale;

        /// <summary>Head 球半径 = 0.35 × k = **0.35**（Godot <c>SphereMesh.radius 0.35</c>，与柱顶半径同值）。</summary>
        static readonly float HeadSphereRadius = 0.35f * GodotScale;

        /// <summary>
        /// Head 球心高度 = (0.7 + 0.8) × k = 1.50 × k = **1.50**：
        /// Godot 里头心在根空间 +0.7、身体底面在 −0.8，平移到地面后为 (0.7 + 0.8) 再乘 k。
        /// 校验：球底 1.50 − 0.35 = 1.15 &lt; 柱顶 1.20（重叠 0.05），总高 1.85。
        /// </summary>
        static readonly float HeadSphereCenterY = 1.50f * GodotScale;

        /// <summary>
        /// 接触阴影面片直径（世界单位）= 目标视觉总高 × 1.1 ≈ **2.035**。
        /// 旧口径 `ContactShadowDecal.DefaultDiameter = 0.6` ≈ 旧总高 0.55 × 1.1；本次随角色放大按**同一比例**
        /// 同步（≈ 两件式圆台柱底径 0.92 的 2.21 倍），保持"脚下压暗一圈、不外溢邻格"的观感。
        ///
        /// 【为什么不改 `ContactShadowDecal.DefaultDiameter`】那是 Scripts 侧的常量（本轮不在改动域内），
        ///   只作 `OnValidate` 提醒与参数留档；预制体真值由本常量写进 decal 的 `diameter` 序列化字段
        ///   与 Transform.localScale（见 <see cref="AddContactShadow"/>）。
        /// </summary>
        static readonly float ContactShadowDiameter = TargetUnitHeight * 1.1f;

        /// <summary>圆台柱侧壁分段（任务给定 16；三角面 = 侧壁 32 + 上下盖 32 = 64）。</summary>
        const int GodotBodySides = 16;

        /// <summary>球经向分段（任务给定 16）。</summary>
        const int GodotHeadSegments = 16;

        /// <summary>球纬向分段（任务给定 12；三角面 = 2×16×(12−1) = 352）。</summary>
        const int GodotHeadRings = 12;

        // ------------------------------------------------------------------
        // 本文件追加的资产键（**不登记进 CrewMeshLibrary**，
        // 以免动到预算镜像表 CrewMeshLibrary.CountPartInstances 的既有断言）
        // ------------------------------------------------------------------

        /// <summary>两件式 Body 圆台柱（顶 r 0.35 / 底 r 0.46 / h 1.20，16 段；底径见常量注释）。</summary>
        const string BodyFrustumKey = "CrewBodyFrustum";

        /// <summary>两件式 Head 圆球（r 0.35，16×12）。</summary>
        const string HeadSphereKey = "CrewHeadSphere";

        /// <summary>接触阴影面片（1×1 XY 面，法线 +Z；装配时绕 X 转 −90° 平铺）。</summary>
        const string ContactShadowQuadKey = "CrewContactShadowQuad";

        const string ContactShadowMaterialFileName = "CrewContactShadow";
        const string ContactShadowTextureFileName = "CrewContactShadow.png";

        // ------------------------------------------------------------------
        // 描边材质参数（所有 Crew 材质共用；与几何无关）
        // ------------------------------------------------------------------

        /// <summary>
        /// 选中虚线的屏幕空间频率（写入所有 Crew 材质的 <c>_DashFrequency</c>）。
        ///
        /// 【换算】phase = NDC.y·F + Time.y·_DashSpeed，NDC.y 跨 2 个单位对应 H 像素 →
        ///   虚线周期 = π·H/F 像素、ON/OFF 各半（1080p：F=50 → 68px 周期 / 各 34px；F=150 → 23px / 各 11px）。
        /// 【为什么取 150】判据要求"选中态下**全部部件**出现青虚线段"（两件式下 = Head + Body）。
        ///   ON/OFF 只由屏幕 Y 决定，F=50 时 OFF 带 34px 与单位屏幕尺寸（广角 ~26px、特写 ~265px）
        ///   同量级，一圈轮廓只落 0~1 段，观感是"角落两三段短线"；F=150 时周期 23px ≪ 单位尺寸 →
        ///   轮廓上恒有 3~11 段虚线，读作完整的一圈点划描边（docs/描边Shader调试.md §三 已记录该现象）。
        /// 【同步要求】<c>M2BattleSceneSetup.EnsureOutlineMaterial</c> 持同源参数镜像（含 _DashFrequency）。
        ///   改这里必须同步那边，否则旧单立方体兜底材质仍是 68px 周期。
        /// </summary>
        const float DashFrequencySelected = 150f;

        /// <summary>
        /// 选中态描边宽度（写入所有 Crew 材质的 <c>_OutlineWidthSelected</c>；模式 0 = 屏幕空间恒定粗细）。
        ///
        /// 【为什么是 0.010】模式 0 下屏幕上单边宽 ≈ `width · (positionCS.w)^(1−_OutlineDistanceAttenuation) / w`
        ///   → ≈ `width · w^(−0.4)`；特写机位 w≈2 时 0.006 → ≈0.0045 NDC ≈ 2.4px，
        ///   扣掉被本体自遮挡的部分后可见只剩 ~1px，再被虚线的 ON/OFF（各 11px）截断，小部件可能读不到青色。
        ///   0.010 → ≈4px（可见 ~2px），与躯干同量级（docs/描边Shader调试.md §三 推荐区间 0.002~0.012）。
        /// 【同步要求】<c>M2BattleSceneSetup.EnsureOutlineMaterial</c> 持同源镜像（含 _OutlineWidthSelected）；
        ///   那个方法只服务旧单立方体兜底材质，未同步（不在本文件域内）。
        /// </summary>
        const float OutlineWidthSelected = 0.010f;

        /// <summary>悬停描边宽度：与选中等比（0.0025 → 0.0042），保持"悬停比选中细"的既有语义。</summary>
        const float OutlineWidthHover = OutlineWidthSelected * 0.42f;

        /// <summary>接触阴影面片色：近黑 + 中心 α 0.45（边缘 α 由贴图径向渐变收到 0）。</summary>
        static readonly Color ContactShadowColor = new Color(0.015f, 0.015f, 0.020f,
            ContactShadowDecal.DefaultCenterAlpha);

        // ------------------------------------------------------------------
        // 追加资产打包
        // ------------------------------------------------------------------

        /// <summary>本文件生成的两件式网格 + 接触阴影件（生成后注入 <see cref="BuildProfessionPrefab"/>）。</summary>
        struct VisualAddOns
        {
            public Mesh BodyFrustum;
            public Mesh HeadSphere;
            public Mesh ContactShadowQuad;
            public Material ContactShadowMaterial;
        }

        // ------------------------------------------------------------------
        // 入口
        // ------------------------------------------------------------------

        /// <summary>生成全部网格 / 材质 / 职业预制体（幂等，可重复调用）。</summary>
        [MenuItem("PirateCrew/角色/生成职业视觉预制体")]
        public static void BuildAll()
        {
            EnsureFolder("Assets/Art");
            EnsureFolder("Assets/Art/Models");
            EnsureFolder(MeshFolder);
            EnsureFolder("Assets/Art/Materials");
            EnsureFolder(CrewMaterialFolder);
            EnsureFolder("Assets/Art/Textures");
            EnsureFolder(CrewTextureFolder);
            EnsureFolder("Assets/Prefabs");
            EnsureFolder("Assets/Prefabs/PirateCrew");
            EnsureFolder(CrewPrefabFolder);

            Dictionary<string, MeshData> meshData = CrewMeshLibrary.BuildAll();
            InjectAddOnMeshes(meshData);
            Dictionary<string, Mesh> meshes = BuildMeshAssets(meshData);
            Material[] materials = BuildMaterialAssets();
            VisualAddOns addOns = BuildAddOns(meshes);
            CrewVisualAssetSet assetSet = BuildAssetSet(meshes, materials);

            var report = new System.Text.StringBuilder();
            report.AppendLine("[CrewVisualPrefabBuilder] 职业视觉预制体生成完成（用户裁决：Godot 两件式 = 圆球 + 圆台柱）：");
            report.AppendLine("  网格资产: " + MeshFolder + "（" + meshes.Count + " 个）");
            report.AppendLine("  材质资产: " + CrewMaterialFolder + "（" + materials.Length + " 个 + 接触阴影 1 个）");
            report.AppendLine("  Body 圆台柱: 顶 r " + BodyTopRadius.ToString("0.00000")
                + " / 底 r " + BodyBottomRadius.ToString("0.00000")
                + " / h " + BodyHeight.ToString("0.00000") + "（" + GodotBodySides + " 段）");
            report.AppendLine("  Head 圆球: r " + HeadSphereRadius.ToString("0.00000")
                + " / 球心 y " + HeadSphereCenterY.ToString("0.00000")
                + "（" + GodotHeadSegments + "×" + GodotHeadRings + "）");

            int failures = 0;
            for (int i = 0; i < CrewVisualCatalog.AllProfessions.Length; i++)
            {
                CrewProfession profession = CrewVisualCatalog.AllProfessions[i];
                GameObject prefab = BuildProfessionPrefab(profession, assetSet, addOns,
                    out int renderers, out int triangles);
                if (prefab == null)
                {
                    failures++;
                    report.AppendLine("  [" + CrewVisualCatalog.DisplayName(profession) + "] 生成失败");
                    continue;
                }

                report.AppendLine("  [" + CrewVisualCatalog.DisplayName(profession) + " / " + profession + "] "
                    + "部件 renderer=" + renderers + "（Body+Head）"
                    + " tri=" + triangles + "（含接触阴影 2）"
                    + " 预算 " + CrewMeshFactory.MaxTrianglesPerUnit
                    + (triangles <= CrewMeshFactory.MaxTrianglesPerUnit ? " OK" : " **超预算**"));
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (failures > 0)
                Debug.LogError(report.ToString());
            else
                Debug.Log(report.ToString());
        }

        // ------------------------------------------------------------------
        // 追加网格（两件式造型 + 接触阴影）
        // ------------------------------------------------------------------

        /// <summary>
        /// 往网格表里追加本文件需要的三件网格：
        /// · <see cref="BodyFrustumKey"/>：按世界尺寸（不是单位尺寸）生成，装配时缩放恒为 1 —— 尺寸即规格值；
        /// · <see cref="HeadSphereKey"/>：同样按世界半径生成；
        /// · <see cref="ContactShadowQuadKey"/>：1×1 的 XY 面（法线 +Z），预制体里绕 X 转 −90° 平铺后缩放。
        /// </summary>
        static void InjectAddOnMeshes(Dictionary<string, MeshData> meshData)
        {
            meshData[BodyFrustumKey] = CrewMeshFactory.Frustum(
                BodyTopRadius, BodyBottomRadius, BodyHeight, GodotBodySides);
            meshData[HeadSphereKey] = CrewMeshFactory.LowPolySphere(
                HeadSphereRadius, GodotHeadSegments, GodotHeadRings);
            meshData[ContactShadowQuadKey] = BuildQuadMeshData();
        }

        static MeshData BuildQuadMeshData()
        {
            var vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
            };
            var normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
            var uvs = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
            };
            var triangles = new[] { 0, 1, 2, 0, 2, 3 };   // 逆时针 → 正面朝 +Z
            return new MeshData(vertices, normals, uvs, triangles);
        }

        // ------------------------------------------------------------------
        // 网格资产
        // ------------------------------------------------------------------

        static Dictionary<string, Mesh> BuildMeshAssets(Dictionary<string, MeshData> meshData)
        {
            var result = new Dictionary<string, Mesh>(meshData.Count);
            foreach (KeyValuePair<string, MeshData> pair in meshData)
            {
                string path = MeshFolder + "/" + pair.Key + ".asset";
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing != null)
                {
                    CrewMeshFactory.ApplyToMesh(existing, pair.Value);
                    EditorUtility.SetDirty(existing);
                    result[pair.Key] = existing;
                    continue;
                }

                Mesh mesh = CrewMeshFactory.CreateMesh(pair.Key, pair.Value);
                AssetDatabase.CreateAsset(mesh, path);
                result[pair.Key] = mesh;
            }
            return result;
        }

        // ------------------------------------------------------------------
        // 材质资产
        // ------------------------------------------------------------------

        static Material[] BuildMaterialAssets()
        {
            var roles = System.Enum.GetValues(typeof(CrewMaterialRole));
            var materials = new Material[roles.Length];

            Shader outlineShader = ResolveOutlineShader();

            foreach (CrewMaterialRole role in roles)
            {
                string path = CrewMaterialFolder + "/" + CrewVisualCatalog.MaterialFileName(role) + ".mat";
                Material material = LoadOrCreateMaterial(path, CrewVisualCatalog.MaterialFileName(role), outlineShader);
                ApplyOutlineUnitMaterial(material, CrewVisualCatalog.RoleColor(role));
                EditorUtility.SetDirty(material);
                materials[(int)role] = material;
            }

            return materials;
        }

        static VisualAddOns BuildAddOns(Dictionary<string, Mesh> meshes)
        {
            return new VisualAddOns
            {
                BodyFrustum = meshes[BodyFrustumKey],
                HeadSphere = meshes[HeadSphereKey],
                ContactShadowQuad = meshes[ContactShadowQuadKey],
                ContactShadowMaterial = BuildContactShadowMaterial(),
            };
        }

        /// <summary>
        /// 接触阴影材质（N7）：URP/Unlit 透明，贴图 = 程序化径向渐变，颜色 = 近黑（α 由贴图 × _BaseColor.a）。
        /// 用 Unlit 而非 Lit，因为阴影面片不该再被主光照亮一次（否则逆光时会变成一块亮斑）。
        /// </summary>
        static Material BuildContactShadowMaterial()
        {
            EnsureFolder("Assets/Art/Textures");
            EnsureFolder(CrewTextureFolder);
            Texture2D radial = BuildContactShadowTexture();

            Shader unlit = Shader.Find(UnlitShaderName);
            if (unlit == null)
            {
                Debug.LogWarning("[CrewVisualPrefabBuilder] 未找到 shader " + UnlitShaderName
                    + "，接触阴影退回 Unlit/Transparent（贴图/颜色仍生效）。");
                unlit = Shader.Find("Unlit/Transparent");
            }

            string path = CrewMaterialFolder + "/" + ContactShadowMaterialFileName + ".mat";
            Material material = LoadOrCreateMaterial(path, ContactShadowMaterialFileName, unlit);

            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", radial);
            if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", radial);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", ContactShadowColor);

            // URP/Unlit 的透明档（等价于 Inspector 里把 Surface Type 切成 Transparent、Blending 选 Alpha）。
            material.SetOverrideTag("RenderType", "Transparent");
            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend"))
                material.SetFloat("_Blend", 0f);
            if (material.HasProperty("_SrcBlend"))
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend"))
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite"))
                material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_Cull"))
                material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Back);
            if (material.HasProperty("_AlphaClip"))
                material.SetFloat("_AlphaClip", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_ALPHAMODULATE_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// 程序化生成 64² 径向渐变贴图（白 RGB + 中心 α=1 → 边缘 α=0 的 smoothstep），落成 PNG 资产。
        ///
        /// 【为什么用贴图而不是顶点色】免贴图方案要靠 shader 读顶点色，而 URP/Unlit 不读顶点色、
        /// 自写 shader 又超出本文件允许改动的范围；64² 单通道遮罩成本可忽略，且贴图可手改替换。
        /// </summary>
        static Texture2D BuildContactShadowTexture()
        {
            const int size = 64;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size - 0.5f;
                    float v = (y + 0.5f) / size - 0.5f;
                    float d = Mathf.Clamp01(new Vector2(u, v).magnitude / 0.5f);  // 0=中心, 1=外接圆
                    float t = 1f - d;
                    float alpha = t * t * (3f - 2f * t);                          // smoothstep 径向渐变
                    pixels[y * size + x] = new Color32(255, 255, 255,
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "CrewContactShadow" };
            texture.SetPixels32(pixels);
            texture.Apply();

            string path = CrewTextureFolder + "/" + ContactShadowTextureFileName;
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;          // 贴地小面片，不需要 mip
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.sRGBTexture = false;            // 它是 α 遮罩：不做 sRGB→linear，否则渐变边缘会变硬
                importer.maxTextureSize = size;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Shader ResolveOutlineShader()
        {
            Shader outlineShader = Shader.Find(OutlineShaderName);
            if (outlineShader == null)
            {
                Debug.LogWarning("[CrewVisualPrefabBuilder] 未找到 shader " + OutlineShaderName
                    + "，退回 URP/Lit（角色将没有描边，描边验收会失败）。");
                outlineShader = Shader.Find("Universal Render Pipeline/Lit");
            }
            return outlineShader;
        }

        static Material LoadOrCreateMaterial(string path, string materialName, Shader shader)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = materialName };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (shader != null && material.shader != shader)
            {
                material.shader = shader;
            }
            return material;
        }

        /// <summary>
        /// 配置"单位描边材质"参数：本体色 + 描边三档（state0 不可见 / hover 白细 / selected 青粗虚线）。
        /// 参数值与 <c>M2BattleSceneSetup.EnsureOutlineMaterial</c> 保持同源（该方法是私有的，故此处镜像；
        /// 若那边调参，这里必须同步，口径见 docs/描边Shader调试.md）。
        /// </summary>
        static void ApplyOutlineUnitMaterial(Material material, Color baseColor)
        {
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", baseColor);

            // state=0 用兜底色，alpha=0 → 片元 discard，未选中的单位不顶青边。
            if (material.HasProperty("_OutlineColor"))
                material.SetColor("_OutlineColor", new Color(0.286f, 0.851f, 0.839f, 0f));
            if (material.HasProperty("_OutlineColorHover"))
                material.SetColor("_OutlineColorHover", new Color(1f, 1f, 1f, 0.22f));
            if (material.HasProperty("_OutlineColorSelected"))
                material.SetColor("_OutlineColorSelected", new Color(0.286f, 0.851f, 0.839f, 0.949f));

            if (material.HasProperty("_OutlineWidth"))
                material.SetFloat("_OutlineWidth", OutlineWidthSelected);
            if (material.HasProperty("_OutlineWidthHover"))
                material.SetFloat("_OutlineWidthHover", OutlineWidthHover);
            if (material.HasProperty("_OutlineWidthSelected"))
                material.SetFloat("_OutlineWidthSelected", OutlineWidthSelected);
            if (material.HasProperty("_OutlineState"))
                material.SetFloat("_OutlineState", 0f);
            if (material.HasProperty("_OutlineAlpha"))
                material.SetFloat("_OutlineAlpha", 1f);
            if (material.HasProperty("_OutlineExpandMode"))
                material.SetFloat("_OutlineExpandMode", 0f);
            if (material.HasProperty("_OutlineDistanceAttenuation"))
                material.SetFloat("_OutlineDistanceAttenuation", 0.4f);
            if (material.HasProperty("_DashSpeed"))
                material.SetFloat("_DashSpeed", 5f);
            if (material.HasProperty("_DashFrequency"))
                material.SetFloat("_DashFrequency", DashFrequencySelected);
            if (material.HasProperty("_DebugMode"))
                material.SetFloat("_DebugMode", 0f);
        }

        /// <summary>
        /// 核对预制体里每个**角色部件**（排除贴地接触阴影面片）的材质是否带 <c>_OutlineState</c>。
        ///
        /// 【为什么要有这一步】MPB 写不存在的属性**不报错**：一旦哪个部件被换成
        /// PirateSurface / URP-Lit 材质，描边会**静默**消失（外观上就是"这个部件选中不变青"）。
        /// 故在这里做一次建预制体期的硬核对，命中缺属性的部件就 <see cref="Debug.LogError"/> 点名列出
        /// （ArtGate/批处理会把它暴露出来），不让它溜到运行时。
        /// 【两件式下的期望】Body（CrewTeamCloth）+ Head（CrewWood）都走 PirateOutline，
        /// 唯一不带 <c>_OutlineState</c> 的是 ContactShadow（CrewContactShadow.mat，被本方法与 binder 一致地排除）。
        /// </summary>
        static void VerifyOutlineMaterials(GameObject root, string fileName)
        {
            int outlineStateId = Shader.PropertyToID("_OutlineState");
            MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);

            int missing = 0;
            string firstOffender = null;

            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer r = renderers[i];
                // 贴地接触阴影面片刻意不描边（与 UnitOutlineBinder 的排除口径一致，见该类的注释）。
                if (r == null || r.GetComponent<ContactShadowDecal>() != null)
                    continue;

                Material m = r.sharedMaterial;
                if (m != null && m.HasProperty(outlineStateId))
                    continue;

                missing++;
                if (firstOffender == null)
                    firstOffender = r.name + "（" + (m != null ? m.name : "空材质") + "）";
            }

            if (missing == 0)
                return;

            Debug.LogError("[CrewVisualPrefabBuilder] " + fileName + " 有 " + missing + "/"
                + renderers.Length + " 个角色部件的材质不含 _OutlineState，选中/悬停不会画出描边，首个："
                + firstOffender + "。两件式下 Body 应用 CrewTeamCloth、Head 应用 CrewWood"
                + "（ApplyOutlineUnitMaterial 生成的那批），否则会出现\"部分部件无青\"。");
        }

        static CrewVisualAssetSet BuildAssetSet(Dictionary<string, Mesh> meshes, Material[] materials)
        {
            return new CrewVisualAssetSet
            {
                Sphere = meshes[CrewMeshLibrary.Sphere],
                SphereSmall = meshes[CrewMeshLibrary.SphereSmall],
                Cylinder = meshes[CrewMeshLibrary.Cylinder],
                Capsule = meshes[CrewMeshLibrary.Capsule],
                Box = meshes[CrewMeshLibrary.Box],
                TorsoStandard = meshes[CrewMeshLibrary.TorsoStandard],
                TorsoWide = meshes[CrewMeshLibrary.TorsoWide],
                TorsoNarrow = meshes[CrewMeshLibrary.TorsoNarrow],
                TorsoRib = meshes[CrewMeshLibrary.TorsoRib],
                TorsoCaptain = meshes[CrewMeshLibrary.TorsoCaptain],
                BandanaStandard = meshes[CrewMeshLibrary.BandanaStandard],
                BandanaTight = meshes[CrewMeshLibrary.BandanaTight],
                BandanaLow = meshes[CrewMeshLibrary.BandanaLow],
                Tricorn = meshes[CrewMeshLibrary.Tricorn],
                CoatSkirt = meshes[CrewMeshLibrary.CoatSkirt],
                Hook = meshes[CrewMeshLibrary.HookMesh],
                Rib = meshes[CrewMeshLibrary.RibMesh],
                Beard = meshes[CrewMeshLibrary.Beard],
                HairClump = meshes[CrewMeshLibrary.HairClump],
                Materials = materials,
            };
        }

        // ------------------------------------------------------------------
        // 预制体
        // ------------------------------------------------------------------

        static GameObject BuildProfessionPrefab(CrewProfession profession, CrewVisualAssetSet assetSet,
            VisualAddOns addOns, out int rendererCount, out int triangles)
        {
            rendererCount = 0;
            triangles = 0;

            string fileName = CrewVisualCatalog.PrefabFileName(profession);
            string path = CrewPrefabFolder + "/" + fileName + ".prefab";

            var root = new GameObject("Crew_" + fileName);
            root.transform.localScale = UnitRootScale;

            var collider = root.AddComponent<BoxCollider>();
            collider.size = Vector3.one;      // §4.1：AABB 12×16px → 世界 0.375×0.5×0.375

            var body = root.AddComponent<Rigidbody>();
            body.mass = 1f;                   // §4.1 weight = 1
            body.drag = 0f;
            body.angularDrag = 0.05f;
            body.useGravity = true;

            var pirate = root.AddComponent<PirateBase>();
            var binder = root.AddComponent<UnitOutlineBinder>();
            var animator = root.AddComponent<CrewVisualAnimator>();

            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            CrewVisualRig rig = CrewVisualRig.Build(visual.transform, profession, assetSet);
            if (rig == null)
            {
                Debug.LogError("[CrewVisualPrefabBuilder] CrewVisualRig.Build 失败: " + fileName);
                Object.DestroyImmediate(root);
                return null;
            }

            // ---- 用户裁决 2026-09-14：塌缩成 Godot 两件式（圆球 + 圆台柱）----
            // 保留 rig.Build 的枢轴接线（Body/TorsoPivot/HeadPivot 与动画器共用），
            // 只把"零件"部分整体换掉：删掉 legs/boots/arms/hands/hats/face/held 后重建 Body + Head。
            ApplyGodotTwoPieceSilhouette(rig, assetSet, addOns);

            // N7：脚底接触阴影面片（地面贴片，非角色部件；单位根的子物体 → 跟随位移，零运行时代码）。
            AddContactShadow(root.transform, addOns);

            // 阵营色部件：显式写进 binder，并拆掉必然变成 missing script 的运行时标记组件。
            Renderer[] tintRenderers = CollectAndStripTintMarkers(root);
            if (tintRenderers.Length != 1)
                Debug.LogError("[CrewVisualPrefabBuilder] " + fileName + " 的阵营色部件数 = "
                    + tintRenderers.Length + "（两件式口径下恒为 1 = Body），请检查 "
                    + "ApplyGodotTwoPieceSilhouette 是否被改动。");

            // 统计实测三角面（用网格资产数据算，不靠估算）。
            var filters = root.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                Mesh mesh = filters[i].sharedMesh;
                if (mesh != null)
                    triangles += mesh.triangles.Length / 3;
            }

            // 部件 renderer 数（排除贴地接触阴影面片）：两件式下恒为 2（Body + Head），供判据 R-1 核对。
            var partRenderers = root.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < partRenderers.Length; i++)
            {
                if (partRenderers[i] != null && partRenderers[i].GetComponent<ContactShadowDecal>() == null)
                    rendererCount++;
            }
            if (rendererCount != 2)
                Debug.LogError("[CrewVisualPrefabBuilder] " + fileName + " 的角色部件 renderer 数 = "
                    + rendererCount + "（两件式口径下恒为 2 = Body + Head），请检查 "
                    + "ApplyGodotTwoPieceSilhouette 是否被改动。");

            // 建完立刻核一遍"每个角色部件的材质是否都带 _OutlineState"（缺的报错点名）。
            VerifyOutlineMaterials(root, fileName);

            // 接线（PirateBase.body / bodyCollider / 表现层引用）。
            var so = new SerializedObject(pirate);
            SetRef(so, "body", body);
            SetRef(so, "bodyCollider", collider);
            SetRef(so, "visualAnimator", animator);
            SetString(so, "crewType", RepresentativeSymbol(profession));
            so.ApplyModifiedPropertiesWithoutUndo();

            var animatorSo = new SerializedObject(animator);
            SetRef(animatorSo, "rig", rig);
            SetRef(animatorSo, "pirate", pirate);
            animatorSo.ApplyModifiedPropertiesWithoutUndo();

            // 描边 binder：显式阵营色部件表（见类头"阵营色接线"）。
            var binderSo = new SerializedObject(binder);
            SetRendererArray(binderSo, "teamTintRenderers", tintRenderers);
            binderSo.ApplyModifiedPropertiesWithoutUndo();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
            Object.DestroyImmediate(root);
            if (!success)
            {
                Debug.LogError("[CrewVisualPrefabBuilder] 保存预制体失败: " + path);
                return null;
            }
            return prefab;
        }

        // ------------------------------------------------------------------
        // 用户裁决 2026-09-14：两件式造型（圆台柱 Body + 圆球 Head）
        // ------------------------------------------------------------------

        /// <summary>
        /// 把 <see cref="CrewVisualRig.Build"/> 装配出的整套零件**塌缩成 Godot 两件式**：
        /// 一个圆台柱（Body，阵营色）+ 一个圆球（Head，木色），其余零件与臂/腿枢轴全部删除。
        ///
        /// 【为什么保留 rig.Build 再删】三个动画枢轴（BodyPivot / TorsoPivot / HeadPivot）的层级与序列化
        ///   接线由 rig 提供且被 <c>CrewVisualAnimator</c> 使用，重建一套层级反而容易漏接线；
        ///   这里沿用"rig 装配 + 后处理"的既有做法，只把零件层整体替换。
        ///
        /// 【尺寸与位置（逐值对照 pirate.tscn）】
        ///   · Body：<see cref="BodyFrustumKey"/> 网格按世界尺寸建模（顶 r 0.35 / 底 r 0.46 /
        ///     h 1.20），缩放恒为 1，中心放在柱高的中点 → 底面恰在脚底 y=0（Visual 局部 y=0）。
        ///   · Head：<see cref="HeadSphereKey"/> 网格半径 0.35，挂在 HeadPivot 下的原点 →
        ///     球心 y 1.50，球底 1.15 与柱顶 1.20 微叠 0.05（与 Godot 的 0.85−0.7/2−1.2/2 = 0.05 一致）。
        ///   · 枢轴归位：TorsoPivot 移到脚底（呼吸缩放/前倾都绕脚底，语义与原来一致），
        ///     HeadPivot 移到球心高度；BodyPivot 保持脚底（与 rig 建的一致）。
        ///
        /// 【动画影响】<c>CrewVisualAnimator</c> 用 <c>rig.ArmLPivot</c> 等做手臂/腿摆动，两件式下这些
        ///   引用为 null → 该组件已有 null 判断，行为退化为"无摆臂/无迈腿"，整身 bob / 前倾 / 倒地 /
        ///   落水下沉 / 呼吸全部照常（它们只依赖 Body/TorsoPivot/HeadPivot）。
        /// </summary>
        static void ApplyGodotTwoPieceSilhouette(CrewVisualRig rig, CrewVisualAssetSet assets, VisualAddOns addOns)
        {
            Transform visual = rig.transform;
            Transform bodyPivot = rig.Body;
            Transform torsoPivot = rig.TorsoPivot;
            Transform headPivot = rig.HeadPivot;

            if (visual == null || bodyPivot == null || torsoPivot == null || headPivot == null
                || addOns.BodyFrustum == null || addOns.HeadSphere == null)
            {
                Debug.LogError("[CrewVisualPrefabBuilder] 两件式装配缺枢轴或网格"
                    + "（Body/TorsoPivot/HeadPivot 或 " + BodyFrustumKey + "/" + HeadSphereKey
                    + "），本预制体的零件层保持 rig 原样。");
                return;
            }

            // ---- 1) 只留动画需要的三个枢轴，其余零件与枢轴全删（幂等重跑也安全）----
            // 顺序：先删最深层的 HeadPivot 子件，再删 TorsoPivot 下除 HeadPivot 的件，
            // 再删 BodyPivot 下除 TorsoPivot 的件，最后清 Visual 下除 BodyPivot 的件。
            ClearChildren(headPivot);                    // 头球 / 头巾 / 三角帽 / 鼻 / 眼 / 须 / 发
            StripChildrenExcept(torsoPivot, headPivot);  // 躯干 / 腰带 / 双臂 / 围裙 / 肩章 / 肋骨 / 大衣下摆…
            StripChildrenExcept(bodyPivot, torsoPivot);  // 双腿 / 靴 / 木腿 / 抱弹 / 下摆
            StripChildrenExcept(visual, bodyPivot);      // Visual 下只留 BodyPivot

            // 枢轴改名：让"Body"这个名字专属于圆台柱 renderer（teamTintRenderers = [Body]）。
            bodyPivot.name = "BodyPivot";

            // ---- 2) 枢轴归位（Visual 局部 y = 脚底起算的世界 y，见类头坐标口径）----
            torsoPivot.localPosition = Vector3.zero;
            torsoPivot.localRotation = Quaternion.identity;
            torsoPivot.localScale = Vector3.one;

            headPivot.localPosition = new Vector3(0f, HeadSphereCenterY, 0f);
            headPivot.localRotation = Quaternion.identity;
            headPivot.localScale = Vector3.one;

            // ---- 3) Body 圆台柱（阵营色：建时挂 CrewTeamTintPart 标记，随后被写进 binder 并删掉）----
            AddSimplePart(torsoPivot, "Body", addOns.BodyFrustum,
                assets.For(CrewMaterialRole.TeamCloth),
                new Vector3(0f, BodyHeight * 0.5f, 0f), Vector3.one, teamTint: true);

            // ---- 4) Head 圆球（Godot cel_wood = 木色 #D4A76A）----
            AddSimplePart(headPivot, "Head", addOns.HeadSphere,
                assets.For(CrewMaterialRole.Wood),
                Vector3.zero, Vector3.one);
        }

        /// <summary>按组装侧 <c>CrewVisualRig.AddPart</c> 的同口径补一个零件。</summary>
        static MeshRenderer AddSimplePart(Transform parent, string name, Mesh mesh, Material material,
            Vector3 localPosition, Vector3 localScale, bool teamTint = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = localScale;

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;

            if (teamTint)
                go.AddComponent<CrewTeamTintPart>();

            return renderer;
        }

        /// <summary>删掉 <paramref name="parent"/> 下除 <paramref name="keep"/> 以外的所有直接子物体。</summary>
        static void StripChildrenExcept(Transform parent, Transform keep)
        {
            var doomed = new List<GameObject>();
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child != keep)
                    doomed.Add(child.gameObject);
            }

            for (int i = 0; i < doomed.Count; i++)
                Object.DestroyImmediate(doomed[i]);
        }

        /// <summary>清空直接子物体。</summary>
        static void ClearChildren(Transform parent)
        {
            StripChildrenExcept(parent, null);
        }

        // ------------------------------------------------------------------
        // N7：脚底接触阴影面片
        // ------------------------------------------------------------------

        /// <summary>
        /// 在单位根下挂"ContactShadow"面片（判据 A-6 / Art Bible §4.1 `:239`）。
        ///
        /// 【贴地口径】单位根的 local y = −0.5 就是脚底平面：脚底 world y = rootY + rootScale.y × Visual.localY
        /// = rootY − 0.25，而 rootScale.y = 0.5 → 局部 −0.5。出生逻辑把单位摆在平台顶面，故这里固定
        /// −0.5 + 0.02/0.5（抬高 0.02 世界单位防 z-fighting），不需要每帧查询地形高度。
        ///
        /// 【缩放口径】根是非均匀缩放（0.375/0.5/0.375）；面片绕 X 转 −90° 平铺后，面内 X/Z 都被 0.375 缩放，
        /// 故 X/Y 用同一系数 → 仍是正圆，且不产生剪切（旋转只把局部 Y 轴映射到世界 Z 轴）。
        /// </summary>
        static MeshRenderer AddContactShadow(Transform root, VisualAddOns addOns)
        {
            var go = new GameObject("ContactShadow");
            go.transform.SetParent(root, false);

            float planarScale = ContactShadowDiameter / UnitRootScale.x;
            go.transform.localPosition = new Vector3(0f,
                -0.5f + ContactShadowDecal.DefaultGroundOffset / UnitRootScale.y, 0f);
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            go.transform.localScale = new Vector3(planarScale, planarScale, 1f);

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = addOns.ContactShadowQuad;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = addOns.ContactShadowMaterial;
            // 面片自身不投影、不接收阴影/探针：它只是一层"压暗"叠加，不参与光照链路。
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            var decal = go.AddComponent<ContactShadowDecal>();
            var decalSo = new SerializedObject(decal);
            SetFloat(decalSo, "diameter", ContactShadowDiameter);
            SetFloat(decalSo, "groundOffset", ContactShadowDecal.DefaultGroundOffset);
            SetFloat(decalSo, "centerAlpha", ContactShadowColor.a);
            decalSo.ApplyModifiedPropertiesWithoutUndo();

            return renderer;
        }

        // ------------------------------------------------------------------
        // 阵营色接线（修 missing script 根因）
        // ------------------------------------------------------------------

        /// <summary>
        /// 收集"阵营色部件"的 renderer 并**删除标记组件**，返回的列表写进
        /// <c>UnitOutlineBinder.teamTintRenderers</c>。
        ///
        /// 【为什么要删标记】<see cref="CrewTeamTintPart"/> 定义在 <c>CrewVisualRig.cs</c> 里，
        /// 而 Unity 的脚本导入器只给"类名 == 文件名"的类生成 MonoScript 子资产；标记类拿不到
        /// MonoScript，序列化时 <c>m_Script</c> 只能写 fileID 0 —— 进预制体就是 missing script
        /// （控制台报 "referenced script is missing"、运行时 <c>GetComponentsInChildren</c> 收不到）。
        /// 实测 7 个预制体 26 处，是 r3「蓝队顶着红队底色」的根因。
        /// 删掉它、改走显式引用后，预制体不再有 missing script，也不再依赖运行时类型。
        ///
        /// 【两件式下的期望】恰好 1 个（Body）；<see cref="BuildProfessionPrefab"/> 会断言这一点。
        /// </summary>
        static Renderer[] CollectAndStripTintMarkers(GameObject root)
        {
            var markers = root.GetComponentsInChildren<CrewTeamTintPart>(true);
            var tintRenderers = new List<Renderer>(markers.Length);
            for (int i = 0; i < markers.Length; i++)
            {
                var renderer = markers[i].GetComponent<Renderer>();
                if (renderer != null)
                    tintRenderers.Add(renderer);
                Object.DestroyImmediate(markers[i]);
            }
            return tintRenderers.ToArray();
        }

        /// <summary>职业的默认战斗导出符号（运行时由 PirateBase.Initialize 覆盖，仅作 prefab 默认值）。</summary>
        static string RepresentativeSymbol(CrewProfession profession)
        {
            switch (profession)
            {
                case CrewProfession.Sailor: return "redPirate";
                case CrewProfession.Bombardier: return "soldier";
                case CrewProfession.Sniper: return "femalePirate";
                case CrewProfession.Hook: return "blindPirate";
                case CrewProfession.Arsonist: return "oldPirate";
                case CrewProfession.Skeleton: return "skeletonPirate";
                case CrewProfession.Captain: return "redPirateCaptain";
                default: return "redPirate";
            }
        }

        // ------------------------------------------------------------------
        // 工具
        // ------------------------------------------------------------------

        static void SetRef(SerializedObject so, string name, Object value)
        {
            SerializedProperty prop = so.FindProperty(name);
            if (prop == null)
            {
                Debug.LogError("[CrewVisualPrefabBuilder] 找不到序列化字段: " + name);
                return;
            }
            prop.objectReferenceValue = value;
        }

        static void SetString(SerializedObject so, string name, string value)
        {
            SerializedProperty prop = so.FindProperty(name);
            if (prop != null)
                prop.stringValue = value;
        }

        static void SetFloat(SerializedObject so, string name, float value)
        {
            SerializedProperty prop = so.FindProperty(name);
            if (prop != null)
                prop.floatValue = value;
        }

        static void SetRendererArray(SerializedObject so, string name, Renderer[] value)
        {
            SerializedProperty prop = so.FindProperty(name);
            if (prop == null)
            {
                Debug.LogError("[CrewVisualPrefabBuilder] 找不到序列化数组字段: " + name);
                return;
            }
            prop.arraySize = value.Length;
            for (int i = 0; i < value.Length; i++)
                prop.GetArrayElementAtIndex(i).objectReferenceValue = value[i];
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);

            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
