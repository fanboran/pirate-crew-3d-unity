using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PirateCrew.Visual
{
    /// <summary>
    /// 标记组件：挂在"阵营色部件"的渲染器所在 GameObject 上。
    /// <c>UnitOutlineBinder</c> 只对这些渲染器写阵营色 <c>_BaseColor</c>，
    /// 其余部件（皮肤/铁/木/骨/皮革）保留各自材质的基础色，
    /// 满足 docs/角色造型规范.md §2.1「阵营色不得污染肤色/铁/木部件」的纪律。
    /// </summary>
    public sealed class CrewTeamTintPart : MonoBehaviour
    {
    }

    /// <summary>
    /// 角色装配所需的网格与材质集合（由编辑器脚本 <c>CrewVisualPrefabBuilder</c> 生成后注入）。
    /// 网格按"单位形状复用"策略共享：圆柱/球/盒是单位尺寸，靠 Transform 缩放成具体零件，
    /// 只有躯干/头巾/三角帽等"形状本身就是特征"的零件用专用网格。
    /// </summary>
    public sealed class CrewVisualAssetSet
    {
        /// <summary>单位球（半径 1，10×6 低模）。</summary>
        public Mesh Sphere;

        /// <summary>小号单位球（半径 1，6×4 低模）——手/眼/球串等小件用，省三角面。</summary>
        public Mesh SphereSmall;

        /// <summary>单位圆柱（半径 1、高 1，带端盖）。</summary>
        public Mesh Cylinder;

        /// <summary>单位胶囊（半径 0.5、圆柱段高 1，总高 2）。</summary>
        public Mesh Capsule;

        /// <summary>单位盒（1×1×1）。</summary>
        public Mesh Box;

        /// <summary>标准躯干圆台（上 r0.052 / 下 r0.095 / h0.200）。</summary>
        public Mesh TorsoStandard;

        /// <summary>加宽躯干圆台（炮手，下 r0.115）。</summary>
        public Mesh TorsoWide;

        /// <summary>收窄躯干圆台（狙击手，下 r0.080）。</summary>
        public Mesh TorsoNarrow;

        /// <summary>胸腔圆台（骷髅，上 r0.045 / 下 r0.070 / h0.185）。</summary>
        public Mesh TorsoRib;

        /// <summary>加长躯干圆台（船长，h0.210）。</summary>
        public Mesh TorsoCaptain;

        /// <summary>标准头巾（顶 r0.055 / 底 r0.082 / h0.045）。</summary>
        public Mesh BandanaStandard;

        /// <summary>紧头巾（炮手，h0.040）。</summary>
        public Mesh BandanaTight;

        /// <summary>低头巾（狙击手，h0.035）。</summary>
        public Mesh BandanaLow;

        /// <summary>三角帽（船长专属自定义网格）。</summary>
        public Mesh Tricorn;

        /// <summary>长大衣下摆（船长，下摆外扩 r0.115 / h0.150）。</summary>
        public Mesh CoatSkirt;

        /// <summary>铁钩（270° 弯管）。</summary>
        public Mesh Hook;

        /// <summary>肋骨半环（180° 弯管）。</summary>
        public Mesh Rib;

        /// <summary>胡须球簇（大胡子，合并单网格）。</summary>
        public Mesh Beard;

        /// <summary>乱发球簇（纵火狂）。</summary>
        public Mesh HairClump;

        /// <summary>按角色取共享材质（索引 = <see cref="CrewMaterialRole"/>）。</summary>
        public Material[] Materials;

        /// <summary>取材质角色对应的共享材质；缺失返回 null。</summary>
        public Material For(CrewMaterialRole role)
        {
            int index = (int)role;
            if (Materials == null || index < 0 || index >= Materials.Length)
                return null;
            return Materials[index];
        }
    }

    /// <summary>
    /// 角色视觉装配根（挂在单位的 <c>Visual</c> 子节点上），持有动画层需要的部件引用。
    ///
    /// 【层级与枢轴】（坐标口径见 docs/M2-3D空间模型对齐.md：脚底 y=0、枢轴 y=0.25）
    /// <code>
    /// 单位根（BoxCollider 1×1×1，scale 0.375/0.5/0.375 → 世界 AABB 0.375×0.5×0.375）
    /// └ Visual（本组件；localPos (0,-0.5,0)、localScale (2.6667,2,2.6667)）
    ///    └ Body（脚底枢轴，用于整体倒地）
    ///       ├ TorsoPivot（腰线 y=腿高，用于呼吸缩放/前倾）
    ///       │  ├ Torso / Belt / 配件
    ///       │  ├ HeadPivot（头心 y≈0.3675）
    ///       │  ├ ArmPivotL/R（肩 y≈0.240）→ Hand / Held
    ///       └ LegPivotL/R（胯 ±0.035, y=0）
    /// </code>
    ///
    /// 【为什么 Visual 用非均匀缩放】单位根是 0.375/0.5/0.375 的非均匀缩放（碰撞盒契约），
    /// 若把角色零件直接挂根下会被压扁；Visual 用 (1/0.375, 1/0.5, 1/0.375) 抵消，
    /// 使正常数矩阵 = 单位阵，零件按世界单位建模（1 单位 = 32px）。对角缩放矩阵相乘抵消后
    /// 再叠旋转**不会产生剪切**，所以动画可以在 Visual 以下任意节点安全旋转。
    /// 矩阵口径逆推：world = pivot + (localPos + localScale * partPos)，故 Visual.localPos.y = −0.5。
    ///
    /// 【与描边链路的关系】所有零件共用 <c>PirateCrew/PirateOutline</c> 材质，
    /// 因此每个 renderer 都带本体 Pass + 描边 Pass；<c>UnitOutlineBinder</c> 收集全部子 renderer
    /// 写 <c>_OutlineState</c>，并对 <see cref="TeamTintRenderers"/> 额外写阵营色 <c>_BaseColor</c>。
    /// </summary>
    public sealed class CrewVisualRig : MonoBehaviour
    {
        // ------------------------------------------------------------------
        // 通用比例常量（docs/角色造型规范.md §1.2）
        // ------------------------------------------------------------------

        /// <summary>标准腿高（§1.2：0.090）。</summary>
        public const float LegHeight = 0.090f;

        /// <summary>腿半径（§1.2：0.022）。</summary>
        public const float LegRadius = 0.022f;

        /// <summary>腿左右间距（胯宽一半）。</summary>
        public const float LegOffsetX = 0.035f;

        /// <summary>标准躯干高（§1.2：0.200）。</summary>
        public const float TorsoHeight = 0.200f;

        /// <summary>腰带中心 y（§1.2：底 0.150 + h0.030/2）。</summary>
        public const float BeltCenterY = 0.165f;

        /// <summary>腰带半径（§1.2：0.098）。</summary>
        public const float BeltRadius = 0.098f;

        /// <summary>标准头球半径（§1.2：d0.155 → r0.0775）。</summary>
        public const float HeadRadius = 0.0775f;

        /// <summary>标准头球中心 y（§1.2：0.3675）。</summary>
        public const float HeadCenterY = 0.3675f;

        /// <summary>肩高（§1.2：≈0.240）。</summary>
        public const float ShoulderY = 0.240f;

        /// <summary>肩左右间距（手臂挂点 x）。</summary>
        public const float ShoulderOffsetX = 0.070f;

        /// <summary>标准手臂高（§1.2：0.110）。</summary>
        public const float ArmHeight = 0.110f;

        /// <summary>标准手臂半径（§1.2：0.020）。</summary>
        public const float ArmRadius = 0.020f;

        /// <summary>标准手球半径（§1.2：d0.040 → r0.020）。</summary>
        public const float HandRadius = 0.020f;

        // ------------------------------------------------------------------
        // 序列化部件引用（动画层用；由 Build 直接赋值）
        // ------------------------------------------------------------------

        [Header("职业外观档")]
        [SerializeField] CrewProfession profession;

        [Header("动画枢轴")]
        [SerializeField] Transform body;
        [SerializeField] Transform torsoPivot;
        [SerializeField] Transform headPivot;
        [SerializeField] Transform armLPivot;
        [SerializeField] Transform armRPivot;
        [SerializeField] Transform legLPivot;
        [SerializeField] Transform legRPivot;
        [SerializeField] Transform heldL;
        [SerializeField] Transform heldR;

        [Header("本体渲染器（用于受击白闪）")]
        [SerializeField] Renderer torsoRenderer;

        List<Renderer> _outlineRenderers;
        List<Renderer> _teamTintRenderers;

        /// <summary>外观档。</summary>
        public CrewProfession Profession => profession;

        /// <summary>整体枢轴（脚底，倒地/位移表现用；**不可旋转 Visual 本身**，见类头）。</summary>
        public Transform Body => body;

        /// <summary>腰线枢轴（呼吸缩放 / 前倾）。</summary>
        public Transform TorsoPivot => torsoPivot;

        /// <summary>头枢轴（点头 / 上下浮动）。</summary>
        public Transform HeadPivot => headPivot;

        /// <summary>左臂枢轴。</summary>
        public Transform ArmLPivot => armLPivot;

        /// <summary>右臂枢轴。</summary>
        public Transform ArmRPivot => armRPivot;

        /// <summary>左腿枢轴。</summary>
        public Transform LegLPivot => legLPivot;

        /// <summary>右腿枢轴。</summary>
        public Transform LegRPivot => legRPivot;

        /// <summary>左手持物挂点。</summary>
        public Transform HeldL => heldL;

        /// <summary>右手持物挂点。</summary>
        public Transform HeldR => heldR;

        /// <summary>躯干渲染器（受击白闪参考）。</summary>
        public Renderer TorsoRenderer => torsoRenderer;

        /// <summary>单位全部可视 renderer（含配件/手持物）；描边 binder 的收集集合。</summary>
        public IReadOnlyList<Renderer> OutlineRenderers
        {
            get
            {
                EnsureCached();
                return _outlineRenderers;
            }
        }

        /// <summary>阵营色部件 renderer 子集（运行时由 binder 写队伍色 _BaseColor）。</summary>
        public IReadOnlyList<Renderer> TeamTintRenderers
        {
            get
            {
                EnsureCached();
                return _teamTintRenderers;
            }
        }

        void EnsureCached()
        {
            if (_outlineRenderers != null)
                return;

            _outlineRenderers = new List<Renderer>(GetComponentsInChildren<Renderer>(true));
            _teamTintRenderers = new List<Renderer>();
            var tintParts = GetComponentsInChildren<CrewTeamTintPart>(true);
            for (int i = 0; i < tintParts.Length; i++)
            {
                var r = tintParts[i].GetComponent<Renderer>();
                if (r != null)
                    _teamTintRenderers.Add(r);
            }
        }

        // ------------------------------------------------------------------
        // 装配
        // ------------------------------------------------------------------

        /// <summary>
        /// 在 <paramref name="visualRoot"/> 上装配指定职业的部件层级并挂上本组件。
        /// **仅供编辑器脚本调用**（内部会 new GameObject / MeshFilter）。
        /// </summary>
        public static CrewVisualRig Build(Transform visualRoot, CrewProfession professionValue, CrewVisualAssetSet assets)
        {
            if (visualRoot == null || assets == null)
                return null;

            // Visual 抵消单位根的非均匀缩放（见类头矩阵口径）。
            visualRoot.localPosition = new Vector3(0f, -0.5f, 0f);
            visualRoot.localScale = new Vector3(1f / 0.375f, 1f / 0.5f, 1f / 0.375f);
            visualRoot.localRotation = Quaternion.identity;

            var rig = visualRoot.gameObject.GetComponent<CrewVisualRig>();
            if (rig == null)
                rig = visualRoot.gameObject.AddComponent<CrewVisualRig>();
            rig.profession = professionValue;
            rig._outlineRenderers = null;
            rig._teamTintRenderers = null;

            rig.body = NewPivot(visualRoot, "Body", Vector3.zero);

            switch (professionValue)
            {
                case CrewProfession.Sailor:
                    BuildSailor(rig, assets);
                    break;
                case CrewProfession.Bombardier:
                    BuildBombardier(rig, assets);
                    break;
                case CrewProfession.Sniper:
                    BuildSniper(rig, assets);
                    break;
                case CrewProfession.Hook:
                    BuildHook(rig, assets);
                    break;
                case CrewProfession.Arsonist:
                    BuildArsonist(rig, assets);
                    break;
                case CrewProfession.Skeleton:
                    BuildSkeleton(rig, assets);
                    break;
                case CrewProfession.Captain:
                    BuildCaptain(rig, assets);
                    break;
            }

            return rig;
        }

        // ---- 通用骨架参数 --------------------------------------------------

        /// <summary>职业微调后的通用比例与配色。</summary>
        struct Proportions
        {
            public float LegHeight;
            public float LegRadius;
            public float TorsoHeight;
            public Mesh TorsoMesh;
            public float HeadRadius;
            public float ArmRadius;
            public float ArmHeight;
            public Mesh ArmMesh;                 // null → 圆柱臂；否则用该网格（胶囊）
            public float HandRadius;

            public CrewMaterialRole HeadMaterial;
            public CrewMaterialRole TorsoMaterial;
            public bool TorsoTeamTint;
            public CrewMaterialRole BeltMaterial;
            public bool BeltTeamTint;
            public CrewMaterialRole LegClothMaterial;
            public CrewMaterialRole BootMaterial;
            public CrewMaterialRole ArmMaterial;
            public CrewMaterialRole HandMaterial;
        }

        static Proportions DefaultProportions(CrewVisualAssetSet assets)
        {
            return new Proportions
            {
                LegHeight = LegHeight,
                LegRadius = LegRadius,
                TorsoHeight = TorsoHeight,
                TorsoMesh = assets.TorsoStandard,
                HeadRadius = HeadRadius,
                ArmRadius = ArmRadius,
                ArmHeight = ArmHeight,
                ArmMesh = null,
                HandRadius = HandRadius,
                HeadMaterial = CrewMaterialRole.Skin,
                TorsoMaterial = CrewMaterialRole.TeamCloth,
                TorsoTeamTint = true,
                BeltMaterial = CrewMaterialRole.TeamCloth,
                BeltTeamTint = true,
                LegClothMaterial = CrewMaterialRole.Leather,
                BootMaterial = CrewMaterialRole.Leather,
                ArmMaterial = CrewMaterialRole.Skin,
                HandMaterial = CrewMaterialRole.Skin,
            };
        }

        /// <summary>
        /// 搭通用骨架：腿（含靴）+ 腰线枢轴 + 躯干 + 腰带 + 头 + 双臂（含手/持物挂点）。
        /// </summary>
        static void BuildCore(CrewVisualRig rig, CrewVisualAssetSet assets, Proportions p)
        {
            Transform bodyNode = rig.body;

            // ---- 腿（含靴） ----
            for (int side = -1; side <= 1; side += 2)
            {
                Transform legPivot = NewPivot(bodyNode, side < 0 ? "LegPivotL" : "LegPivotR",
                    new Vector3(LegOffsetX * side, 0f, 0f));
                AddPart(legPivot, side < 0 ? "LegL" : "LegR", assets.Cylinder,
                    assets.For(p.LegClothMaterial),
                    new Vector3(0f, p.LegHeight * 0.5f, 0f), Vector3.zero,
                    new Vector3(p.LegRadius, p.LegHeight, p.LegRadius));

                AddPart(legPivot, side < 0 ? "BootL" : "BootR", assets.Box,
                    assets.For(p.BootMaterial),
                    new Vector3(0f, 0.014f, 0.008f), Vector3.zero,
                    new Vector3(p.LegRadius * 2.4f, 0.028f, p.LegRadius * 3.4f));

                if (side < 0) rig.legLPivot = legPivot; else rig.legRPivot = legPivot;
            }

            // ---- 腰线枢轴 ----
            float torsoBottomY = p.LegHeight;
            rig.torsoPivot = NewPivot(bodyNode, "TorsoPivot", new Vector3(0f, torsoBottomY, 0f));

            // ---- 躯干 ----
            rig.torsoRenderer = AddPart(rig.torsoPivot, "Torso", p.TorsoMesh, assets.For(p.TorsoMaterial),
                new Vector3(0f, p.TorsoHeight * 0.5f, 0f), Vector3.zero, Vector3.one, p.TorsoTeamTint);

            // ---- 腰带 ----
            AddPart(rig.torsoPivot, "Belt", assets.Cylinder, assets.For(p.BeltMaterial),
                new Vector3(0f, BeltCenterY - torsoBottomY, 0f), Vector3.zero,
                new Vector3(BeltRadius, 0.030f, BeltRadius), p.BeltTeamTint);

            // ---- 头 ----
            rig.headPivot = NewPivot(rig.torsoPivot, "HeadPivot",
                new Vector3(0f, HeadCenterY - torsoBottomY, 0f));
            AddPart(rig.headPivot, "Head", assets.Sphere, assets.For(p.HeadMaterial),
                Vector3.zero, Vector3.zero, Vector3.one * p.HeadRadius);

            // ---- 双臂 ----
            float shoulderRel = ShoulderY - torsoBottomY;
            for (int side = -1; side <= 1; side += 2)
            {
                Transform armPivot = NewPivot(rig.torsoPivot, side < 0 ? "ArmPivotL" : "ArmPivotR",
                    new Vector3(ShoulderOffsetX * side, shoulderRel, 0f));

                Mesh armMesh = p.ArmMesh != null ? p.ArmMesh : assets.Cylinder;
                Vector3 armScale = p.ArmMesh != null
                    ? new Vector3(p.ArmRadius * 2f, p.ArmHeight, p.ArmRadius * 2f) // 胶囊网格半径 0.5
                    : new Vector3(p.ArmRadius, p.ArmHeight, p.ArmRadius);

                AddPart(armPivot, side < 0 ? "ArmL" : "ArmR", armMesh, assets.For(p.ArmMaterial),
                    new Vector3(0f, -p.ArmHeight * 0.5f, 0f), Vector3.zero, armScale);

                AddPart(armPivot, side < 0 ? "HandL" : "HandR", assets.SphereSmall, assets.For(p.HandMaterial),
                    new Vector3(0f, -p.ArmHeight - p.HandRadius * 0.7f, 0f), Vector3.zero, Vector3.one * p.HandRadius);

                Transform held = NewPivot(armPivot, side < 0 ? "HeldL" : "HeldR",
                    new Vector3(0f, -p.ArmHeight - p.HandRadius * 1.6f, 0f));
                if (side < 0) { rig.armLPivot = armPivot; rig.heldL = held; }
                else { rig.armRPivot = armPivot; rig.heldR = held; }
            }
        }

        // ---- 逐职业 --------------------------------------------------------

        static void BuildSailor(CrewVisualRig rig, CrewVisualAssetSet assets)
        {
            Proportions p = DefaultProportions(assets);
            BuildCore(rig, assets, p);

            // 头巾 + 结（阵营色，规格 §3.2）。
            AddPart(rig.headPivot, "Bandana", assets.BandanaStandard, assets.For(CrewMaterialRole.TeamCloth),
                new Vector3(0f, p.HeadRadius * 0.90f, 0f), Vector3.zero, Vector3.one, teamTint: true);
            AddPart(rig.headPivot, "BandanaKnot", assets.SphereSmall, assets.For(CrewMaterialRole.TeamCloth),
                new Vector3(0.055f, p.HeadRadius * 0.55f, -0.048f), Vector3.zero,
                new Vector3(0.021f, 0.021f, 0.021f), teamTint: true);

            AddEyePair(rig.headPivot, assets, p.HeadRadius);

            // 右手短刀（铁刃 + 木柄，规格 §3.2）。
            AddKnife(rig.heldR, assets, 0.110f, 0.016f);
        }

        static void BuildBombardier(CrewVisualRig rig, CrewVisualAssetSet assets)
        {
            Proportions p = DefaultProportions(assets);
            p.TorsoMesh = assets.TorsoWide;
            p.ArmRadius = 0.026f;
            p.ArmHeight = 0.115f;
            p.HandRadius = 0.023f;
            BuildCore(rig, assets, p);

            AddPart(rig.headPivot, "Bandana", assets.BandanaTight, assets.For(CrewMaterialRole.TeamCloth),
                new Vector3(0f, p.HeadRadius * 0.86f, 0f), Vector3.zero, Vector3.one, teamTint: true);
            AddEyePair(rig.headPivot, assets, p.HeadRadius);

            float torsoBottomY = p.LegHeight;

            // 肩甲（压扁球，补"最宽肩"，规格 §3.3）。
            for (int side = -1; side <= 1; side += 2)
            {
                AddPart(rig.torsoPivot, side < 0 ? "ShoulderPadL" : "ShoulderPadR", assets.SphereSmall,
                    assets.For(CrewMaterialRole.Leather),
                    new Vector3(ShoulderOffsetX * side * 0.95f, ShoulderY - torsoBottomY + 0.008f, 0f),
                    Vector3.zero, new Vector3(0.042f, 0.026f, 0.042f));
            }

            // 皮革围裙（前挂薄盒，规格 §3.3）。
            AddPart(rig.torsoPivot, "Apron", assets.Box, assets.For(CrewMaterialRole.Leather),
                new Vector3(0f, 0.045f, 0.070f), Vector3.zero, new Vector3(0.130f, 0.110f, 0.016f));

            // 火药袋（小深色球 + 口，规格 §3.3）。
            AddPart(rig.torsoPivot, "PowderBag", assets.SphereSmall, assets.For(CrewMaterialRole.Dark),
                new Vector3(-0.070f, 0.060f, 0.045f), Vector3.zero, new Vector3(0.028f, 0.030f, 0.028f));
            AddPart(rig.torsoPivot, "PowderBagCollar", assets.Cylinder, assets.For(CrewMaterialRole.Dark),
                new Vector3(-0.070f, 0.084f, 0.045f), Vector3.zero, new Vector3(0.012f, 0.018f, 0.012f));

            // 双手抱炸弹（腹前中心，规格 §3.3）。挂在身体上而非手挂点，保证"抱"的姿态稳定。
            AddPart(rig.body, "Bomb", assets.SphereSmall, assets.For(CrewMaterialRole.Dark),
                new Vector3(0f, 0.150f, 0.075f), Vector3.zero, Vector3.one * 0.025f);
            AddPart(rig.body, "BombFuse", assets.Cylinder, assets.For(CrewMaterialRole.Flame),
                new Vector3(0.014f, 0.176f, 0.075f), Vector3.zero, new Vector3(0.004f, 0.028f, 0.004f));
        }

        static void BuildSniper(CrewVisualRig rig, CrewVisualAssetSet assets)
        {
            Proportions p = DefaultProportions(assets);
            p.LegRadius = 0.019f;
            p.LegHeight = 0.100f;
            p.TorsoMesh = assets.TorsoNarrow;
            p.TorsoHeight = 0.205f;
            p.HeadRadius = 0.0725f;              // d0.145
            p.ArmRadius = 0.017f;
            p.ArmHeight = 0.112f;
            p.ArmMesh = assets.Capsule;          // 细胶囊臂
            p.HandRadius = 0.018f;
            BuildCore(rig, assets, p);

            AddPart(rig.headPivot, "Bandana", assets.BandanaLow, assets.For(CrewMaterialRole.TeamCloth),
                new Vector3(0f, p.HeadRadius * 0.80f, 0f), Vector3.zero, Vector3.one, teamTint: true);

            // 单眼眼罩（压扁深色球 + 细带，规格 §3.4）。
            AddPart(rig.headPivot, "EyePatch", assets.SphereSmall, assets.For(CrewMaterialRole.Dark),
                new Vector3(-0.030f, 0.008f, 0.062f), Vector3.zero, new Vector3(0.019f, 0.019f, 0.010f));
            AddPart(rig.headPivot, "EyePatchStrap", assets.Box, assets.For(CrewMaterialRole.Dark),
                new Vector3(0f, 0.008f, 0.055f), Vector3.zero, new Vector3(0.150f, 0.007f, 0.006f));

            // 长管火枪（长 0.28 = 身高 0.56 倍，规格 §3.4）：管 + 木托。
            Transform held = rig.heldR;
            AddPart(held, "MusketBarrel", assets.Cylinder, assets.For(CrewMaterialRole.Iron),
                new Vector3(0f, -0.070f, 0.020f), new Vector3(0f, 0f, 90f),
                new Vector3(0.011f, 0.280f, 0.011f));
            AddPart(held, "MusketStock", assets.Box, assets.For(CrewMaterialRole.Wood),
                new Vector3(0f, -0.008f, 0.020f), new Vector3(0f, 0f, 90f),
                new Vector3(0.026f, 0.075f, 0.030f));
        }

        static void BuildHook(CrewVisualRig rig, CrewVisualAssetSet assets)
        {
            Proportions p = DefaultProportions(assets);
            p.LegRadius = 0.021f;
            p.LegHeight = 0.095f;
            p.ArmRadius = 0.019f;
            p.ArmHeight = 0.110f;
            BuildCore(rig, assets, p);

            // 头巾歪戴 12°（规格 §3.5）。
            AddPart(rig.headPivot, "Bandana", assets.BandanaStandard, assets.For(CrewMaterialRole.TeamCloth),
                new Vector3(0f, p.HeadRadius * 0.88f, 0f), new Vector3(0f, 0f, 12f), Vector3.one, teamTint: true);
            AddEyePair(rig.headPivot, assets, p.HeadRadius);

            // 左腿木腿（木 + 铁箍，规格 §3.5）：叠在通用左腿外侧。
            Transform leftLeg = rig.legLPivot;
            AddPart(leftLeg, "WoodLeg", assets.Cylinder, assets.For(CrewMaterialRole.Wood),
                new Vector3(0f, 0.050f, 0f), Vector3.zero, new Vector3(0.024f, 0.100f, 0.024f));
            AddPart(leftLeg, "WoodLegBand", assets.Cylinder, assets.For(CrewMaterialRole.Iron),
                new Vector3(0f, 0.030f, 0f), Vector3.zero, new Vector3(0.027f, 0.010f, 0.027f));

            // 铁钩替代左手（270° 弯管），挂在左手挂点。
            AddPart(rig.heldL, "Hook", assets.Hook, assets.For(CrewMaterialRole.Iron),
                new Vector3(-0.010f, -0.010f, 0f), new Vector3(90f, 0f, 0f), Vector3.one);

            // 麻绳腰带 + 绳结（规格 §3.5）。
            AddPart(rig.torsoPivot, "RopeBelt", assets.Cylinder, assets.For(CrewMaterialRole.Leather),
                new Vector3(0f, BeltCenterY - p.LegHeight + 0.010f, 0f), Vector3.zero,
                new Vector3(BeltRadius * 1.02f, 0.018f, BeltRadius * 1.02f));
            AddPart(rig.torsoPivot, "RopeKnot", assets.SphereSmall, assets.For(CrewMaterialRole.Leather),
                new Vector3(0.060f, BeltCenterY - p.LegHeight + 0.010f, 0.055f), Vector3.zero,
                new Vector3(0.018f, 0.018f, 0.018f));

            // 右手短刀。
            AddKnife(rig.heldR, assets, 0.110f, 0.016f);
        }

        static void BuildArsonist(CrewVisualRig rig, CrewVisualAssetSet assets)
        {
            Proportions p = DefaultProportions(assets);
            p.ArmRadius = 0.021f;
            BuildCore(rig, assets, p);

            // 乱发：头巾 + 3 簇小球（阵营色，规格 §3.6）。
            AddPart(rig.headPivot, "Hair", assets.BandanaStandard, assets.For(CrewMaterialRole.TeamCloth),
                new Vector3(0f, p.HeadRadius * 0.86f, 0f), Vector3.zero, Vector3.one, teamTint: true);
            for (int k = 0; k < 3; k++)
            {
                float a = k * 2.094f;
                AddPart(rig.headPivot, "HairClump" + k, assets.HairClump, assets.For(CrewMaterialRole.TeamCloth),
                    new Vector3(Mathf.Cos(a) * 0.055f, p.HeadRadius * 0.55f, Mathf.Sin(a) * 0.055f),
                    new Vector3(0f, -a * Mathf.Rad2Deg, 0f), Vector3.one, teamTint: true);
            }

            // 爆炸状大胡子（球簇，灰白毛色，规格 §3.6）。
            AddPart(rig.headPivot, "Beard", assets.Beard, assets.For(CrewMaterialRole.Fur),
                new Vector3(0f, -0.040f, 0.040f), Vector3.zero, Vector3.one);

            // 火把（右手）：木柄 + 高亮火球（规格 §3.6）。详见 CrewVisualPrefabBuilder 的发光说明。
            AddPart(rig.heldR, "TorchHandle", assets.Cylinder, assets.For(CrewMaterialRole.Wood),
                new Vector3(0f, -0.040f, 0f), Vector3.zero, new Vector3(0.008f, 0.090f, 0.008f));
            AddPart(rig.heldR, "TorchFlame", assets.SphereSmall, assets.For(CrewMaterialRole.Flame),
                new Vector3(0f, 0.014f, 0f), Vector3.zero, Vector3.one * 0.023f);

            // 燃烧瓶（左手）：玻璃瓶身 + 瓶颈 + 布塞（规格 §3.6）。
            AddPart(rig.heldL, "MolotovBody", assets.Cylinder, assets.For(CrewMaterialRole.Glass),
                new Vector3(0f, -0.020f, 0f), Vector3.zero, new Vector3(0.018f, 0.060f, 0.018f));
            AddPart(rig.heldL, "MolotovNeck", assets.Cylinder, assets.For(CrewMaterialRole.Glass),
                new Vector3(0f, 0.016f, 0f), Vector3.zero, new Vector3(0.007f, 0.020f, 0.007f));
            AddPart(rig.heldL, "MolotovCork", assets.SphereSmall, assets.For(CrewMaterialRole.Fur),
                new Vector3(0f, 0.030f, 0f), Vector3.zero, new Vector3(0.008f, 0.010f, 0.008f));

            // 肩挂油壶（小铁圆台，规格 §3.6）。
            AddPart(rig.torsoPivot, "OilFlask", assets.Cylinder, assets.For(CrewMaterialRole.Iron),
                new Vector3(-0.060f, 0.100f, -0.060f), Vector3.zero, new Vector3(0.020f, 0.045f, 0.020f));
        }

        static void BuildSkeleton(CrewVisualRig rig, CrewVisualAssetSet assets)
        {
            Proportions p = DefaultProportions(assets);
            p.LegRadius = 0.018f;
            p.LegHeight = 0.090f;
            p.TorsoMesh = assets.TorsoRib;
            p.TorsoHeight = 0.185f;
            p.HeadRadius = 0.080f;               // d0.160
            p.ArmRadius = 0.016f;
            p.ArmHeight = 0.110f;
            p.HandRadius = 0.016f;
            p.HeadMaterial = CrewMaterialRole.Bone;
            p.TorsoMaterial = CrewMaterialRole.Bone;
            p.TorsoTeamTint = false;
            p.BeltMaterial = CrewMaterialRole.Iron;
            p.BeltTeamTint = false;
            p.LegClothMaterial = CrewMaterialRole.Bone;
            p.BootMaterial = CrewMaterialRole.Bone;
            p.ArmMaterial = CrewMaterialRole.Bone;
            p.HandMaterial = CrewMaterialRole.Bone;
            BuildCore(rig, assets, p);

            // 头心抬到胸腔顶上（胸高 0.185 < 标准躯干）。
            rig.headPivot.localPosition = new Vector3(0f, p.TorsoHeight + p.HeadRadius * 0.72f, 0f);

            // 眼窝（两个黑洞，规格 §3.7）。
            for (int side = -1; side <= 1; side += 2)
            {
                AddPart(rig.headPivot, side < 0 ? "EyeSocketL" : "EyeSocketR", assets.SphereSmall,
                    assets.For(CrewMaterialRole.Dark),
                    new Vector3(0.028f * side, 0.008f, 0.066f), Vector3.zero,
                    new Vector3(0.016f, 0.018f, 0.012f));
            }

            // 下颌（盒，规格 §3.7）。
            AddPart(rig.headPivot, "Jaw", assets.Box, assets.For(CrewMaterialRole.Bone),
                new Vector3(0f, -0.058f, 0.040f), Vector3.zero, new Vector3(0.070f, 0.026f, 0.045f));

            // 肋骨 ×3（半环弯管横跨胸腔，规格 §3.7）。
            for (int k = 0; k < 3; k++)
            {
                AddPart(rig.torsoPivot, "Rib" + k, assets.Rib, assets.For(CrewMaterialRole.Bone),
                    new Vector3(0f, 0.045f + k * 0.038f, 0.010f), Vector3.zero,
                    new Vector3(1.0f, 1.0f, 0.75f));
            }

            // 脊椎（背后 4 小球串，规格 §3.7）。
            for (int k = 0; k < 4; k++)
            {
                AddPart(rig.torsoPivot, "Spine" + k, assets.SphereSmall, assets.For(CrewMaterialRole.Bone),
                    new Vector3(0f, 0.030f + k * 0.036f, -0.052f), Vector3.zero,
                    Vector3.one * 0.013f);
            }

            // 破头巾残片（阵营色，规格 §3.7 / R-11）。
            AddPart(rig.headPivot, "TornBandana", assets.Box, assets.For(CrewMaterialRole.TeamCloth),
                new Vector3(0.020f, p.HeadRadius * 0.72f, -0.010f), new Vector3(0f, 0f, -18f),
                new Vector3(0.090f, 0.014f, 0.070f), teamTint: true);

            // 骨头棒（右手，规格 §3.7）。
            AddPart(rig.heldR, "BoneClub", assets.Cylinder, assets.For(CrewMaterialRole.Bone),
                new Vector3(0f, -0.045f, 0f), Vector3.zero, new Vector3(0.011f, 0.100f, 0.011f));
            AddPart(rig.heldR, "BoneClubKnob", assets.SphereSmall, assets.For(CrewMaterialRole.Bone),
                new Vector3(0f, -0.095f, 0f), Vector3.zero, Vector3.one * 0.018f);
        }

        static void BuildCaptain(CrewVisualRig rig, CrewVisualAssetSet assets)
        {
            Proportions p = DefaultProportions(assets);
            p.LegHeight = 0.100f;                // 比普通船员高约 0.06（规格 §3.8 / R-10）
            p.LegRadius = 0.023f;
            p.TorsoMesh = assets.TorsoCaptain;
            p.TorsoHeight = 0.210f;
            p.HeadRadius = 0.080f;               // d0.160
            p.ArmRadius = 0.026f;
            p.ArmHeight = 0.115f;
            p.HandRadius = 0.021f;
            p.ArmMaterial = CrewMaterialRole.TeamCloth;
            BuildCore(rig, assets, p);

            // 头心相对躯干顶抬升，保证三角帽有落位空间。
            rig.headPivot.localPosition = new Vector3(0f, p.TorsoHeight + p.HeadRadius * 0.92f, 0f);

            // 三角帽（自定义网格，规格 §3.8）。
            AddPart(rig.headPivot, "Tricorn", assets.Tricorn, assets.For(CrewMaterialRole.TeamCloth),
                new Vector3(0f, p.HeadRadius * 0.80f, 0f), Vector3.zero, Vector3.one, teamTint: true);

            // 白胡（球簇，规格 §3.8）。
            AddPart(rig.headPivot, "WhiteBeard", assets.Beard, assets.For(CrewMaterialRole.Fur),
                new Vector3(0f, -0.044f, 0.044f), Vector3.zero, new Vector3(1.1f, 0.9f, 1.0f));

            // 长大衣下摆（外扩圆台，阵营色，规格 §3.8）。
            AddPart(rig.body, "CoatSkirt", assets.CoatSkirt, assets.For(CrewMaterialRole.TeamCloth),
                new Vector3(0f, p.LegHeight + 0.050f, 0f), Vector3.zero, Vector3.one, teamTint: true);

            // 黄铜肩章（压扁球 + 流苏盒，规格 §3.8）。
            float torsoBottomY = p.LegHeight;
            for (int side = -1; side <= 1; side += 2)
            {
                AddPart(rig.torsoPivot, side < 0 ? "EpauletteL" : "EpauletteR", assets.SphereSmall,
                    assets.For(CrewMaterialRole.Brass),
                    new Vector3(ShoulderOffsetX * side, ShoulderY - torsoBottomY + 0.012f, 0f),
                    Vector3.zero, new Vector3(0.052f, 0.026f, 0.052f));
                AddPart(rig.torsoPivot, side < 0 ? "TasselL" : "TasselR", assets.Box,
                    assets.For(CrewMaterialRole.Brass),
                    new Vector3(ShoulderOffsetX * side * 1.15f, ShoulderY - torsoBottomY - 0.020f, 0f),
                    Vector3.zero, new Vector3(0.012f, 0.038f, 0.012f));
            }

            // 袖口环（阵营色，规格 §3.8"袖口加宽"）。
            AddPart(rig.armRPivot, "CuffR", assets.Cylinder, assets.For(CrewMaterialRole.TeamCloth),
                new Vector3(0f, -p.ArmHeight + 0.008f, 0f), Vector3.zero,
                new Vector3(p.ArmRadius * 1.25f, 0.020f, p.ArmRadius * 1.25f), teamTint: true);
            AddPart(rig.armLPivot, "CuffL", assets.Cylinder, assets.For(CrewMaterialRole.TeamCloth),
                new Vector3(0f, -p.ArmHeight + 0.008f, 0f), Vector3.zero,
                new Vector3(p.ArmRadius * 1.25f, 0.020f, p.ArmRadius * 1.25f), teamTint: true);

            // 佩剑（细长盒刀身 + 护手环 + 圆柱柄，规格 §3.8）。
            AddPart(rig.heldR, "SwordBlade", assets.Box, assets.For(CrewMaterialRole.Iron),
                new Vector3(0f, -0.090f, 0f), Vector3.zero, new Vector3(0.014f, 0.150f, 0.004f));
            AddPart(rig.heldR, "SwordGuard", assets.Cylinder, assets.For(CrewMaterialRole.Brass),
                new Vector3(0f, -0.012f, 0f), new Vector3(90f, 0f, 0f), new Vector3(0.008f, 0.050f, 0.008f));
            AddPart(rig.heldR, "SwordHilt", assets.Cylinder, assets.For(CrewMaterialRole.Brass),
                new Vector3(0f, 0.010f, 0f), Vector3.zero, new Vector3(0.008f, 0.036f, 0.008f));
        }

        // ---- 公共零件 ------------------------------------------------------

        static void AddEyePair(Transform headPivot, CrewVisualAssetSet assets, float headRadius)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                AddPart(headPivot, side < 0 ? "EyeL" : "EyeR", assets.SphereSmall, assets.For(CrewMaterialRole.Dark),
                    new Vector3(0.028f * side, 0.008f, headRadius * 0.86f), Vector3.zero,
                    new Vector3(0.012f, 0.014f, 0.008f));
            }
        }

        /// <summary>短刀：铁刃 + 木柄，挂在手挂点上（刃朝下，规格 §3.2）。</summary>
        static void AddKnife(Transform held, CrewVisualAssetSet assets, float bladeLength, float bladeWidth)
        {
            if (held == null)
                return;
            AddPart(held, "KnifeBlade", assets.Box, assets.For(CrewMaterialRole.Iron),
                new Vector3(0f, -bladeLength * 0.5f, 0f), Vector3.zero,
                new Vector3(bladeWidth, bladeLength, 0.004f));
            AddPart(held, "KnifeHandle", assets.Cylinder, assets.For(CrewMaterialRole.Wood),
                new Vector3(0f, 0.018f, 0f), Vector3.zero, new Vector3(0.009f, 0.040f, 0.009f));
        }

        // ---- 层级工具 ------------------------------------------------------

        static Transform NewPivot(Transform parent, string name, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            return go.transform;
        }

        static MeshRenderer AddPart(Transform parent, string name, Mesh mesh, Material material,
            Vector3 localPosition, Vector3 localEuler, Vector3 localScale, bool teamTint = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(localEuler);
            go.transform.localScale = localScale;

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;

            if (teamTint)
                go.AddComponent<CrewTeamTintPart>();

            return renderer;
        }
    }
}
