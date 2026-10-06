using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PirateCrew.Visual
{
    /// <summary>
    /// 标记组件：挂在"阵营色部件"的渲染器所在 GameObject 上。
    /// <c>UnitOutlineBinder</c> 只对这些渲染器写阵营色 <c>_BaseColor</c>，
    /// 其余部件（皮肤/铁/木/骨/皮革）保留各自材质的基础色，
    /// 满足 docs/设计/美术/角色造型.md §2.1「阵营色不得污染肤色/铁/木部件」的纪律。
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
    /// 【层级与枢轴】（坐标口径见 docs/设计/3D空间模型对齐.md：脚底 y=0、枢轴 y=0.25）
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
        // 通用比例常量（docs/设计/美术/角色造型.md §1.2）
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
        public static CrewVisualRig Build(Transform visualRoot, CrewVisualAssetSet assets)
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
            rig._outlineRenderers = null;
            rig._teamTintRenderers = null;

            rig.body = NewPivot(visualRoot, "Body", Vector3.zero);

            // 单一外观档（2026-10-05 创始人裁决：职业外观塌缩）——先按默认比例立起全套零件，
            // 两件式装配（ApplyTwoPieceSilhouette）随即只保三枢轴并整体重建为圆台柱 + 圆球。
            BuildCore(rig, assets, DefaultProportions(assets));

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
