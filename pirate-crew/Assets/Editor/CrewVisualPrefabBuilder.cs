using System.Collections.Generic;
using System.IO;
using PirateCrew.PirateCrew.Battle;
using PirateCrew.PirateCrew.Visual;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 【波次 I2 填充】角色视觉预制体生成器：程序化产出 7 个职业预制体（六职业 + 船长），
    /// 配套网格资产（<c>Assets/Art/Models/Generated/</c>）与共享材质（<c>Assets/Art/Materials/Crew/</c>）。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/角色/生成职业视觉预制体
    ///   无头: -batchmode -nographics -quit -executeMethod PirateCrew.EditorTools.CrewVisualPrefabBuilder.BuildAll
    ///
    /// 【预制体结构】（docs/角色造型规范.md §5 接线要求）
    /// <code>
    /// Crew_&lt;Profession&gt;（根）
    /// ├ Transform.scale = (0.375, 0.5, 0.375)   ← 与 PirateBase.prefab 一致
    /// ├ BoxCollider size=(1,1,1)                  ← 世界 AABB 0.375×0.5×0.375（碰撞契约不变）
    /// ├ Rigidbody mass=1 drag=0 angularDrag=0.05 useGravity=on
    /// ├ PirateBase / UnitOutlineBinder / CrewVisualAnimator
    /// ├ ContactShadow（接触阴影面片，y=−0.46 → 脚底上方 0.02；见 §N7）
    /// └ Visual（CrewVisualRig + 各部件 renderer）
    /// </code>
    ///
    /// 【为什么不替换 PirateBase.prefab 的 Cube 外观】<c>M2BattleSceneSetup.BuildAll</c> 会重建
    /// PirateBase.prefab 为 Cube；把职业外观塞进去会与该重建互相覆盖。这里改为**独立职业预制体**，
    /// 由 <c>BattleController.crewVisualPrefabs</c> 按 <c>CrewVisualCatalog</c> 映射选择，
    /// 未命中/未接线时回落 PirateBase.prefab（对方块外观做兜底，不破坏既有场景）。
    ///
    /// 【幂等】
    ///   · 网格资产存在则**就地刷新**（<c>ApplyToMesh</c>，不换 GUID、不丢引用）；
    ///   · 材质存在则就地更新参数（不新建重名副本）；
    ///   · 预制体按固定路径覆盖保存。
    ///
    /// 【本脚本承担的三处 r3 复验整改】
    ///   N7 接触阴影：脚底半球透明径向渐变面片（贴图 64² 程序化生成，中心 α=0.45），
    ///      挂在**单位根**下名为 <c>ContactShadow</c> → 跟随位移，零运行时代码（判据 A-6）。
    ///   N19 四肢写实化：腿由"等径圆柱"改**上粗下细圆台**（8 段）+ 加厚前出头靴块；
    ///      臂同样锥化，手由小球改**手掌块**；靴子另给深棕材质 <c>CrewBoot</c>（骷髅的骨色靴不动）。
    ///      做法是装配后只换 <c>MeshFilter.sharedMesh</c> 与零件缩放，**不动枢轴层级**
    ///      → <c>CrewVisualAnimator</c> 的动画链路零影响（R-3 三角面反而下降：圆台 32 tri < 圆柱 40 tri）。
    ///   阵营色接线：<c>CrewTeamTintPart</c> 与 <c>CrewVisualRig</c> 同处一个 .cs，Unity 只给
    ///      "类名 == 文件名"的类生成 MonoScript → 该标记进预制体必然变成 **missing script**（实测 7 个
    ///      预制体共 26 处），运行时一个都收不到，蓝队会顶着红队底色。故建预制体时把标记对应的 renderer
    ///      列表**显式写进 <c>UnitOutlineBinder.teamTintRenderers</c>**，并把标记组件删掉（不再留 missing script）。
    ///
    /// 【本脚本承担的三处复验整改（① ② 为 r5，③ 为 r5 起草 / r6 按复验重做）】
    ///   ① 描边覆盖（选中青虚线"r3 862px → r4 311px"）：归因结论是**选中虚线的周期**而不是 binder
    ///      的收集口径——r3/r4 同机位图里单位像素级同形同址（红帽 bbox 都是 x896-1023 / y552-817），
    ///      13 个 Crew 材质的 <c>m_Shader</c> 全是 PirateOutline 且都带 <c>_OutlineState</c>，
    ///      预制体里唯一的显式数组 <c>teamTintRenderers</c> 只管阵营色。真正的变量是 phase 里的
    ///      <c>_Time.y × _DashSpeed</c>：`_DashFrequency=50` 在 1080p 下 ON/OFF 各 34px，
    ///      比靴(15px)/腰带(16px)这类小件还长 → 小件可能整件落在 OFF 带里（判据"某部件 0 青色"）。
    ///      故把 <c>_DashFrequency</c> 提到 <see cref="DashFrequencySelected"/>（推导见该常量注释）。
    ///      r6 复验补充：帽/头/臂仍只有 0~1px 青——**不是材质/收集问题**（7 个预制体的
    ///      部件→材质 guid 对照见 docs/描边Shader调试.md 或 r6 报告，全部命中带 _OutlineState 的 12 个
    ///      Crew 材质，唯 ContactShadow 例外且被 binder 显式排除），而是**屏幕空间法线外扩在特写
    ///      距离下只有 ~1px**（R_border ≈ width·w^−0.4，w≈2 时 ≈0.0045NDC≈2.4px，扣掉被本体遮挡的
    ///      部分后可见约 1px）。故把 <c>_OutlineWidthSelected</c> 由 0.006 提到
    ///      <see cref="OutlineWidthSelected"/>（见常量注释与 <see cref="ApplyOutlineUnitMaterial"/>）。
    ///   ② 脚下灰色刀片状碎片：根因是**手持武器的铁刀片穿到脚底平面以下**，不是接触阴影片
    ///      （阴影片是 y=+0.02 的水平 quad、近黑；碎片实测取色 (70,67,61) = CrewIron 0.431/0.416/0.388
    ///      在阴影下的值，形状是 0.014×0.150 的竖直薄板）。见 <see cref="ApplyHeldWeaponFloorFit"/>。
    ///   ③ 帽子/脸：**所有布帽件（Bandana/Tricorn）**换成三角帽网格（压扁帽冠 + 高上翻三檐、
    ///      檐外缘 1.05×头半径）、删掉读作"肉瘤"的左侧 <c>BandanaKnot</c>、鼻长减半并贴回头球面。
    ///      r5 只改了名为 <c>Tricorn</c> 的船长件，非船长特写主体的下翻圆台头巾没被覆盖 →
    ///      复验仍读作"红桶帽"。r7 按 r6 复验重算几何：檐半径 1.30→**1.05**×头半径、
    ///      上翻角 34°→**46°**、冠高 0.44→**0.34**×头半径、每片檐径向**收短 14%**。
    ///      见 <see cref="ApplyHeadDetails"/> 与 <see cref="BuildTricornFlatMeshData"/>。
    ///   三者都只改建预制体时的网格/参数/材质常量，不动 shader、不动运行时链路。
    ///
    /// 【发光说明】火把/火焰用 <c>Flame</c> 材质的高亮 base color（#FF7A1A）借 HDR + Bloom 出光。
    ///   PirateOutline shader 没有 Emission 通道，且本波次禁止改 shader（另一 agent 在改 ShadowCaster），
    ///   故不引入 URP/Lit 发光材质，避免"火焰没有描边"破坏 R-5。
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
        // 本波次追加资产（键名只在本脚本内使用；**不登记进 CrewMeshLibrary**，
        // 以免动到预算镜像表 CrewMeshLibrary.CountPartInstances 的既有断言）
        // ------------------------------------------------------------------

        const string LegTaperKey = "CrewLegTaper";
        const string ArmTaperKey = "CrewArmTaper";
        const string ContactShadowQuadKey = "CrewContactShadowQuad";
        const string TricornFlatKey = "CrewTricornFlat";

        const string BootMaterialFileName = "CrewBoot";
        const string ContactShadowMaterialFileName = "CrewContactShadow";
        const string ContactShadowTextureFileName = "CrewContactShadow.png";

        /// <summary>四肢锥化分段（8 段足够低模，且比原圆柱的 10 段更省面）。</summary>
        const int LimbTaperSides = 8;

        /// <summary>腿锥化：上（胯）半径系数，配装配侧 (r, h, r) 缩放 → 上半径 = r。</summary>
        const float LegTaperTopRadius = 1.0f;

        /// <summary>腿锥化：下（脚踝）半径系数 → 下半径 = 0.72r（"上粗下细"，替换原等径圆柱的"细棍"观感）。</summary>
        const float LegTaperBottomRadius = 0.72f;

        /// <summary>臂锥化：下（腕）半径系数 → 腕半径 = 0.78r。</summary>
        const float ArmTaperBottomRadius = 0.78f;

        /// <summary>靴块高度（世界单位；原 0.028）。</summary>
        const float BootHeight = 0.034f;

        /// <summary>靴块宽 = 腿半径 × 该系数（原 2.4）。</summary>
        const float BootWidthMul = 2.9f;

        /// <summary>靴块深 = 腿半径 × 该系数（原 3.4）——加深是"靴"而非"脚垫"的关键。</summary>
        const float BootDepthMul = 4.1f;

        /// <summary>靴尖前伸（世界单位，+Z 为角色正面）。</summary>
        const float BootForward = 0.014f;

        const float HandWidthMul = 2.2f;
        const float HandHeightMul = 1.7f;
        const float HandDepthMul = 1.85f;

        // ------------------------------------------------------------------
        // r5 复验参数（描边覆盖 / 三角帽 / 脸 / 手持物离地）
        // ------------------------------------------------------------------

        /// <summary>
        /// 选中虚线的屏幕空间频率（写入所有 Crew 材质的 <c>_DashFrequency</c>）。
        ///
        /// 【换算】phase = NDC.y·F + Time.y·_DashSpeed，NDC.y 跨 2 个单位对应 H 像素 →
        ///   虚线周期 = π·H/F 像素、ON/OFF 各半（1080p：F=50 → 68px 周期 / 各 34px；F=150 → 23px / 各 11px）。
        /// 【为什么是**提高**而不是降低】判据要求"选中态下头/帽/躯干/臂/腿/靴**全部部件**出现青虚线段"。
        ///   ON/OFF 只由屏幕 Y 决定，一个高 h 像素的部件可能**整件落在 OFF 带里**
        ///   （概率 ≈ max(0, (OFF − h)/周期)）。要保证每件都跨到一段 ON，必须让 OFF 短于最小部件
        ///   （靴 ~15px、腰带 ~16px）→ F ≥ π·1080/(2·13) ≈ 130。取 150 留余量。
        ///   F=50 时 OFF=34px：15~16px 的小件有约 45% 概率整件无青——这正是 r4 实测
        ///   "头/帽/臂/腿 0 青色"的来源；且周期 68px 与单位屏幕尺寸（广角 ~26px、特写 ~265px）
        ///   同量级，一圈轮廓只落 0~1 段，观感是"角落两三段短线"而非一圈虚线
        ///   （docs/描边Shader调试.md §三 已记录该现象）。F=150 时周期 23px ≪ 单位尺寸 →
        ///   轮廓上恒有 3~11 段虚线，读作完整的一圈点划描边。
        /// 【同步要求】<c>M2BattleSceneSetup.EnsureOutlineMaterial</c> 持同源参数镜像（含 _DashFrequency）。
        ///   改这里必须同步那边，否则旧单立方体兜底材质仍是 68px 周期。
        /// </summary>
        const float DashFrequencySelected = 150f;

        /// <summary>
        /// 选中态描边宽度（写入所有 Crew 材质的 <c>_OutlineWidthSelected</c>；模式 0 = 屏幕空间恒定粗细）。
        ///
        /// 【r6 由 0.006 提到 0.010 的原因】r5 复验仍然只有躯干/腿有青，帽 ~1px、臂 0px；
        ///   YAML 取证已排除材质与收集（12 个 Crew 材质全带 <c>_OutlineState</c>，7 个预制体的
        ///   部件→材质 guid 全部命中，唯 ContactShadow 例外且被 binder 显式排除），剩下的是
        ///   **外扩量太小**：模式 0 下屏幕上单边宽 ≈ `width · (positionCS.w)^(1−_OutlineDistanceAttenuation) / w`
        ///   → ≈ `width · w^(−0.4)`；特写机位 w≈2 时 0.006 → ≈0.0045 NDC ≈ **2.4px**，
        ///   扣掉被本体自遮挡的部分后可见只剩 ~1px，再被虚线的 ON/OFF(各 11px) 截断，
        ///   小部件（帽/头/臂）的青色像素就掉到 0~1。放大到 0.010 → ≈4px（可见 ~2px），
        ///   与躯干/腿同量级（docs/描边Shader调试.md §三 的推荐区间 0.002~0.012 内）。
        /// 【同步要求】<c>M2BattleSceneSetup.EnsureOutlineMaterial</c> 持同源镜像（含 _OutlineWidthSelected）；
        ///   那个方法只服务旧单立方体兜底材质，本次未同步（不在 r6 文件域内），已在 r6 报告登记。
        /// </summary>
        const float OutlineWidthSelected = 0.010f;

        /// <summary>悬停描边宽度：与选中等比（0.0025 → 0.0042），保持"悬停比选中细"的既有语义。</summary>
        const float OutlineWidthHover = OutlineWidthSelected * 0.42f;

        /// <summary>
        /// 脚底平面的世界 y：单位根摆在原点时 = rootScale.y × (−0.5)。
        /// 只有"根在原点、无旋转"的建预制体阶段成立，故仅供本脚本的离地检查用。
        /// </summary>
        static readonly float FootPlaneWorldY = -UnitRootScale.y * 0.5f;

        /// <summary>手持武器最低点相对脚底平面的留白（世界单位，≈0.03px@广角）。</summary>
        const float HeldWeaponFloorClearance = 0.014f;

        /// <summary>穿地修正后允许的最短刀身（避免"武器缩没了"；再不够就靠缩短+告警收口）。</summary>
        const float MinHeldBladeLength = 0.046f;

        /// <summary>
        /// 三角帽帽冠高 = 基准头半径 × **0.34**（r7：0.44 → 0.34）。
        /// r6 复验"冠顶反而高于檐顶"（应檐上缘高于冠顶）——压低冠高，让高上翻的檐缘抬到冠顶之上；
        /// 0.34×0.0775 ≈ 0.0264，仍满足规范"冠高 ≤ 0.45×头径"（= 0.45×2×0.0775 ≈ 0.0698）。
        /// </summary>
        static readonly float TricornCrownHeight = ReferenceHeadRadius * 0.34f;

        /// <summary>三角帽帽冠底口半径（与原 Tricorn 的 crownRadius 0.048 一致，保证坐在头球上无缝）。</summary>
        const float TricornCrownBottomRadius = 0.048f;

        /// <summary>三角帽帽冠顶口半径（上窄下宽，规范 §5.1 禁上下等径）。</summary>
        const float TricornCrownTopRadius = 0.036f;

        /// <summary>
        /// 三角帽檐**外缘半径**（世界单位）= 基准头半径 × **1.05**（r7：1.30 → 1.05）。
        /// 【为什么收】**r6 复验**：帽宽/头高实测 1.7-1.9（目标 ≤1.35）——1.30×头半径的三片檐
        /// 把剪影撑成一顶宽檐桶帽，且右檐甩出细长红三角。<b>几何核对（按帽冠底口半径 0.048、
        /// 上翻 46°、檐片再收短 14% 计算）</b>：三檐最大水平半径 0.0891（= 1.150×头半径），
        /// 帽宽/头径 = 1.15（旧值 1.472）；檐缘上缘 y=+0.0332 已高于冠顶 y=0.0264（差 +0.0068），
        /// 满足"檐上缘高于冠顶"。
        /// 本值只决定"上翻后外缘应落在哪"→ 反解檐长（见 <see cref="BuildTricornFlatMeshData"/>）；
        /// 檐片再乘 <see cref="TricornFlapShorten"/> 收短消尖角。
        /// </summary>
        static readonly float TricornBrimRadius = ReferenceHeadRadius * 1.05f;

        /// <summary>
        /// 三角帽三片檐的上翻角（度）。r7：34° → **46°**（取复验给定的 42-48° 中值）。
        /// 【为什么翻更高】r6 复验"冠顶反而高于檐顶"要改成"檐上缘高于冠顶"：上翻角越大，檐缘抬得越高
        /// （檐外缘 y ≈ 檐长×sin(角)），且径向水平外扩越小 → 同时帮到帽宽判据。
        /// 几何核对：46° 下檐缘上缘 y=+0.0332 > 冠顶 y=0.0264。
        /// </summary>
        const float TricornFoldDegrees = 46f;

        /// <summary>
        /// 三角帽檐片**径向长度收缩系数**（r7 新增）：檐片长度（圆心→檐尖）再乘 (1 − 该值) = **0.86（收 14%）**。
        /// 【为什么收】r6 复验"右檐甩细长红三角"——檐片是薄盒，其**外缘两角的对角**是剪影上最远的点，
        /// 正是那根细长尖角。沿径向收短把对角一起收回（三檐最大水平半径 0.0933→0.0891）。
        /// 【为什么不会在帽冠处开裂】檐片**内缘**位置只由帽冠底口半径 0.048 与上翻角决定
        /// （推导见 <see cref="BuildTricornFlatMeshData"/>），与檐长无关；收短只动外端。
        /// </summary>
        const float TricornFlapShorten = 0.14f;

        /// <summary>三角帽檐厚（闭合盒，保证薄配件描边完整；规范 R-9 禁单面 Quad）。</summary>
        const float TricornBrimThickness = 0.010f;

        /// <summary>鼻尖相对头心的高度偏移（略低于赤道）。</summary>
        const float NoseHeightOffset = -0.006f;

        /// <summary>
        /// 鼻尖半径系数（× 头半径 → 长度）。r6 由 0.22 砍到 0.11（**缩 50%**）：
        /// r5 实测鼻尖凸出头球 0.36×头半径（≈18px@特写），被读作"鸟喙"。
        /// </summary>
        const float NoseLengthMul = 0.11f;

        /// <summary>眼下移后的高度（世界单位，正比头径缩放）——原 +0.008 偏上，脸显得"眼睛过曝在空白上"。</summary>
        const float EyeHeightOffset = -0.010f;

        /// <summary>基准头半径（§1.2 的 0.0775）；眼/鼻位置按 <c>当前头半径/该值</c> 等比换算。</summary>
        const float ReferenceHeadRadius = 0.0775f;

        /// <summary>深棕靴色 #4A3021【提案】（N19："深棕靴"；比 Leather #8B5E3C 更暗，与沙地拉开明度）。</summary>
        static readonly Color BootColor = new Color(0.290f, 0.188f, 0.129f, 1f);

        /// <summary>接触阴影面片色：近黑 + 中心 α 0.45（边缘 α 由贴图径向渐变收到 0）。</summary>
        static readonly Color ContactShadowColor = new Color(0.015f, 0.015f, 0.020f,
            ContactShadowDecal.DefaultCenterAlpha);

        // ------------------------------------------------------------------
        // 追加资产打包
        // ------------------------------------------------------------------

        /// <summary>本波次新增的网格/材质句柄（生成后注入 <see cref="BuildProfessionPrefab"/>）。</summary>
        struct VisualAddOns
        {
            public Mesh LegTaper;
            public Mesh ArmTaper;
            public Mesh ContactShadowQuad;
            public Mesh TricornFlat;
            public Material BootMaterial;
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
            report.AppendLine("[CrewVisualPrefabBuilder] 职业视觉预制体生成完成：");
            report.AppendLine("  网格资产: " + MeshFolder + "（" + meshes.Count + " 个）");
            report.AppendLine("  材质资产: " + CrewMaterialFolder + "（" + materials.Length + " 个 + 靴/接触阴影 2 个）");

            int failures = 0;
            for (int i = 0; i < CrewVisualCatalog.AllProfessions.Length; i++)
            {
                CrewProfession profession = CrewVisualCatalog.AllProfessions[i];
                GameObject prefab = BuildProfessionPrefab(profession, assetSet, addOns,
                    out int renderers, out int triangles, out int heldFixes);
                if (prefab == null)
                {
                    failures++;
                    report.AppendLine("  [" + CrewVisualCatalog.DisplayName(profession) + "] 生成失败");
                    continue;
                }

                report.AppendLine("  [" + CrewVisualCatalog.DisplayName(profession) + " / " + profession + "] "
                    + "renderer=" + renderers
                    + " tri=" + triangles
                    + "（预算 " + CrewMeshFactory.MaxTrianglesPerUnit + "）"
                    + (triangles <= CrewMeshFactory.MaxTrianglesPerUnit ? " OK" : " **超预算**")
                    + (heldFixes > 0 ? " 手持物离地修正=" + heldFixes + " 处" : ""));
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (failures > 0)
                Debug.LogError(report.ToString());
            else
                Debug.Log(report.ToString());
        }

        // ------------------------------------------------------------------
        // 追加网格（N7 / N19）
        // ------------------------------------------------------------------

        /// <summary>
        /// 往网格表里追加本波次新增的零件：
        /// · 腿/臂"上粗下细"圆台——与单位 <c>Cylinder</c> 同口径（半径 1 / 高 1、沿 Y 居中），
        ///   所以装配侧既有的 (r, h, r) 缩放、枢轴位置、动画插值全部无需改动，只换 sharedMesh。
        /// · 接触阴影面片——1×1 的 XY 面（法线 +Z），预制体里绕 X 转 −90° 平铺，再缩放到 0.6 单位。
        /// </summary>
        static void InjectAddOnMeshes(Dictionary<string, MeshData> meshData)
        {
            meshData[LegTaperKey] = CrewMeshFactory.Frustum(
                LegTaperTopRadius, LegTaperBottomRadius, 1f, LimbTaperSides);
            meshData[ArmTaperKey] = CrewMeshFactory.Frustum(
                LegTaperTopRadius, ArmTaperBottomRadius, 1f, LimbTaperSides);
            meshData[ContactShadowQuadKey] = BuildQuadMeshData();
            meshData[TricornFlatKey] = BuildTricornFlatMeshData();
        }

        /// <summary>
        /// 三角帽塑形（r5 起草 / r6 按复验判据重算几何 / r7 收口）：压扁帽冠 + 三片上翻帽檐。
        ///
        /// 【坐标系】帽冠底口在 y=0（装配侧把它摆在 <c>HeadPivot + HeadRadius×0.80</c>，
        /// 底口半径 0.048 正好等于头球在该高度的截面半径 → 坐下无缝）。
        /// 【r7 檐片几何（复验判据：帽宽/头高 ≤1.35、檐上缘高于冠顶、消细长尖角）】
        ///   檐片是薄盒，先平放（内缘在帽冠底口 x=<see cref="TricornCrownBottomRadius"/> 处），
        ///   再整体绕 Z **上翻** <see cref="TricornFoldDegrees"/>°（= 46°，r7 由 34° 抬高）。旋转把内缘从
        ///   (inner, 0) 转到 (0.048cosf, 0.048sinf)，故装配时先给 y 一个 −0.048·sinf 的补偿，
        ///   使**上翻后的内缘恰好回到帽冠底口平面 y=0**（檐片不会插进头球；该位置与檐长无关，
        ///   故檐片收短不会在帽冠处开裂）。
        ///   上翻后的外缘轴向 x = 0.048 + L·cosf（L = 檐片长），令其等于
        ///   <see cref="TricornBrimRadius"/> 反解 L = (R_brim − 0.048)/cosf，再乘
        ///   (1 − <see cref="TricornFlapShorten"/>) 收短 14% 消掉外缘对角尖角。
        ///   几何核对：R_brim = 1.05×头半径、46°、收 14% → 三檐最大水平半径 0.0891（1.150×头半径），
        ///   檐缘上缘 y ≈ +0.0332 高于冠顶 y = 0.0264（r7 冠高 0.34×头半径）→ 剪影即三角帽。
        /// 【朝向】三片 120° 均布，并整体绕 Y 转 −90° → 一片正对**前方 +Z**、两片朝后侧，
        ///   与真实三角帽的"前一后二"角位一致（0/120/240 会转成"一右两左"的歪剪影）。
        /// 【为什么用盒做檐】闭合几何才有完整描边（规范 R-9 明令薄配件禁单面 Quad）。
        /// </summary>
        static MeshData BuildTricornFlatMeshData()
        {
            MeshData crown = CrewMeshFactory.Lathe(
                new[]
                {
                    new Vector2(TricornCrownBottomRadius, 0f),
                    new Vector2(TricornCrownTopRadius, TricornCrownHeight),
                },
                10, capBottom: true, capTop: true);

            float fold = TricornFoldDegrees;
            float cosF = Mathf.Cos(fold * Mathf.Deg2Rad);
            float sinF = Mathf.Sin(fold * Mathf.Deg2Rad);

            // 上翻后外缘落到 TricornBrimRadius，再收短 TricornFlapShorten 消对角尖角；
            // 内缘上翻后回到帽冠底口平面（与檐长无关，见方法头推导）。
            float flapLength = (TricornBrimRadius - TricornCrownBottomRadius) / cosF
                               * (1f - TricornFlapShorten);
            float flapCenterX = TricornCrownBottomRadius * cosF + flapLength * 0.5f;
            float flapCenterY = -TricornCrownBottomRadius * sinF;

            MeshData flap = CrewMeshFactory.Box(
                new Vector3(flapLength, TricornBrimThickness, TricornBrimRadius * 0.95f));
            flap = CrewMeshFactory.Translate(flap, new Vector3(flapCenterX, flapCenterY, 0f));
            flap = CrewMeshFactory.RotateZ(flap, fold);

            var parts = new List<MeshData>(4) { crown };
            for (int k = 0; k < 3; k++)
                parts.Add(CrewMeshFactory.RotateY(flap, k * 120f - 90f));
            return MeshData.Combine(parts.ToArray());
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
                LegTaper = meshes[LegTaperKey],
                ArmTaper = meshes[ArmTaperKey],
                ContactShadowQuad = meshes[ContactShadowQuadKey],
                TricornFlat = meshes[TricornFlatKey],
                BootMaterial = BuildBootMaterial(),
                ContactShadowMaterial = BuildContactShadowMaterial(),
            };
        }

        /// <summary>深棕靴材质（N19）：与其它角色材质同走 PirateOutline（否则靴子没有描边，破 R-9）。</summary>
        static Material BuildBootMaterial()
        {
            string path = CrewMaterialFolder + "/" + BootMaterialFileName + ".mat";
            Material material = LoadOrCreateMaterial(path, BootMaterialFileName, ResolveOutlineShader());
            ApplyOutlineUnitMaterial(material, BootColor);
            EditorUtility.SetDirty(material);
            return material;
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
        /// 自写 shader 又超出本波次允许改动的文件范围；64² 单通道遮罩成本可忽略，且贴图可手改替换。
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
            // 选中虚线密度：见 DashFrequencySelected 的推导（r4 的"部件 0 青色"根因）。
            if (material.HasProperty("_DashFrequency"))
                material.SetFloat("_DashFrequency", DashFrequencySelected);
            if (material.HasProperty("_DebugMode"))
                material.SetFloat("_DebugMode", 0f);
        }

        /// <summary>
        /// 核对预制体里每个**角色部件**（排除贴地接触阴影面片）的材质是否带 <c>_OutlineState</c>。
        ///
        /// 【为什么要有这一步（r6）】r4/r5 两次复验都出现"帽/头/臂选中时不发青"，排查路径一直是
        /// "读 prefab YAML 对材质 guid → 看 .mat 有没有 _OutlineState"。那个结论在 r6 已被证伪
        /// （12 个 Crew 材质全带该属性，部件全部命中），但如果将来有人把一个部件换成
        /// PirateSurface / URP-Lit 材质，就会**静默**丢掉描边（MPB 写不存在的属性不报错）。
        /// 故在这里做一次建预制体期的硬核对，命中缺属性的部件就 **Debug.LogError** 点名列出
        /// （ArtGate/批处理会把它暴露出来），不让它溜到运行时。
        /// 【实测结果】r6 全量核对 7 个预制体：**0 命中**——所有角色部件本就统一走带 _OutlineState 的
        /// 12 个 Crew 材质（唯一不带的是 ContactShadow，被本方法与 binder 一致地排除），
        /// 故这里没有"换绑"动作可做，存在的意义是不让将来的静默回归发生。
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
                + firstOffender + "。请把这些部件换成 PirateOutline shader 的 Crew 材质"
                + "（ApplyOutlineUnitMaterial 生成的那批），否则 r4/r5 的\"部分部件无青\"会复发。");
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
            VisualAddOns addOns, out int rendererCount, out int triangles, out int heldFixes)
        {
            rendererCount = 0;
            triangles = 0;
            heldFixes = 0;

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

            // N19：腿/臂锥化 + 靴块/手掌块（只换 mesh 与零件缩放，不动枢轴 → 动画链路零影响）。
            ApplyRealisticLimbs(visual.transform, assetSet, addOns);

            // r5：三角帽塑形 + 鼻尖/眼下移 + 手持武器不穿地（同样只改装配后的 mesh/Transform）。
            ApplyHeadDetails(visual.transform, assetSet, addOns);
            heldFixes = ApplyHeldWeaponFloorFit(visual.transform);

            // N7：脚底接触阴影面片（单位根的子物体 → 跟随位移，零运行时代码）。
            AddContactShadow(root.transform, addOns);

            // 阵营色部件：显式写进 binder，并拆掉必然变成 missing script 的运行时标记组件。
            Renderer[] tintRenderers = CollectAndStripTintMarkers(root);

            // 统计实测三角面（用网格资产数据算，不靠估算）。
            var filters = root.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                Mesh mesh = filters[i].sharedMesh;
                if (mesh != null)
                    triangles += mesh.triangles.Length / 3;
            }
            rendererCount = root.GetComponentsInChildren<MeshRenderer>(true).Length;

            // r6：建完立刻核一遍"每个角色部件的材质是否都带 _OutlineState"（缺的报错点名）。
            // 这一步把 r4/r5 反复出现的"部分部件选中无青"从"运行时靠肉眼/读 YAML 排查"提前到
            // 建预制体阶段（ArtGate 会把这行错误暴露出来），见 VerifyOutlineMaterials。
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
        // N19：四肢写实化（装配后换网格/缩放，不改枢轴）
        // ------------------------------------------------------------------

        /// <summary>
        /// 腿/臂由等径圆柱改"上粗下细"圆台，靴块加厚前出头，手由小球改手掌块。
        /// 只写 <c>MeshFilter.sharedMesh</c> 与零件 <c>localScale/localPosition</c>：
        /// 枢轴层级、动画插值的基准值（<c>CrewVisualAnimator.Awake</c> 记的是枢轴，不是零件）都不受影响。
        /// </summary>
        static void ApplyRealisticLimbs(Transform visual, CrewVisualAssetSet assets, VisualAddOns addOns)
        {
            ApplyLeg(visual, assets, addOns, "LegL", "BootL");
            ApplyLeg(visual, assets, addOns, "LegR", "BootR");
            ApplyArm(visual, assets, addOns, "ArmL", "HandL");
            ApplyArm(visual, assets, addOns, "ArmR", "HandR");
        }

        static void ApplyLeg(Transform visual, CrewVisualAssetSet assets, VisualAddOns addOns,
            string legName, string bootName)
        {
            Transform leg = FindDeep(visual, legName);
            if (leg == null)
            {
                Debug.LogWarning("[CrewVisualPrefabBuilder] 找不到腿部零件 " + legName + "，N19 锥化跳过该部件。");
                return;
            }

            float radius = leg.localScale.x;
            float height = leg.localScale.y;
            var legFilter = leg.GetComponent<MeshFilter>();
            if (legFilter != null)
                legFilter.sharedMesh = addOns.LegTaper;     // 与 Cylinder 同口径（半径 1/高 1），缩放语义不变
            leg.localScale = new Vector3(radius, height, radius);

            Transform boot = FindDeep(visual, bootName);
            if (boot == null)
                return;

            // 靴块：高 0.034、宽 2.9r、深 4.1r、向 +Z 前伸 —— 从"脚垫"变成能读出朝向的靴子。
            boot.localScale = new Vector3(radius * BootWidthMul, BootHeight, radius * BootDepthMul);
            boot.localPosition = new Vector3(0f, BootHeight * 0.5f, BootForward);

            var bootRenderer = boot.GetComponent<MeshRenderer>();
            if (bootRenderer != null && bootRenderer.sharedMaterial == assets.For(CrewMaterialRole.Leather))
            {
                // 只有默认皮革靴换深棕；骷髅的靴子是骨色（R-11 无皮肤色纪律之外的骨色体系）保持不动。
                bootRenderer.sharedMaterial = addOns.BootMaterial;
            }
        }

        static void ApplyArm(Transform visual, CrewVisualAssetSet assets, VisualAddOns addOns,
            string armName, string handName)
        {
            Transform arm = FindDeep(visual, armName);
            if (arm == null)
            {
                Debug.LogWarning("[CrewVisualPrefabBuilder] 找不到手臂零件 " + armName + "，N19 锥化跳过该部件。");
                return;
            }

            var armFilter = arm.GetComponent<MeshFilter>();
            float height = arm.localScale.y;
            // 胶囊网格（狙击手细臂）的半径约定是 0.5，圆柱是 1.0：换网格前先把半径换算回世界值，
            // 否则狙击手的臂会粗一倍。
            bool capsuleMesh = armFilter != null && armFilter.sharedMesh == assets.Capsule;
            float radius = capsuleMesh ? arm.localScale.x * 0.5f : arm.localScale.x;
            if (armFilter != null)
                armFilter.sharedMesh = addOns.ArmTaper;
            arm.localScale = new Vector3(radius, height, radius);

            Transform hand = FindDeep(visual, handName);
            if (hand == null)
                return;

            float handRadius = hand.localScale.x;      // SphereSmall 是半径 1 的球，scale.x 即手半径
            var handFilter = hand.GetComponent<MeshFilter>();
            if (handFilter != null)
                handFilter.sharedMesh = assets.Box;    // 球手 → 手掌块（写实向的低模拳头）
            hand.localScale = new Vector3(handRadius * HandWidthMul,
                handRadius * HandHeightMul, handRadius * HandDepthMul);
        }

        // ------------------------------------------------------------------
        // r5：三角帽塑形 + 脸（鼻尖 / 眼下移）
        // ------------------------------------------------------------------

        /// <summary>
        /// 帽子与脸（r5 起草 / r6 按复验重做）：
        ///   · <b>三角帽</b>：把**所有布帽件**（<c>Bandana</c> 与 <c>Tricorn</c>）统一换成
        ///     <see cref="TricornFlatKey"/> 网格（压扁帽冠 + 三片 46° 上翻帽檐，外缘 1.05×头半径），
        ///     并归位到 <c>HeadRadius×0.80</c>（帽冠底口与头球相切的高度）、清零歪戴角。
        ///     <b>为什么不能只改 Tricorn</b>：r5 就是这么做的，但特写主体是**非船长**职业
        ///     （红队 FirstAlive = 水手），它的帽子叫 <c>Bandana</c>、网格是
        ///     <c>Frustum(top 0.055, bottom 0.082)</c> 的**下翻圆台**——低圆冠 + 下翻宽檐，
        ///     正是复验读到的"红桶帽/贝雷帽"；r5 的三角帽塑形从未作用到它身上。
        ///   · <b>删左侧圆疙瘩</b>：确认归属为 <c>BandanaKnot</c>（SphereSmall + 阵营布，r0.021，
        ///     仅水手有，挂在 <c>HeadPivot</c> 的 (0.055, 0.55r, −0.048)）——复验把它读作"肉瘤"。
        ///   · <b>鼻尖</b>：长度减半（<see cref="NoseLengthMul"/> 0.22→0.11）并**贴回球面**
        ///     （中心按 <c>z = √(r²−y²)×0.99</c> 放在头球表面，与眼下移同口径），消除"鸟喙感"。
        ///   · <b>眼</b>：下移到 <see cref="EyeHeightOffset"/> 并按球面重算 z（r5 已做，保留）。
        /// 只动 <c>HeadPivot</c> 下的帽子/鼻/眼，不动任何枢轴，动画链路零影响。
        /// </summary>
        static void ApplyHeadDetails(Transform visual, CrewVisualAssetSet assets, VisualAddOns addOns)
        {
            Transform head = FindDeep(visual, "Head");
            Transform headPivot = FindDeep(visual, "HeadPivot");
            float headRadius = head != null ? Mathf.Abs(head.localScale.x) : ReferenceHeadRadius;
            if (headRadius < 0.02f)
                headRadius = ReferenceHeadRadius;
            float headRatio = headRadius / ReferenceHeadRadius;

            // ---- 三角帽（所有布帽件统一换网格；TricornFlat 按基准头半径建模，故按比例缩放）----
            ReshapeHat(FindDeep(visual, "Tricorn"), addOns, headRatio);
            ReshapeHat(FindDeep(visual, "Bandana"), addOns, headRatio);

            // ---- 删掉左侧圆疙瘩（BandanaKnot，仅水手；归属见方法头）----
            Transform knot = FindDeep(visual, "BandanaKnot");
            if (knot != null)
                Object.DestroyImmediate(knot.gameObject);

            NudgeEyesDown(visual, headRadius);

            // ---- 鼻尖（缩 50% + 贴回球面）----
            if (headPivot == null || head == null)
                return;

            var headRenderer = head.GetComponent<MeshRenderer>();
            Material headMaterial = headRenderer != null && headRenderer.sharedMaterial != null
                ? headRenderer.sharedMaterial
                : assets.For(CrewMaterialRole.Skin);

            float noseY = NoseHeightOffset * headRatio;
            // 贴回球面：把鼻心放到该高度处的球面上（×0.99 与 NudgeEyesDown 同口径），
            // 使鼻根埋进球里、只留半个椭球的小凸起（r5 是 z=0.92r 的悬空前伸 → 鸟喙）。
            float noseSurfaceZ = Mathf.Sqrt(Mathf.Max(headRadius * headRadius - noseY * noseY, 0f)) * 0.99f;
            float noseLength = headRadius * NoseLengthMul * 2f;
            AddSimplePart(headPivot, "Nose", assets.SphereSmall, headMaterial,
                new Vector3(0f, noseY, noseSurfaceZ),
                new Vector3(noseLength * 0.62f, noseLength * 0.52f, noseLength));
        }

        /// <summary>
        /// 把一个布帽件换成三角帽网格并归位（r6）。找不到件时静默返回 false（骷髅的破头巾 /
        /// 纵火犯的乱发不是帽件，不参与换装）。
        /// </summary>
        static bool ReshapeHat(Transform hat, VisualAddOns addOns, float headRatio)
        {
            if (hat == null || addOns.TricornFlat == null)
                return false;

            var filter = hat.GetComponent<MeshFilter>();
            if (filter != null)
                filter.sharedMesh = addOns.TricornFlat;

            // 帽冠底口落在头球在 0.80r 处的截面半径上（与原 Tricorn 的装配口径一致）；
            // 网格按基准头半径建模，故整体按 headRatio 缩放，保持"檐 1.05×头半径"的比值（r7）。
            hat.localRotation = Quaternion.identity;   // 原头巾有 12° 歪戴，三角帽不歪
            hat.localPosition = new Vector3(0f, ReferenceHeadRadius * 0.80f * headRatio, 0f);
            hat.localScale = Vector3.one * headRatio;
            return true;
        }

        /// <summary>
        /// 眼睛下移到球面新位置：眼睛原本贴在 y=+0.008 的球面上（z=r×0.86），
        /// 只改 y 会让它陷进球里，故按 <c>z = √(r²−y²)×0.99</c> 重算（保留原 x）。
        /// </summary>
        static void NudgeEyesDown(Transform visual, float headRadius)
        {
            float ratio = headRadius / ReferenceHeadRadius;
            float y = EyeHeightOffset * ratio;
            float z = Mathf.Sqrt(Mathf.Max(headRadius * headRadius - y * y, 0f)) * 0.99f;

            for (int i = 0; i < 2; i++)
            {
                Transform eye = FindDeep(visual, i == 0 ? "EyeL" : "EyeR");
                if (eye == null)
                    continue;
                eye.localPosition = new Vector3(eye.localPosition.x, y, z);
            }
        }

        // ------------------------------------------------------------------
        // r5：手持武器离地（脚下"灰色刀片碎片"根因）
        // ------------------------------------------------------------------

        /// <summary>
        /// 把挂在 <c>HeldL</c>/<c>HeldR</c> 下的**竖直刀身**修到脚底平面（y=−0.25 世界）以上，
        /// 消除 r4 特写里"脚下灰色刀片状碎片"。
        ///
        /// 【根因（r4 复验实测）】碎片不是接触阴影片：阴影片是 y=+0.02、0.6×0.6 的水平半透明 quad、
        ///   颜色近黑 <c>(0.015,0.015,0.020)</c>；而碎片实测取色 <c>(70,67,61)</c>，正是 <c>CrewIron</c>
        ///   基础色 <c>(0.431,0.416,0.388)</c> 在阴影下的值，形状是竖直薄板。
        ///   船长的佩剑 <c>SwordBlade</c>（长 0.150，挂点 HeldR 在视觉空间 y≈0.091，脚底平面 = −0.25 世界）
        ///   → 刀尖到 y≈−0.324，**穿地 0.074**；普通船员的短刀穿 0.019。摄像机俯角 30° 时
        ///   这块竖直薄板在画面里投成一片三角刀片，就是评审看到的碎片。
        ///
        /// 【修法】保持"握把端（上端）不动"，只把刀身缩短到刀尖留白 ≥ <see cref="HeldWeaponFloorClearance"/>：
        ///   既不动枢轴（动画里手/武器相对位置不变），也不动枪管/铁钩等横向件
        ///   （筛选条件：localScale.y 明显大于厚度，且实测最低点确实越界）。
        /// 【代价】船长佩剑由 0.150 缩到 ≈0.062（匕首长度）。要保住长剑，正确做法是抬高握把枢轴
        ///   （<c>CrewVisualRig</c> 侧改动，超出本脚本范围），不能靠让刀穿地。
        /// </summary>
        static int ApplyHeldWeaponFloorFit(Transform visual)
        {
            return FitHeldSubtree(FindDeep(visual, "HeldL")) + FitHeldSubtree(FindDeep(visual, "HeldR"));
        }

        static int FitHeldSubtree(Transform held)
        {
            if (held == null)
                return 0;

            int fixes = 0;
            MeshRenderer[] parts = held.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < parts.Length; i++)
            {
                MeshRenderer renderer = parts[i];
                if (renderer == null)
                    continue;

                Transform part = renderer.transform;
                Vector3 scale = part.localScale;
                // 只处理"竖直细长"的刀身：高度要明显大于厚度，避免误伤枪管(0.011×0.280×0.011 但已转 90°)、
                // 弯钩(scale=1) 这类横向件。
                if (scale.y <= 0.02f || scale.y <= scale.z * 1.5f)
                    continue;

                // 预制体构建期单位根在原点且无旋转 → renderer.bounds 就是世界坐标，脚底平面 = FootPlaneWorldY。
                float bottom = renderer.bounds.min.y;
                float limit = FootPlaneWorldY + HeldWeaponFloorClearance;
                if (bottom >= limit)
                    continue;

                float length = scale.y;
                float top = part.localPosition.y + length * 0.5f;      // 握把端固定
                float newLength = Mathf.Max(length - (limit - bottom), MinHeldBladeLength);
                part.localScale = new Vector3(scale.x, newLength, scale.z);
                part.localPosition = new Vector3(part.localPosition.x, top - newLength * 0.5f,
                    part.localPosition.z);
                fixes++;
            }
            return fixes;
        }

        /// <summary>
        /// 按组装侧 <c>CrewVisualRig.AddPart</c> 的同口径补一个零件（建预制体阶段专用）。
        /// 只用于 r5 的鼻尖这类"装配脚本里没有、又不值得改 rig 的"小件。
        /// </summary>
        static void AddSimplePart(Transform parent, string name, Mesh mesh, Material material,
            Vector3 localPosition, Vector3 localScale)
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

            float planarScale = ContactShadowDecal.DefaultDiameter / UnitRootScale.x;
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
            SetFloat(decalSo, "diameter", ContactShadowDecal.DefaultDiameter);
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

        /// <summary>按名字深度优先找子物体（装配层没有全局索引，零件名在职业间是稳定的）。</summary>
        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name)
                return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDeep(root.GetChild(i), name);
                if (found != null)
                    return found;
            }
            return null;
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
